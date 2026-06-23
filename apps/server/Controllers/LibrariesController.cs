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
            .Include(l => l.Paths)
            .ToListAsync(ct);
        var counts = await _db.Series
            .Where(s => libIds.Contains(s.LibraryId))
            .GroupBy(s => s.LibraryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return Ok(libs.Select(l => ToDto(l, counts.GetValueOrDefault(l.Id))).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LibraryDto>> Create(CreateLibraryRequest req, CancellationToken ct)
    {
        if (req.StorageKind == StorageKind.Smb && req.CredentialId is null)
            return BadRequest(new { error = "SMB libraries require a credential." });

        var paths = NormalizePaths(req.Paths, req.RootPath);
        if (paths.Count == 0)
            return BadRequest(new { error = "At least one folder path is required." });

        var lib = new Library
        {
            Name = req.Name,
            Type = req.Type,
            StorageKind = req.StorageKind,
            RootPath = paths[0],
            CredentialId = req.CredentialId,
            FolderWatch = req.FolderWatch,
            Paths = paths.Select(p => new LibraryPath { Path = p }).ToList(),
        };
        _db.Libraries.Add(lib);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(lib, 0));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LibraryDto>> Update(int id, UpdateLibraryRequest req, CancellationToken ct)
    {
        var lib = await _db.Libraries.Include(l => l.Paths).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lib is null) return NotFound();

        if (req.Name is not null) lib.Name = req.Name;
        if (req.FolderWatch is { } fw) lib.FolderWatch = fw;
        if (req.CredentialId is not null) lib.CredentialId = req.CredentialId;

        if (req.Paths is not null)
        {
            var paths = NormalizePaths(req.Paths, null);
            if (paths.Count == 0)
                return BadRequest(new { error = "A library needs at least one folder path." });
            if (lib.StorageKind == StorageKind.Smb && lib.CredentialId is null)
                return BadRequest(new { error = "SMB libraries require a credential." });

            // Keep existing rows whose path is unchanged (preserves any per-path credential overrides),
            // drop the rest, and add new ones — so the next scan reconciles content for added/removed folders.
            var keep = lib.Paths.Where(p => paths.Contains(p.Path, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var stale in lib.Paths.Except(keep).ToList())
                _db.LibraryPaths.Remove(stale);
            foreach (var p in paths.Where(p => !keep.Any(k => k.Path.Equals(p, StringComparison.OrdinalIgnoreCase))))
                lib.Paths.Add(new LibraryPath { Path = p });

            lib.RootPath = paths[0];
        }

        await _db.SaveChangesAsync(ct);
        await _db.Entry(lib).Collection(l => l.Paths).LoadAsync(ct);
        var seriesCount = await _db.Series.CountAsync(s => s.LibraryId == lib.Id, ct);
        return Ok(ToDto(lib, seriesCount));
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

    /// <summary>Trims, de-duplicates (case-insensitively), and drops empty entries from a path list.</summary>
    private static List<string> NormalizePaths(IReadOnlyList<string>? paths, string? fallback)
    {
        var source = paths is { Count: > 0 } ? paths : (fallback is null ? Array.Empty<string>() : new[] { fallback });
        var result = new List<string>();
        foreach (var raw in source)
        {
            var p = raw?.Trim();
            if (string.IsNullOrEmpty(p)) continue;
            if (!result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
        }
        return result;
    }

    private static LibraryDto ToDto(Library l, int seriesCount) => new(
        l.Id, l.Name, l.Type, l.StorageKind, l.RootPath, l.CredentialId, l.FolderWatch, l.LastScanAt,
        seriesCount,
        (l.Paths ?? new List<LibraryPath>())
            .Select(p => new LibraryPathDto(p.Id, p.Path, p.CredentialId)).ToList());

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
        int id,
        [FromQuery] string? filter,
        [FromQuery] string sort = "name",
        [FromQuery] string? genre = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId() ?? 0;
        var isAdmin = User.IsAdmin();
        var libIds = await _access.AccessibleLibraryIdsAsync(userId, isAdmin, ct);
        if (!libIds.Contains(id)) return Forbid();
        var restriction = await _access.GetRestrictionAsync(userId, isAdmin, ct);

        var query = _access.FilterSeries(_db.Series.Where(s => s.LibraryId == id), libIds, restriction);
        if (!string.IsNullOrWhiteSpace(filter))
            query = query.Where(s => s.Name.Contains(filter));
        if (!string.IsNullOrWhiteSpace(genre))
            query = query.Where(s => s.Genres != null && s.Genres.Contains(genre));

        query = sort.ToLowerInvariant() switch
        {
            "recent" or "updated" => query.OrderByDescending(s => s.UpdatedAt),
            "added" => query.OrderByDescending(s => s.CreatedAt),
            "chapters" => query.OrderByDescending(s => s.Volumes.SelectMany(v => v.Chapters).Count()),
            _ => query.OrderBy(s => s.SortName),
        };

        var series = await query
            .Select(s => new SeriesDto(
                s.Id, s.LibraryId, s.Name, s.Summary, s.CoverPath != null,
                s.Volumes.Count,
                s.Volumes.SelectMany(v => v.Chapters).Count(),
                s.Volumes.SelectMany(v => v.Chapters)
                    .Count(c => c.ReadingProgress.Any(rp => rp.UserId == userId && rp.IsRead))))
            .ToListAsync(ct);

        // "status" is applied in memory because it depends on the per-user read count above.
        series = status?.ToLowerInvariant() switch
        {
            "unread" => series.Where(s => s.ReadChapters == 0).ToList(),
            "reading" => series.Where(s => s.ReadChapters > 0 && s.ReadChapters < s.ChapterCount).ToList(),
            "completed" => series.Where(s => s.ChapterCount > 0 && s.ReadChapters >= s.ChapterCount).ToList(),
            _ => series,
        };
        return Ok(series);
    }

    /// <summary>Distinct genres present in a library, for the browse/filter dropdown.</summary>
    [HttpGet("{id:int}/genres")]
    public async Task<ActionResult<IEnumerable<string>>> Genres(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId() ?? 0;
        var isAdmin = User.IsAdmin();
        var libIds = await _access.AccessibleLibraryIdsAsync(userId, isAdmin, ct);
        if (!libIds.Contains(id)) return Forbid();
        var restriction = await _access.GetRestrictionAsync(userId, isAdmin, ct);

        var raw = await _access
            .FilterSeries(_db.Series.Where(s => s.LibraryId == id), libIds, restriction)
            .Where(s => s.Genres != null && s.Genres != "")
            .Select(s => s.Genres!)
            .ToListAsync(ct);

        var genres = raw
            .SelectMany(g => g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Ok(genres);
    }
}
