using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/progress")]
[Authorize]
public sealed class ProgressController : ControllerBase
{
    private readonly MangroveDbContext _db;
    public ProgressController(MangroveDbContext db) => _db = db;

    [HttpPost]
    public async Task<ActionResult<ProgressDto>> Save(ProgressRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!await _db.Chapters.AnyAsync(c => c.Id == req.ChapterId, ct)) return NotFound();

        var progress = await _db.ReadingProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == req.ChapterId, ct);

        if (progress is null)
        {
            progress = new ReadingProgress { UserId = userId.Value, ChapterId = req.ChapterId };
            _db.ReadingProgress.Add(progress);
        }

        progress.PageNum = req.Page;
        progress.ScrollOffset = req.ScrollOffset;
        if (req.IsRead is { } read) progress.IsRead = read;
        progress.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new ProgressDto(progress.ChapterId, progress.PageNum, progress.ScrollOffset, progress.IsRead, progress.UpdatedAt));
    }

    [HttpGet("{chapterId:int}")]
    public async Task<ActionResult<ProgressDto>> GetForChapter(int chapterId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var progress = await _db.ReadingProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapterId, ct);
        if (progress is null)
            return Ok(new ProgressDto(chapterId, 0, null, false, DateTime.MinValue));

        return Ok(new ProgressDto(progress.ChapterId, progress.PageNum, progress.ScrollOffset, progress.IsRead, progress.UpdatedAt));
    }

    [HttpPost("chapter/{chapterId:int}/read")]
    public async Task<IActionResult> MarkChapterRead(int chapterId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, ct);
        if (chapter is null) return NotFound();

        var progress = await _db.ReadingProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapterId, ct);
        if (progress is null)
        {
            progress = new ReadingProgress { UserId = userId.Value, ChapterId = chapterId };
            _db.ReadingProgress.Add(progress);
        }
        progress.IsRead = true;
        progress.PageNum = Math.Max(0, chapter.PageCount - 1);
        progress.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("chapter/{chapterId:int}/unread")]
    public async Task<IActionResult> MarkChapterUnread(int chapterId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var progress = await _db.ReadingProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapterId, ct);
        if (progress is not null)
        {
            _db.ReadingProgress.Remove(progress);
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpPost("series/{seriesId:int}/read")]
    public async Task<IActionResult> MarkSeriesRead(int seriesId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var chapters = await _db.Chapters.Where(c => c.Volume.SeriesId == seriesId).ToListAsync(ct);
        if (chapters.Count == 0) return NotFound();

        var existing = await _db.ReadingProgress
            .Where(p => p.UserId == userId && p.Chapter.Volume.SeriesId == seriesId)
            .ToDictionaryAsync(p => p.ChapterId, ct);

        foreach (var ch in chapters)
        {
            if (!existing.TryGetValue(ch.Id, out var prog))
            {
                prog = new ReadingProgress { UserId = userId.Value, ChapterId = ch.Id };
                _db.ReadingProgress.Add(prog);
            }
            prog.IsRead = true;
            prog.PageNum = Math.Max(0, ch.PageCount - 1);
            prog.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("series/{seriesId:int}/unread")]
    public async Task<IActionResult> MarkSeriesUnread(int seriesId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var rows = await _db.ReadingProgress
            .Where(p => p.UserId == userId && p.Chapter.Volume.SeriesId == seriesId)
            .ToListAsync(ct);
        if (rows.Count > 0)
        {
            _db.ReadingProgress.RemoveRange(rows);
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProgressDto>>> Get([FromQuery] int? seriesId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var query = _db.ReadingProgress.Where(p => p.UserId == userId);
        if (seriesId is { } sid)
            query = query.Where(p => p.Chapter.Volume.SeriesId == sid);

        var items = await query
            .Select(p => new ProgressDto(p.ChapterId, p.PageNum, p.ScrollOffset, p.IsRead, p.UpdatedAt))
            .ToListAsync(ct);
        return Ok(items);
    }
}
