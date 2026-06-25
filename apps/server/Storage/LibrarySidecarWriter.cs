using Mangrove.Server.Data;
using Mangrove.Server.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Storage;

/// <summary>
/// Writes metadata sidecars (<c>folder.jpg</c> cover and <c>ComicInfo.xml</c>) back into a series'
/// folder on its library storage. This makes auto-fetched/edited metadata permanent and portable:
/// the scanner reads these same files, so the data survives re-scans and database resets, and travels
/// with the library if it's moved or imported elsewhere. All writes are best-effort — a read-only or
/// unreachable share is logged, never thrown.
/// </summary>
public sealed class LibrarySidecarWriter
{
    private readonly MangroveDbContext _db;
    private readonly StorageProviderFactory _providers;
    private readonly ILogger<LibrarySidecarWriter> _log;

    public LibrarySidecarWriter(MangroveDbContext db, StorageProviderFactory providers, ILogger<LibrarySidecarWriter> log)
    {
        _db = db;
        _providers = providers;
        _log = log;
    }

    /// <summary>Writes the cover bytes as <c>folder.jpg</c> in the series folder. Returns false on failure.</summary>
    public async Task<bool> WriteCoverAsync(Series series, byte[] jpeg, CancellationToken ct)
    {
        var target = await ResolveAsync(series, ct);
        if (target is null) return false;
        return await WriteAsync(target.Value.Provider, $"{target.Value.Dir}\\folder.jpg", jpeg, ct);
    }

    /// <summary>Writes (replacing) <c>ComicInfo.xml</c> built from the series' current metadata.</summary>
    public async Task<bool> WriteComicInfoAsync(Series series, CancellationToken ct)
    {
        var target = await ResolveAsync(series, ct);
        if (target is null) return false;
        return await WriteAsync(target.Value.Provider, $"{target.Value.Dir}\\ComicInfo.xml", ComicInfoWriter.Build(series), ct);
    }

    private async Task<bool> WriteAsync(IStorageProvider provider, string path, byte[] data, CancellationToken ct)
    {
        try
        {
            await provider.WriteAsync(path, data, ct);
            _log.LogInformation("Wrote metadata sidecar to library at {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not write metadata sidecar to {Path}", path);
            return false;
        }
    }

    /// <summary>
    /// Resolves the series' on-disk folder and the storage provider for the root that contains it, by
    /// locating one of the series' files and matching it to a configured library root. Returns null
    /// for loose files placed directly in a root (no series folder to write into).
    /// </summary>
    private async Task<(IStorageProvider Provider, string Dir)?> ResolveAsync(Series series, CancellationToken ct)
    {
        try
        {
            var library = await _db.Libraries
                .Include(l => l.Paths).ThenInclude(p => p.Credential)
                .Include(l => l.Credential)
                .FirstOrDefaultAsync(l => l.Id == series.LibraryId, ct);
            if (library is null) return null;

            var storagePath = await _db.MangaFiles
                .Where(f => f.Chapter.Volume.SeriesId == series.Id)
                .Select(f => f.StoragePath)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(storagePath)) return null;

            var roots = library.Paths.Count > 0
                ? library.Paths.Select(p => (Path: p.Path, Cred: p.Credential ?? library.Credential))
                : new[] { (Path: library.RootPath, Cred: library.Credential) }.AsEnumerable();

            (string Path, Credential? Cred)? match = null;
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r.Path)) continue;
                var norm = r.Path.Replace('/', '\\').TrimEnd('\\');
                if (storagePath.Replace('/', '\\').StartsWith(norm, StringComparison.OrdinalIgnoreCase))
                {
                    if (match is null || norm.Length > match.Value.Path.Replace('/', '\\').TrimEnd('\\').Length)
                        match = (r.Path, r.Cred);
                }
            }
            if (match is null) return null;

            var rootNorm = match.Value.Path.Replace('/', '\\').TrimEnd('\\');
            var full = storagePath.Replace('/', '\\');
            var rel = full.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase) ? full[rootNorm.Length..] : full;
            var segments = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) return null; // loose file directly in a root: no series folder

            var seriesDir = $"{rootNorm}\\{segments[0]}";
            var provider = _providers.ForLibrary(library, match.Value.Cred);
            return (provider, seriesDir);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not resolve library folder for series {Id}", series.Id);
            return null;
        }
    }
}
