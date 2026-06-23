using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly AccessService _access;
    public DashboardController(MangroveDbContext db, AccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var isAdmin = User.IsAdmin();
        var libIds = await _access.AccessibleLibraryIdsAsync(userId.Value, isAdmin, ct);
        var restriction = await _access.GetRestrictionAsync(userId.Value, isAdmin, ct);

        var continueReading = await _db.ReadingProgress
            .Where(p => p.UserId == userId && !p.IsRead && p.PageNum > 0 &&
                libIds.Contains(p.Chapter.Volume.Series.LibraryId))
            .OrderByDescending(p => p.UpdatedAt)
            .Take(12)
            .Select(p => new ContinueReadingDto(
                p.ChapterId,
                p.Chapter.Volume.SeriesId,
                p.Chapter.Volume.Series.Name,
                p.Chapter.Number,
                p.PageNum,
                p.Chapter.PageCount,
                p.Chapter.CoverPath != null || p.Chapter.Volume.Series.CoverPath != null))
            .ToListAsync(ct);

        var recentlyAdded = await _access.FilterSeries(_db.Series, libIds, restriction)
            .OrderByDescending(s => s.CreatedAt)
            .Take(12)
            .Select(s => new SeriesDto(
                s.Id, s.LibraryId, s.Name, s.Summary, s.CoverPath != null,
                s.Volumes.Count, s.Volumes.SelectMany(v => v.Chapters).Count(), 0))
            .ToListAsync(ct);

        return Ok(new DashboardDto(continueReading, recentlyAdded));
    }
}
