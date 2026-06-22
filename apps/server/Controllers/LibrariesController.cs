using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Mangrove.Server.Scanning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/libraries")]
[Authorize]
public sealed class LibrariesController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly ScanJobQueue _scanQueue;
    private readonly AccessService _access;

    public LibrariesController(MangroveDbContext db, ScanJobQueue scanQueue, AccessService access)
    {
        _db = db;
        _scanQueue = scanQueue;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LibraryDto>>> List(CancellationToken ct)
    {
        var libIds = await _access.AccessibleLibraryIdsAsync(User.GetUserId() ?? 0, User.IsAdmin(), ct);
        var libs = await _db.Libraries
            .Where(l => libIds.Contains(l.Id))
            .Select(l => new LibraryDto(l.Id, l.Name, l.Type, l.StorageKind, l.RootPath,
                l.CredentialId, l.FolderWatch, l.LastScanAt, l.Series.Count))
            .ToListAsync(ct);
        return Ok(libs);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LibraryDto>> Create(CreateLibraryRequest req, CancellationToken ct)
    {
        if (req.StorageKind == StorageKind.Smb && req.CredentialId is null)
            return BadRequest(new { error = "SMB libraries require a credential." });

        var lib = new Library
        {
            Name = req.Name,
            Type = req.Type,
            StorageKind = req.StorageKind,
            RootPath = req.RootPath,
            CredentialId = req.CredentialId,
            FolderWatch = req.FolderWatch,
        };
        _db.Libraries.Add(lib);
        await _db.SaveChangesAsync(ct);
        return Ok(new LibraryDto(lib.Id, lib.Name, lib.Type, lib.StorageKind, lib.RootPath,
            lib.CredentialId, lib.FolderWatch, lib.LastScanAt, 0));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var lib = await _db.Libraries.FindAsync(new object[] { id }, ct);
        if (lib is null) return NotFound();
        _db.Libraries.Remove(lib);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/scan")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ScanStatusDto>> Scan(int id, CancellationToken ct)
    {
        if (!await _db.Libraries.AnyAsync(l => l.Id == id, ct)) return NotFound();
        var queued = _scanQueue.Enqueue(id);
        var state = _scanQueue.StateOf(id);
        return Accepted(new ScanStatusDto(id, StateName(state), queued));
    }

    [HttpGet("{id:int}/scan-status")]
    [Authorize(Roles = "Admin")]
    public ActionResult<ScanStatusDto> ScanStatus(int id)
    {
        var state = _scanQueue.StateOf(id);
        return Ok(new ScanStatusDto(id, StateName(state), state != ScanState.Idle));
    }

    private static string StateName(ScanState s) => s switch
    {
        ScanState.Running => "running",
        ScanState.Queued => "queued",
        _ => "idle",
    };

    [HttpGet("{id:int}/series")]
    public async Task<ActionResult<IEnumerable<SeriesDto>>> Series(
        int id, [FromQuery] string? filter, [FromQuery] string sort = "name", CancellationToken ct = default)
    {
        var userId = User.GetUserId() ?? 0;
        var isAdmin = User.IsAdmin();
        var libIds = await _access.AccessibleLibraryIdsAsync(userId, isAdmin, ct);
        if (!libIds.Contains(id)) return Forbid();
        var restriction = await _access.GetRestrictionAsync(userId, isAdmin, ct);

        var query = _access.FilterSeries(_db.Series.Where(s => s.LibraryId == id), libIds, restriction);
        if (!string.IsNullOrWhiteSpace(filter))
            query = query.Where(s => s.Name.Contains(filter));

        query = sort.ToLowerInvariant() switch
        {
            "recent" => query.OrderByDescending(s => s.UpdatedAt),
            _ => query.OrderBy(s => s.SortName),
        };

        var series = await query
            .Select(s => new SeriesDto(
                s.Id, s.LibraryId, s.Name, s.Summary, s.CoverPath != null,
                s.Volumes.Count,
                s.Volumes.SelectMany(v => v.Chapters).Count()))
            .ToListAsync(ct);
        return Ok(series);
    }
}
