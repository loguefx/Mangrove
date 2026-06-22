using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/want-to-read")]
[Authorize]
public sealed class WantToReadController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly AccessService _access;

    public WantToReadController(MangroveDbContext db, AccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SeriesDto>>> List(CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var isAdmin = User.IsAdmin();
        var libIds = await _access.AccessibleLibraryIdsAsync(userId, isAdmin, ct);
        var restriction = await _access.GetRestrictionAsync(userId, isAdmin, ct);

        var seriesQuery = _db.WantToRead.Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => w.Series);

        var series = await _access.FilterSeries(seriesQuery, libIds, restriction)
            .Select(s => new SeriesDto(s.Id, s.LibraryId, s.Name, s.Summary, s.CoverPath != null,
                s.Volumes.Count, s.Volumes.SelectMany(v => v.Chapters).Count()))
            .ToListAsync(ct);
        return Ok(series);
    }

    /// <summary>
    /// Favorited series with new chapters the user hasn't read yet. "New" means a chapter
    /// added (scanned) after the user favorited the series, so the backlog you knowingly added
    /// isn't flagged — only genuinely new releases are. Powers the Home "Catch up" rail and
    /// the Favorites notification badge.
    /// </summary>
    [HttpGet("unread")]
    public async Task<ActionResult<IEnumerable<FavoriteUnreadDto>>> Unread(CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var isAdmin = User.IsAdmin();
        var libIds = await _access.AccessibleLibraryIdsAsync(userId, isAdmin, ct);
        var restriction = await _access.GetRestrictionAsync(userId, isAdmin, ct);

        var favs = await _db.WantToRead
            .Where(w => w.UserId == userId)
            .Select(w => new { w.SeriesId, w.CreatedAt })
            .ToListAsync(ct);
        if (favs.Count == 0) return Ok(Array.Empty<FavoriteUnreadDto>());

        var favIds = favs.Select(f => f.SeriesId).ToList();
        var accessibleIds = await _access
            .FilterSeries(_db.Series.Where(s => favIds.Contains(s.Id)), libIds, restriction)
            .Select(s => s.Id)
            .ToListAsync(ct);
        var favAt = favs.Where(f => accessibleIds.Contains(f.SeriesId))
            .ToDictionary(f => f.SeriesId, f => f.CreatedAt);
        if (favAt.Count == 0) return Ok(Array.Empty<FavoriteUnreadDto>());

        var sids = favAt.Keys.ToList();
        var rows = await _db.Chapters
            .Where(c => sids.Contains(c.Volume.SeriesId)
                && !c.ReadingProgress.Any(rp => rp.UserId == userId && rp.IsRead))
            .Select(c => new
            {
                SeriesId = c.Volume.SeriesId,
                SeriesName = c.Volume.Series.Name,
                HasCover = c.Volume.Series.CoverPath != null,
                ChapterId = c.Id,
                VolNum = c.Volume.Number,
                ChapNum = c.Number,
                c.CreatedAt,
            })
            .ToListAsync(ct);

        var result = rows
            .Where(r => r.CreatedAt > favAt[r.SeriesId])
            .GroupBy(r => new { r.SeriesId, r.SeriesName, r.HasCover })
            .Select(g =>
            {
                var next = g.OrderBy(x => x.VolNum).ThenBy(x => x.ChapNum).First();
                return new FavoriteUnreadDto(
                    g.Key.SeriesId, g.Key.SeriesName, g.Key.HasCover,
                    g.Count(), next.ChapterId, next.ChapNum);
            })
            .OrderByDescending(x => x.NewChapters)
            .ThenBy(x => x.SeriesName)
            .ToList();

        return Ok(result);
    }

    [HttpPost("{seriesId:int}")]
    public async Task<IActionResult> Add(int seriesId, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        if (!await _access.CanAccessSeriesAsync(userId, User.IsAdmin(), seriesId, ct)) return NotFound();
        if (!await _db.WantToRead.AnyAsync(w => w.UserId == userId && w.SeriesId == seriesId, ct))
        {
            _db.WantToRead.Add(new WantToRead { UserId = userId, SeriesId = seriesId });
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpDelete("{seriesId:int}")]
    public async Task<IActionResult> Remove(int seriesId, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var entry = await _db.WantToRead.FirstOrDefaultAsync(w => w.UserId == userId && w.SeriesId == seriesId, ct);
        if (entry is not null)
        {
            _db.WantToRead.Remove(entry);
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }
}
