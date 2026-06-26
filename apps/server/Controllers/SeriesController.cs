using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Mangrove.Server.Readers;
using Mangrove.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/series")]
[Authorize]
public sealed class SeriesController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly AccessService _access;
    private readonly ServerPaths _paths;
    private readonly LibrarySidecarWriter _sidecar;
    private readonly Metadata.AniListMetadataService _online;
    private readonly ILogger<SeriesController> _logger;
    public SeriesController(
        MangroveDbContext db,
        AccessService access,
        ServerPaths paths,
        LibrarySidecarWriter sidecar,
        Metadata.AniListMetadataService online,
        ILogger<SeriesController> logger)
    {
        _db = db;
        _access = access;
        _paths = paths;
        _sidecar = sidecar;
        _online = online;
        _logger = logger;
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SeriesDetailDto>> Get(int id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        if (!await _access.CanAccessSeriesAsync(userId, User.IsAdmin(), id, ct)) return NotFound();

        var series = await _db.Series
            .Include(s => s.Volumes).ThenInclude(v => v.Chapters)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series is null) return NotFound();

        return Ok(await BuildDetailAsync(series, userId, ct));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SeriesDetailDto>> Update(int id, UpdateSeriesRequest req, CancellationToken ct)
    {
        var series = await _db.Series
            .Include(s => s.Volumes).ThenInclude(v => v.Chapters)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) { series.Name = req.Name.Trim(); series.SortName = req.Name.Trim(); }
        series.Summary = req.Summary;
        series.Publisher = req.Publisher;
        series.Language = req.Language;
        series.Genres = req.Genres;
        series.Tags = req.Tags;
        series.AgeRating = req.AgeRating;
        series.AgeRatingTier = AgeRatingMap.Tier(req.AgeRating);
        series.MetadataLocked = true; // user edits win over future scans (spec §8)
        series.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Persist the corrected metadata back into the library so it replaces any cached ComicInfo.xml
        // and survives re-scans / DB resets (best-effort; read-only shares are simply skipped).
        await _sidecar.WriteComicInfoAsync(series, ct);

        return Ok(await BuildDetailAsync(series, User.GetUserId() ?? 0, ct));
    }

    /// <summary>
    /// Admin-only: look up this series on the online provider (AniList) and return the result for the
    /// edit dialog to review. Nothing is saved — the admin edits and then clicks Save. Pass <c>q</c>
    /// to search by a different title than the series' name.
    /// </summary>
    [HttpPost("{id:int}/online-metadata")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OnlineMetadataDto>> FetchOnlineMetadata(
        int id, [FromQuery] string? q, CancellationToken ct)
    {
        var series = await _db.Series.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series is null) return NotFound();

        var query = string.IsNullOrWhiteSpace(q) ? series.Name : q.Trim();
        var meta = await _online.FetchAsync(query, ct);
        if (meta is null)
            return Ok(new OnlineMetadataDto(false, null, null, null, null, null, null, null));

        return Ok(new OnlineMetadataDto(
            true, meta.Summary, meta.Genres, meta.Tags, meta.Writer, meta.Penciller, meta.AgeRating, meta.CoverUrl));
    }

    /// <summary>Admin-only: set the series cover from a remote image URL (e.g. the online provider's).</summary>
    [HttpPost("{id:int}/cover-from-url")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SeriesDetailDto>> CoverFromUrl(int id, CoverFromUrlRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Url)) return BadRequest(new { error = "No image URL provided." });

        var series = await _db.Series
            .Include(s => s.Volumes).ThenInclude(v => v.Chapters)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series is null) return NotFound();

        var raw = await _online.DownloadImageAsync(req.Url, ct);
        if (raw is null) return BadRequest(new { error = "Could not download the image." });

        byte[] resized;
        try { resized = ImageHelper.ResizeCover(raw); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process online cover for series {Id}", id);
            return BadRequest(new { error = "Could not read the downloaded image." });
        }

        var coverPath = _paths.CoverFileForSeries(series.Id);
        await System.IO.File.WriteAllBytesAsync(coverPath, resized, ct);
        series.CoverPath = coverPath;
        series.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var savedToLibrary = await _sidecar.WriteCoverAsync(series, resized, ct);
        Response.Headers["X-Cover-Saved-To-Library"] = savedToLibrary ? "true" : "false";

        return Ok(await BuildDetailAsync(series, User.GetUserId() ?? 0, ct));
    }

    [HttpGet("{id:int}/cover")]
    [AllowAnonymous]
    public async Task<IActionResult> Cover(int id, CancellationToken ct)
    {
        var series = await _db.Series.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series?.CoverPath is null || !System.IO.File.Exists(series.CoverPath))
            return NotFound();

        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(series.CoverPath, "image/jpeg");
    }

    /// <summary>
    /// Admin-only: replace a series' cover with an uploaded image. The image is normalized to a
    /// cover-sized JPEG, written to the persistent local cover cache (so it shows immediately and
    /// survives app updates) and, best-effort, saved back into the series folder on the library as
    /// <c>folder.jpg</c> — which the scanner already treats as the source-of-truth cover, so it
    /// also survives re-scans. A NAS write failure (read-only share, permissions) is logged but does
    /// not fail the request.
    /// </summary>
    [HttpPost("{id:int}/cover")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<SeriesDetailDto>> UploadCover(int id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No image file was uploaded." });
        if (file.Length > 25 * 1024 * 1024)
            return BadRequest(new { error = "Image too large (max 25 MB)." });

        var series = await _db.Series
            .Include(s => s.Volumes).ThenInclude(v => v.Chapters)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series is null) return NotFound();

        byte[] resized;
        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            resized = ImageHelper.ResizeCover(ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process uploaded cover for series {Id}", id);
            return BadRequest(new { error = "Could not read the uploaded image." });
        }

        // 1) Persistent local cache: shows immediately and survives app updates (cache lives in the
        //    data dir). Setting CoverPath also stops future scans replacing it with an extracted page.
        var coverPath = _paths.CoverFileForSeries(series.Id);
        await System.IO.File.WriteAllBytesAsync(coverPath, resized, ct);
        series.CoverPath = coverPath;
        series.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // 2) Best-effort write of folder.jpg back to the library so the cover is permanent.
        var savedToLibrary = await _sidecar.WriteCoverAsync(series, resized, ct);
        Response.Headers["X-Cover-Saved-To-Library"] = savedToLibrary ? "true" : "false";

        return Ok(await BuildDetailAsync(series, User.GetUserId() ?? 0, ct));
    }

    private async Task<SeriesDetailDto> BuildDetailAsync(Series series, int userId, CancellationToken ct)
    {
        var volumes = series.Volumes
            .OrderBy(v => v.Number)
            .Select(v => new VolumeDto(v.Id, v.Number, v.Name,
                v.Chapters.OrderBy(c => c.Number).Select(c => new ChapterDto(
                    c.Id, c.Number, c.Title, c.PageCount, c.FileFormat, c.CoverPath != null)).ToList()))
            .ToList();

        var ratings = await _db.SeriesReviews.Where(r => r.SeriesId == series.Id).ToListAsync(ct);
        double? avg = ratings.Count > 0 ? Math.Round(ratings.Average(r => r.Stars), 2) : null;
        var mine = ratings.FirstOrDefault(r => r.UserId == userId);
        var wantToRead = await _db.WantToRead.AnyAsync(w => w.UserId == userId && w.SeriesId == series.Id, ct);
        var (writer, penciller) = ParsePeople(series.People);

        return new SeriesDetailDto(
            series.Id, series.LibraryId, series.Name, series.Summary, series.CoverPath != null, volumes,
            series.Genres, series.Tags, series.Publisher, series.AgeRating,
            avg, ratings.Count, mine?.Stars, mine?.Body, wantToRead,
            series.Language, writer, penciller);
    }

    /// <summary>Extracts writer/penciller from the <see cref="Series.People"/> JSON blob, if present.</summary>
    private static (string? Writer, string? Penciller) ParsePeople(string? peopleJson)
    {
        if (string.IsNullOrWhiteSpace(peopleJson)) return (null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(peopleJson);
            var root = doc.RootElement;
            string? Get(string name) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.String
                    ? el.GetString()
                    : null;
            return (Get("writer"), Get("penciller"));
        }
        catch
        {
            return (null, null);
        }
    }
}
