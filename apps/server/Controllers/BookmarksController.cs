using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

public sealed record BookmarkRequest(int ChapterId, int Page);
public sealed record BookmarkDto(int Id, int ChapterId, int Page, string SeriesName, float ChapterNumber, DateTime CreatedAt);

[ApiController]
[Route("api/bookmarks")]
[Authorize]
public sealed class BookmarksController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly AccessService _access;

    public BookmarksController(MangroveDbContext db, AccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BookmarkDto>>> List([FromQuery] int? chapterId, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var q = _db.Bookmarks.Where(b => b.UserId == userId);
        if (chapterId is { } cid) q = q.Where(b => b.ChapterId == cid);
        var list = await q
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BookmarkDto(b.Id, b.ChapterId, b.PageNum,
                b.Chapter.Volume.Series.Name, b.Chapter.Number, b.CreatedAt))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<BookmarkDto>> Add(BookmarkRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        if (!await _access.CanAccessChapterAsync(userId, User.IsAdmin(), req.ChapterId, ct)) return NotFound();

        var bookmark = new Bookmark { UserId = userId, ChapterId = req.ChapterId, PageNum = req.Page };
        _db.Bookmarks.Add(bookmark);
        await _db.SaveChangesAsync(ct);

        var dto = await _db.Bookmarks.Where(b => b.Id == bookmark.Id)
            .Select(b => new BookmarkDto(b.Id, b.ChapterId, b.PageNum,
                b.Chapter.Volume.Series.Name, b.Chapter.Number, b.CreatedAt))
            .FirstAsync(ct);
        return Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var bookmark = await _db.Bookmarks.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
        if (bookmark is null) return NotFound();
        _db.Bookmarks.Remove(bookmark);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
