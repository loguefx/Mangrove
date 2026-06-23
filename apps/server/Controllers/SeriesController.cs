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
    private readonly StorageProviderFactory _providers;
    private readonly ILogger<SeriesController> _logger;
    public SeriesController(
        MangroveDbContext db,
        AccessService access,
        ServerPaths paths,
        StorageProviderFactory providers,
        ILogger<SeriesController> logger)
    {
        _db = db;
        _access = access;
        _paths = paths;
        _providers = providers;
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
        var savedToLibrary = await TryWriteFolderJpgAsync(series, resized, ct);
        Response.Headers["X-Cover-Saved-To-Library"] = savedToLibrary ? "true" : "false";

        return Ok(await BuildDetailAsync(series, User.GetUserId() ?? 0, ct));
    }

    /// <summary>
    /// Writes the cover bytes as <c>folder.jpg</c> into the series' folder on its library. Returns
    /// false (and logs) on any failure instead of throwing, since the local cover cache is already
    /// updated by the time this runs.
    /// </summary>
    private async Task<bool> TryWriteFolderJpgAsync(Series series, byte[] jpeg, CancellationToken ct)
    {
        try
        {
            var library = await _db.Libraries
                .Include(l => l.Paths).ThenInclude(p => p.Credential)
                .Include(l => l.Credential)
                .FirstOrDefaultAsync(l => l.Id == series.LibraryId, ct);
            if (library is null) return false;

            var storagePath = await _db.MangaFiles
                .Where(f => f.Chapter.Volume.SeriesId == series.Id)
                .Select(f => f.StoragePath)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(storagePath)) return false;

            // Find which configured root contains this file (and that root's credential).
            var roots = library.Paths.Count > 0
                ? library.Paths.Select(p => (Path: p.Path, Cred: p.Credential ?? library.Credential))
                : new[] { (Path: library.RootPath, Cred: library.Credential) }.AsEnumerable();

            (string Path, Credential? Cred)? match = null;
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r.Path)) continue;
                var norm = r.Path.Replace('/', '\\').TrimEnd('\\');
                if (storagePath.Replace('/', '\\').StartsWith(norm, StringComparison.OrdinalIgnoreCase))
                {
                    if (match is null || norm.Length > match.Value.Path.Replace('/', '\\').TrimEnd('\\').Length)
                        match = (r.Path, r.Cred);
                }
            }
            if (match is null) return false;

            // Series folder = root + first path segment of the file relative to that root.
            var rootNorm = match.Value.Path.Replace('/', '\\').TrimEnd('\\');
            var full = storagePath.Replace('/', '\\');
            var rel = full.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase) ? full[rootNorm.Length..] : full;
            var segments = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) return false; // loose file directly in root: no series folder
            var seriesDir = $"{rootNorm}\\{segments[0]}";

            var provider = _providers.ForLibrary(library, match.Value.Cred);
            var target = $"{seriesDir}\\folder.jpg";
            await provider.WriteAsync(target, jpeg, ct);
            _logger.LogInformation("Saved uploaded cover to library at {Path}", target);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write folder.jpg to library for series {Id}", series.Id);
            return false;
        }
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
