using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
public sealed class SearchController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly AccessService _access;
    public SearchController(MangroveDbContext db, AccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SearchResultDto>>> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<SearchResultDto>());

        var userId = User.GetUserId() ?? 0;
        var isAdmin = User.IsAdmin();
        var libIds = await _access.AccessibleLibraryIdsAsync(userId, isAdmin, ct);
        var restriction = await _access.GetRestrictionAsync(userId, isAdmin, ct);

        var term = q.Trim();
        var results = await _access.FilterSeries(_db.Series, libIds, restriction)
            .Where(s =>
                EF.Functions.Like(s.Name, $"%{term}%") ||
                (s.Summary != null && EF.Functions.Like(s.Summary, $"%{term}%")) ||
                (s.Genres != null && EF.Functions.Like(s.Genres, $"%{term}%")) ||
                (s.Tags != null && EF.Functions.Like(s.Tags, $"%{term}%")))
            .OrderBy(s => s.SortName)
            .Take(50)
            .Select(s => new SearchResultDto(s.Id, s.Name, s.LibraryId, s.CoverPath != null, "series"))
            .ToListAsync(ct);

        return Ok(results);
    }
}
