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
