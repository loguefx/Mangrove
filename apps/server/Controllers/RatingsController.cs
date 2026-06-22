using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

/// <summary>Ratings (stars) and reviews (body) share one row per (user, series) — spec §6.</summary>
[ApiController]
[Authorize]
public sealed class RatingsController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly AccessService _access;

    public RatingsController(MangroveDbContext db, AccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpPost("api/ratings")]
    public async Task<IActionResult> Rate(RatingRequest req, CancellationToken ct)
    {
        if (req.Stars is < 1 or > 5) return BadRequest(new { error = "Stars must be 1-5." });
        var userId = User.GetUserId() ?? 0;
        if (!await _access.CanAccessSeriesAsync(userId, User.IsAdmin(), req.SeriesId, ct)) return NotFound();

        var row = await Upsert(userId, req.SeriesId, ct);
        row.Stars = req.Stars;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("api/reviews")]
    public async Task<IActionResult> Review(ReviewRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        if (!await _access.CanAccessSeriesAsync(userId, User.IsAdmin(), req.SeriesId, ct)) return NotFound();

        var row = await Upsert(userId, req.SeriesId, ct);
        row.Body = string.IsNullOrWhiteSpace(req.Body) ? null : req.Body.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("api/reviews")]
    public async Task<ActionResult<IEnumerable<ReviewDto>>> ForSeries([FromQuery] int seriesId, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        if (!await _access.CanAccessSeriesAsync(userId, User.IsAdmin(), seriesId, ct)) return NotFound();

        var reviews = await _db.SeriesReviews
            .Where(r => r.SeriesId == seriesId && (r.Body != null || r.Stars > 0))
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new ReviewDto(r.UserId, r.User.Username, r.SeriesId, r.Stars, r.Body, r.UpdatedAt))
            .ToListAsync(ct);
        return Ok(reviews);
    }

    [HttpDelete("api/ratings/{seriesId:int}")]
    public async Task<IActionResult> Remove(int seriesId, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var row = await _db.SeriesReviews.FirstOrDefaultAsync(r => r.UserId == userId && r.SeriesId == seriesId, ct);
        if (row is not null)
        {
            _db.SeriesReviews.Remove(row);
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    private async Task<SeriesReview> Upsert(int userId, int seriesId, CancellationToken ct)
    {
        var row = await _db.SeriesReviews.FirstOrDefaultAsync(r => r.UserId == userId && r.SeriesId == seriesId, ct);
        if (row is null)
        {
            row = new SeriesReview { UserId = userId, SeriesId = seriesId };
            _db.SeriesReviews.Add(row);
        }
        return row;
    }
}
