using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

/// <summary>
/// Per-user key/value preferences (reading direction, etc.) that sync across all of a
/// user's clients (web + app). Values are opaque strings owned by the client.
/// </summary>
[ApiController]
[Route("api/me/preferences")]
[Authorize]
public sealed class PreferencesController : ControllerBase
{
    private readonly MangroveDbContext _db;
    public PreferencesController(MangroveDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<Dictionary<string, string?>>> Get(CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var prefs = await _db.UserPreferences
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Key, p => p.Value, ct);
        return Ok(prefs);
    }

    /// <summary>Upserts the provided keys. A null value removes the preference.</summary>
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] Dictionary<string, string?> updates, CancellationToken ct)
    {
        if (updates is null || updates.Count == 0) return NoContent();
        var userId = User.GetUserId() ?? 0;

        var keys = updates.Keys.ToList();
        var existing = await _db.UserPreferences
            .Where(p => p.UserId == userId && keys.Contains(p.Key))
            .ToListAsync(ct);

        foreach (var (key, value) in updates)
        {
            var row = existing.FirstOrDefault(p => p.Key == key);
            if (value is null)
            {
                if (row is not null) _db.UserPreferences.Remove(row);
            }
            else if (row is null)
            {
                _db.UserPreferences.Add(new UserPreference { UserId = userId, Key = key, Value = value });
            }
            else
            {
                row.Value = value;
            }
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
