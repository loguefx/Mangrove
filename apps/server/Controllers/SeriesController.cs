using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
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
    public SeriesController(MangroveDbContext db, AccessService access)
    {
        _db = db;
        _access = access;
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

        return new SeriesDetailDto(
            series.Id, series.LibraryId, series.Name, series.Summary, series.CoverPath != null, volumes,
            series.Genres, series.Tags, series.Publisher, series.AgeRating,
            avg, ratings.Count, mine?.Stars, mine?.Body, wantToRead);
    }
}
