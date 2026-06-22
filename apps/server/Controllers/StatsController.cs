using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/stats")]
[Authorize]
public sealed class StatsController : ControllerBase
{
    private readonly MangroveDbContext _db;
    public StatsController(MangroveDbContext db) => _db = db;

    [HttpGet("server")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServerStatsDto>> Server(CancellationToken ct)
    {
        return Ok(new ServerStatsDto(
            await _db.Users.CountAsync(ct),
            await _db.Libraries.CountAsync(ct),
            await _db.Series.CountAsync(ct),
            await _db.Volumes.CountAsync(ct),
            await _db.Chapters.CountAsync(ct),
            await _db.MangaFiles.SumAsync(f => (long?)f.Bytes, ct) ?? 0,
            await _db.Chapters.SumAsync(c => (int?)c.PageCount, ct) ?? 0));
    }

    /// <summary>
    /// Recent reading activity across all users: a timeline of what each user opened, where they
    /// stopped (page/total), what they finished, and when they caught up to a series' newest chapter.
    /// Newest first. Admin-only.
    /// </summary>
    [HttpGet("activity")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<ActivityDto>>> Activity(CancellationToken ct)
    {
        const int Limit = 200;

        // Each (user, chapter) has one upserted progress row; ordering by UpdatedAt yields the timeline.
        var rows = await _db.ReadingProgress
            .Include(p => p.User)
            .Include(p => p.Chapter).ThenInclude(c => c.Volume).ThenInclude(v => v.Series)
            .Where(p => p.PageNum > 0 || p.IsRead)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(Limit)
            .ToListAsync(ct);

        // Highest chapter number per involved series, to flag "caught up" (read the newest chapter).
        var seriesIds = rows.Select(p => p.Chapter.Volume.SeriesId).Distinct().ToList();
        var latestChapter = await _db.Chapters
            .Where(c => seriesIds.Contains(c.Volume.SeriesId))
            .GroupBy(c => c.Volume.SeriesId)
            .Select(g => new { SeriesId = g.Key, Max = g.Max(c => c.Number) })
            .ToDictionaryAsync(x => x.SeriesId, x => x.Max, ct);

        var activity = rows.Select(p =>
        {
            var series = p.Chapter.Volume.Series;
            var seriesId = p.Chapter.Volume.SeriesId;
            var isNewest = latestChapter.TryGetValue(seriesId, out var max) && p.Chapter.Number >= max;
            var caughtUp = p.IsRead && isNewest;
            var status = caughtUp ? "caught-up" : p.IsRead ? "finished" : "reading";
            return new ActivityDto(
                p.UserId, p.User.Username, series?.Id, series?.Name,
                p.ChapterId, p.Chapter.Number, p.Chapter.Title,
                p.PageNum, p.Chapter.PageCount, p.IsRead, caughtUp, status, p.UpdatedAt);
        }).ToList();

        return Ok(activity);
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserStatsDto>> Me(CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var progress = _db.ReadingProgress.Where(p => p.UserId == userId);
        return Ok(new UserStatsDto(
            await progress.CountAsync(p => p.IsRead, ct),
            await progress.SumAsync(p => (int?)p.PageNum, ct) ?? 0,
            await progress.CountAsync(p => !p.IsRead && p.PageNum > 0, ct),
            await _db.WantToRead.CountAsync(w => w.UserId == userId, ct)));
    }
}
