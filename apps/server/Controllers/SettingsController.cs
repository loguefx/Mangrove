using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Roles = "Admin")]
public sealed class SettingsController : ControllerBase
{
    private readonly MangroveDbContext _db;
    public SettingsController(MangroveDbContext db) => _db = db;

    /// <summary>Known settings keys with their defaults (spec §6 AppSetting).</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["opds.enabled"] = "true",
        ["server.baseUrl"] = "",
        ["theme.default"] = "dark",
        ["scan.onStartup"] = "false",
    };

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SettingDto>>> Get(CancellationToken ct)
    {
        var stored = await _db.AppSettings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        var merged = Defaults.Keys.Union(stored.Keys)
            .OrderBy(k => k)
            .Select(k => new SettingDto(k, stored.TryGetValue(k, out var v) ? v : Defaults.GetValueOrDefault(k)))
            .ToList();
        return Ok(merged);
    }

    [HttpPut]
    public async Task<IActionResult> Put(IReadOnlyList<SettingDto> settings, CancellationToken ct)
    {
        foreach (var s in settings)
        {
            if (string.IsNullOrWhiteSpace(s.Key)) continue;
            var existing = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == s.Key, ct);
            if (existing is null) _db.AppSettings.Add(new AppSetting { Key = s.Key, Value = s.Value });
            else existing.Value = s.Value;
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
