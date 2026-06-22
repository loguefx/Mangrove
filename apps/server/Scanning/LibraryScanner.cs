using System.Globalization;
using System.Text.Json;
using Mangrove.Server.Data;
using Mangrove.Server.Readers;
using Mangrove.Server.Storage;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Scanning;

public sealed record ScanResult(int FilesSeen, int ChaptersAdded, int ChaptersUpdated, int ChaptersRemoved, int SeriesCount);

/// <summary>
/// Walks a library root through <see cref="IStorageProvider"/>, grouping content units (comic
/// archives, books, and raw image folders) into Series → Volume → Chapter → MangaFile via
/// <see cref="FilenameParser"/> (spec §8). Incremental: unchanged units are skipped. Extracts
/// covers and ComicInfo/EPUB metadata on first sight.
/// </summary>
public sealed class LibraryScanner
{
    private readonly MangroveDbContext _db;
    private readonly StorageProviderFactory _providers;
    private readonly ReaderService _readers;
    private readonly ComicInfoReader _comicInfo;
    private readonly EpubService _epub;
    private readonly ServerPaths _paths;
    private readonly FilenameParser _parser;
    private readonly ILogger<LibraryScanner> _logger;

    private const int MaxDepth = 8;

    public LibraryScanner(
        MangroveDbContext db,
        StorageProviderFactory providers,
        ReaderService readers,
        ComicInfoReader comicInfo,
        EpubService epub,
        ServerPaths paths,
        FilenameParser parser,
        ILogger<LibraryScanner> logger)
    {
        _db = db;
        _providers = providers;
        _readers = readers;
        _comicInfo = comicInfo;
        _epub = epub;
        _paths = paths;
        _parser = parser;
        _logger = logger;
    }

    private sealed record ContentUnit(
        string Path, string Name, bool IsFolder, long Size, DateTime LastModified,
        int ImageCount, string Format, string[] Segments, string Root, IStorageProvider Provider);

    /// <summary>Runs a scan and records a <see cref="JobLog"/> entry for the tasks/history view.</summary>
    public Task<ScanResult> ScanAsync(int libraryId, CancellationToken ct = default) =>
        ScanAsync(libraryId, recordHistory: true, ct);

    /// <summary>
    /// Runs a scan. When <paramref name="recordHistory"/> is false (automatic/periodic scans), a
    /// <see cref="JobLog"/> is written only if the scan changed something or failed — so routine
    /// no-op background scans don't flood the task history.
    /// </summary>
    public async Task<ScanResult> ScanAsync(int libraryId, bool recordHistory, CancellationToken ct = default)
    {
        var libName = await _db.Libraries.Where(l => l.Id == libraryId).Select(l => l.Name).FirstOrDefaultAsync(ct);
        var target = libName ?? $"library:{libraryId}";

        JobLog? job = null;
        if (recordHistory)
        {
            job = new JobLog { Kind = "scan", Target = target, Status = "Running" };
            _db.JobLogs.Add(job);
            await _db.SaveChangesAsync(ct);
        }

        try
        {
            var result = await ScanCoreAsync(libraryId, ct);

            // For quiet (automatic) scans, only leave a history entry when something actually changed.
            if (job is null && result.ChaptersAdded + result.ChaptersUpdated + result.ChaptersRemoved > 0)
            {
                job = new JobLog { Kind = "scan", Target = target };
                _db.JobLogs.Add(job);
            }

            if (job is not null)
            {
                job.Status = "Completed";
                job.Message = $"{result.ChaptersAdded} added, {result.ChaptersUpdated} updated, {result.ChaptersRemoved} removed";
                job.FinishedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            return result;
        }
        catch (Exception ex)
        {
            job ??= AddFailureJob(target);
            job.Status = "Failed";
            job.Message = ex.Message;
            job.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private JobLog AddFailureJob(string target)
    {
        var job = new JobLog { Kind = "scan", Target = target };
        _db.JobLogs.Add(job);
        return job;
    }

    private async Task<ScanResult> ScanCoreAsync(int libraryId, CancellationToken ct = default)
    {
        var library = await _db.Libraries
            .Include(l => l.Credential)
            .Include(l => l.Paths).ThenInclude(p => p.Credential)
            .FirstOrDefaultAsync(l => l.Id == libraryId, ct)
            ?? throw new InvalidOperationException($"Library {libraryId} not found.");

        // A library can span several storage folders, each optionally using its own credential
        // (falling back to the library credential). Older libraries with no LibraryPath rows fall
        // back to the legacy single RootPath.
        var roots = library.Paths.Count > 0
            ? library.Paths
                .Where(p => !string.IsNullOrWhiteSpace(p.Path))
                .Select(p => (Path: p.Path, Provider: _providers.ForLibrary(library, p.Credential ?? library.Credential)))
                .ToList()
            : new List<(string Path, IStorageProvider Provider)>
                { (library.RootPath, _providers.ForLibrary(library, library.Credential)) };

        var units = new List<ContentUnit>();
        foreach (var (root, prov) in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            await CollectUnitsAsync(prov, root, root, 0, units, ct);
        }

        var added = 0;
        var updated = 0;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folderCoverTried = new HashSet<int>();

        foreach (var unit in units)
        {
            ct.ThrowIfCancellationRequested();
            seenPaths.Add(unit.Path);

            var hash = ComputeHash(unit);
            var existing = await _db.MangaFiles
                .Include(f => f.Chapter)
                .FirstOrDefaultAsync(f => f.StoragePath == unit.Path
                    && f.Chapter.Volume.Series.LibraryId == library.Id, ct);

            if (existing is not null && existing.Hash == hash)
                continue; // unchanged — skip needless I/O (spec §8)

            var (seriesName, parsed) = ResolveSeries(unit);

            var series = await GetOrCreateSeriesAsync(library.Id, seriesName, ct);

            // Apply the series-level cover (folder.jpg/cover.jpg/...) the first time we see a series,
            // BEFORE caching any chapter page — so a real cover always wins over an extracted page, and
            // covers land immediately rather than only after the whole (slow, interruptible) scan. The
            // end-of-scan pass below is kept as a safety net (e.g. art added after the initial scan).
            if (folderCoverTried.Add(series.Id) && TryGetSeriesDir(unit, out var seriesDir))
                await TryApplyFolderCoverAsync(series, seriesDir, unit.Provider, ct);
            var volume = await GetOrCreateVolumeAsync(series, parsed.Volume ?? 0, ct);
            var chapter = await GetOrCreateChapterAsync(volume, parsed, unit, ct);

            chapter.FileFormat = unit.Format;

            try
            {
                chapter.PageCount = await _readers.CountPagesAsync(unit.Format, unit.Path, unit.Provider, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to count pages for '{Path}'", unit.Path);
                chapter.PageCount = 0;
            }

            await _db.SaveChangesAsync(ct); // ensure chapter.Id for cover filenames

            await CacheCoverAsync(series, chapter, unit, unit.Provider, ct);
            await ApplyMetadataAsync(series, chapter, unit, unit.Provider, ct);

            if (existing is null)
            {
                _db.MangaFiles.Add(new MangaFile
                {
                    ChapterId = chapter.Id,
                    StoragePath = unit.Path,
                    Bytes = unit.Size,
                    Format = unit.Format,
                    Hash = hash,
                    LastModified = unit.LastModified,
                });
                added++;
            }
            else
            {
                existing.Bytes = unit.Size;
                existing.Hash = hash;
                existing.LastModified = unit.LastModified;
                existing.ChapterId = chapter.Id;
                existing.Format = unit.Format;
                updated++;
            }

            await _db.SaveChangesAsync(ct);
        }

        // Apply series-level sidecar assets sitting next to the chapters: a cover image
        // (folder.jpg/cover.jpg/poster.jpg) and a ComicInfo.xml metadata file. This is the common
        // manga layout (and matches Jellyfin/Kavita conventions).
        await ApplyFolderAssetsAsync(library, units, ct);

        var removed = await RemoveMissingAsync(library.Id, seenPaths, ct);

        library.LastScanAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var seriesCount = await _db.Series.CountAsync(s => s.LibraryId == library.Id, ct);
        _logger.LogInformation(
            "Scan of library {Library} complete: {Added} added, {Updated} updated, {Removed} removed",
            library.Name, added, updated, removed);

        return new ScanResult(units.Count, added, updated, removed, seriesCount);
    }

    private async Task CollectUnitsAsync(
        IStorageProvider provider, string root, string path, int depth, List<ContentUnit> output, CancellationToken ct)
    {
        if (depth > MaxDepth) return;

        IReadOnlyList<StorageEntry> entries;
        try
        {
            entries = await provider.ListAsync(path, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list '{Path}' during scan", path);
            return;
        }

        // A raw-image-folder chapter is a leaf folder of page images. Exclude series cover art
        // (folder.jpg/cover.jpg/...) and don't treat a folder that also holds archives/books as a
        // chapter — that's a series/container folder, not a page folder.
        var hasArchives = entries.Any(e => !e.IsDirectory && SupportedFormats.IsSupportedFile(e.Name));
        var pageImages = entries
            .Where(e => !e.IsDirectory && ImageFormats.IsImage(e.Name) && !IsCoverFileName(e.Name))
            .ToList();
        if (pageImages.Count > 0 && !hasArchives && !PathsEqual(path, root))
        {
            output.Add(new ContentUnit(
                Path: path,
                Name: LastSegment(path),
                IsFolder: true,
                Size: pageImages.Sum(f => f.Size),
                LastModified: pageImages.Max(f => f.LastModified),
                ImageCount: pageImages.Count,
                Format: FormatRegistry.ImageFolderFormat,
                Segments: RelativeSegments(root, path),
                Root: root,
                Provider: provider));
        }

        foreach (var file in entries.Where(e => !e.IsDirectory && SupportedFormats.IsSupportedFile(e.Name)))
        {
            output.Add(new ContentUnit(
                Path: file.FullPath,
                Name: file.Name,
                IsFolder: false,
                Size: file.Size,
                LastModified: file.LastModified,
                ImageCount: 0,
                Format: SupportedFormats.NormalizedExtension(file.Name).TrimStart('.'),
                Segments: RelativeSegments(root, file.FullPath),
                Root: root,
                Provider: provider));
        }

        foreach (var dir in entries.Where(e => e.IsDirectory))
            await CollectUnitsAsync(provider, root, dir.FullPath, depth + 1, output, ct);
    }

    private (string SeriesName, ParsedInfo Parsed) ResolveSeries(ContentUnit unit)
    {
        if (unit.IsFolder)
        {
            var parsed = _parser.Parse(unit.Segments.Length > 0 ? unit.Segments[^1] : unit.Name);
            var series = unit.Segments.Length >= 1 ? unit.Segments[0] : parsed.Series;
            return (series, parsed);
        }
        else
        {
            var parsed = _parser.Parse(unit.Name);
            var folderSegments = unit.Segments.Length > 0 ? unit.Segments[..^1] : Array.Empty<string>();
            var series = folderSegments.Length >= 1 ? folderSegments[0] : parsed.Series;
            return (series, parsed);
        }
    }

    private async Task<Series> GetOrCreateSeriesAsync(int libraryId, string name, CancellationToken ct)
    {
        var existing = await _db.Series
            .FirstOrDefaultAsync(s => s.LibraryId == libraryId && s.Name == name, ct);
        if (existing is not null) return existing;

        var series = new Series { LibraryId = libraryId, Name = name, SortName = name };
        _db.Series.Add(series);
        await _db.SaveChangesAsync(ct);
        return series;
    }

    private async Task<Volume> GetOrCreateVolumeAsync(Series series, float number, CancellationToken ct)
    {
        var existing = await _db.Volumes
            .FirstOrDefaultAsync(v => v.SeriesId == series.Id && v.Number == number, ct);
        if (existing is not null) return existing;

        var volume = new Volume { SeriesId = series.Id, Number = number };
        _db.Volumes.Add(volume);
        await _db.SaveChangesAsync(ct);
        return volume;
    }

    private async Task<Chapter> GetOrCreateChapterAsync(
        Volume volume, ParsedInfo parsed, ContentUnit unit, CancellationToken ct)
    {
        var number = parsed.Chapter ?? 0;
        var title = parsed.IsSpecial ? StripExt(unit.Name) : null;

        var existing = await _db.Chapters
            .FirstOrDefaultAsync(c => c.VolumeId == volume.Id && c.Number == number && c.Title == title, ct);
        if (existing is not null) return existing;

        var chapter = new Chapter
        {
            VolumeId = volume.Id,
            Number = number,
            Title = title,
            Range = number.ToString(CultureInfo.InvariantCulture),
        };
        _db.Chapters.Add(chapter);
        await _db.SaveChangesAsync(ct);
        return chapter;
    }

    // Series cover image filenames to look for, in order of preference.
    private static readonly string[] CoverFileNames = { "folder", "cover", "poster", "default" };

    /// <summary>
    /// For each series in this scan, reads its top-level folder for sidecar assets: a cover image
    /// (folder.jpg/cover.jpg/poster.jpg) and a series-level <c>ComicInfo.xml</c>. Runs on every scan
    /// (including incremental) so existing libraries pick up art and metadata added after the first scan.
    /// </summary>
    private async Task ApplyFolderAssetsAsync(
        Library library, List<ContentUnit> units, CancellationToken ct)
    {
        // Map series name -> the folder that holds its assets, plus the provider that can read it.
        // First occurrence wins (a series can appear under several paths).
        var seriesDirs = new Dictionary<string, (string Dir, IStorageProvider Provider)>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units)
        {
            if (!TryGetSeriesDir(unit, out var dir)) continue;
            var (seriesName, _) = ResolveSeries(unit);
            seriesDirs.TryAdd(seriesName, (dir, unit.Provider));
        }

        foreach (var (seriesName, target) in seriesDirs)
        {
            ct.ThrowIfCancellationRequested();
            var series = await _db.Series
                .FirstOrDefaultAsync(s => s.LibraryId == library.Id && s.Name == seriesName, ct);
            if (series is null) continue;

            IReadOnlyList<StorageEntry> entries;
            try
            {
                entries = await target.Provider.ListAsync(target.Dir, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list series folder '{Dir}' for assets", target.Dir);
                continue;
            }

            await TryApplyFolderCoverAsync(series, entries, target.Provider, ct);
            await TryApplySidecarMetadataAsync(series, entries, target.Provider, ct);
        }
    }

    /// <summary>
    /// Resolves the on-disk series folder for a unit (the top-level folder under its storage root), or
    /// false when the unit isn't inside a series folder (a loose file directly in the root).
    /// </summary>
    private static bool TryGetSeriesDir(ContentUnit unit, out string dir)
    {
        dir = string.Empty;
        if (unit.Segments.Length == 0) return false;
        // Needs a containing series folder: folder-units always have one; file-units need >= 2 segments.
        if (!unit.IsFolder && unit.Segments.Length < 2) return false;
        dir = JoinPath(unit.Root, unit.Segments[0]);
        return true;
    }

    /// <summary>
    /// Lists <paramref name="dir"/> and applies a series-level cover image when present. Convenience
    /// wrapper used during the main scan loop (where entries haven't been listed yet).
    /// </summary>
    private async Task<bool> TryApplyFolderCoverAsync(
        Series series, string dir, IStorageProvider provider, CancellationToken ct)
    {
        try
        {
            var entries = await provider.ListAsync(dir, ct);
            return await TryApplyFolderCoverAsync(series, entries, provider, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply folder cover for series '{Series}' from '{Dir}'", series.Name, dir);
            return false;
        }
    }

    /// <summary>
    /// Looks for a series-level cover image (folder.jpg/cover.jpg/...) among <paramref name="entries"/>
    /// and, if found, makes it the series cover — overriding any page extracted from a chapter. Returns
    /// true when a cover was applied. Failures are logged and swallowed so one bad folder can't fail the scan.
    /// </summary>
    private async Task<bool> TryApplyFolderCoverAsync(
        Series series, IReadOnlyList<StorageEntry> entries, IStorageProvider provider, CancellationToken ct)
    {
        try
        {
            var coverEntry = FindCoverImage(entries);
            if (coverEntry is null) return false;

            await using var stream = await provider.OpenReadAsync(coverEntry.FullPath, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);

            var resized = ImageHelper.ResizeCover(ms.ToArray());
            var seriesCover = _paths.CoverFileForSeries(series.Id);
            await File.WriteAllBytesAsync(seriesCover, resized, ct);
            series.CoverPath = seriesCover;
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply folder cover for series '{Series}'", series.Name);
            return false;
        }
    }

    /// <summary>
    /// Reads a series-level sidecar <c>ComicInfo.xml</c> (sitting next to the chapter archives, the layout
    /// produced by many downloaders) and fills in series metadata. Uses fallback semantics: only fields
    /// not already populated by a chapter's embedded ComicInfo.xml are filled, so CBZ-level data wins.
    /// User-locked series are left untouched. Failures are logged and swallowed.
    /// </summary>
    private async Task TryApplySidecarMetadataAsync(
        Series series, IReadOnlyList<StorageEntry> entries, IStorageProvider provider, CancellationToken ct)
    {
        if (series.MetadataLocked) return;

        var sidecar = entries.FirstOrDefault(e => !e.IsDirectory &&
            Path.GetFileName(e.Name).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
        if (sidecar is null) return;

        try
        {
            await using var stream = await provider.OpenReadAsync(sidecar.FullPath, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            ms.Position = 0;

            var ci = _comicInfo.Parse(ms);
            if (ci is null) return;

            var changed = false;
            if (string.IsNullOrEmpty(series.Summary) && ci.Summary is not null) { series.Summary = ci.Summary; changed = true; }
            if (string.IsNullOrEmpty(series.Publisher) && ci.Publisher is not null) { series.Publisher = ci.Publisher; changed = true; }
            if (string.IsNullOrEmpty(series.Language) && ci.Language is not null) { series.Language = ci.Language; changed = true; }
            if (string.IsNullOrEmpty(series.Genres) && ci.Genre is not null) { series.Genres = ci.Genre; changed = true; }
            if (string.IsNullOrEmpty(series.Tags) && ci.Tags is not null) { series.Tags = ci.Tags; changed = true; }
            if (string.IsNullOrEmpty(series.AgeRating) && ci.AgeRating is not null)
            {
                series.AgeRating = ci.AgeRating;
                series.AgeRatingTier = AgeRatingMap.Tier(ci.AgeRating);
                changed = true;
            }
            if (string.IsNullOrEmpty(series.People) && (ci.Writer is not null || ci.Penciller is not null))
            {
                series.People = JsonSerializer.Serialize(new { writer = ci.Writer, penciller = ci.Penciller });
                changed = true;
            }

            if (changed)
            {
                series.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply sidecar ComicInfo.xml for series '{Series}'", series.Name);
        }
    }

    private static StorageEntry? FindCoverImage(IReadOnlyList<StorageEntry> entries)
    {
        var images = entries.Where(e => !e.IsDirectory && ImageFormats.IsImage(e.Name)).ToList();
        foreach (var preferred in CoverFileNames)
        {
            var match = images.FirstOrDefault(e =>
                Path.GetFileNameWithoutExtension(e.Name).Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }

    private static bool IsCoverFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return CoverFileNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    private static string JoinPath(string root, string segment)
    {
        var r = root.Replace('/', '\\').TrimEnd('\\');
        return $"{r}\\{segment}";
    }

    private async Task CacheCoverAsync(
        Series series, Chapter chapter, ContentUnit unit, IStorageProvider provider, CancellationToken ct)
    {
        try
        {
            var raw = await _readers.GetRawCoverAsync(unit.Format, unit.Path, provider, ct);
            if (raw is null) return;

            var resized = ImageHelper.ResizeCover(raw);
            var chapterCover = _paths.CoverFileForChapter(chapter.Id);
            await File.WriteAllBytesAsync(chapterCover, resized, ct);
            chapter.CoverPath = chapterCover;

            if (string.IsNullOrEmpty(series.CoverPath))
            {
                var seriesCover = _paths.CoverFileForSeries(series.Id);
                await File.WriteAllBytesAsync(seriesCover, resized, ct);
                series.CoverPath = seriesCover;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache cover for chapter {Chapter}", chapter.Id);
        }
    }

    private async Task ApplyMetadataAsync(
        Series series, Chapter chapter, ContentUnit unit, IStorageProvider provider, CancellationToken ct)
    {
        if (series.MetadataLocked) return;

        try
        {
            var kind = FormatRegistry.FromFormat(unit.Format);
            if (kind == MediaKind.ComicArchive)
            {
                await using var stream = await provider.OpenReadAsync(unit.Path, ct);
                var ci = _comicInfo.ReadFromArchive(stream);
                if (ci is null) return;

                if (ci.Summary is not null) series.Summary = ci.Summary;
                if (ci.Publisher is not null) series.Publisher = ci.Publisher;
                if (ci.Language is not null) series.Language = ci.Language;
                if (ci.Genre is not null) series.Genres = ci.Genre;
                if (ci.Tags is not null) series.Tags = ci.Tags;
                if (ci.AgeRating is not null)
                {
                    series.AgeRating = ci.AgeRating;
                    series.AgeRatingTier = AgeRatingMap.Tier(ci.AgeRating);
                }
                if (ci.Writer is not null || ci.Penciller is not null)
                    series.People = JsonSerializer.Serialize(new { writer = ci.Writer, penciller = ci.Penciller });
                if (ci.Title is not null) chapter.Title = ci.Title;
                series.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            else if (kind == MediaKind.Epub)
            {
                await using var stream = await provider.OpenReadAsync(unit.Path, ct);
                var meta = await _epub.ReadMetadataAsync(stream, ct);
                if (meta.Description is not null) series.Summary = meta.Description;
                if (meta.Publisher is not null) series.Publisher = meta.Publisher;
                if (meta.Language is not null) series.Language = meta.Language;
                if (meta.Author is not null)
                    series.People = JsonSerializer.Serialize(new { writer = meta.Author });
                series.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read metadata for '{Path}'", unit.Path);
        }
    }

    private async Task<int> RemoveMissingAsync(int libraryId, HashSet<string> seenPaths, CancellationToken ct)
    {
        var dbFiles = await _db.MangaFiles
            .Include(f => f.Chapter).ThenInclude(c => c.Volume).ThenInclude(v => v.Series)
            .Where(f => f.Chapter.Volume.Series.LibraryId == libraryId)
            .ToListAsync(ct);

        var removed = 0;
        foreach (var file in dbFiles)
        {
            if (seenPaths.Contains(file.StoragePath)) continue;
            _db.Chapters.Remove(file.Chapter);
            removed++;
        }
        if (removed > 0) await _db.SaveChangesAsync(ct);
        return removed;
    }

    private static string ComputeHash(ContentUnit unit) => unit.IsFolder
        ? $"dir:{unit.Size}:{unit.LastModified.Ticks}:{unit.ImageCount}"
        : $"{unit.Size}:{unit.LastModified.Ticks}";

    private static string[] RelativeSegments(string root, string full)
    {
        var r = root.Replace('/', '\\').TrimEnd('\\');
        var f = full.Replace('/', '\\');
        var rel = f.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? f[r.Length..] : f;
        return rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a.Replace('/', '\\').TrimEnd('\\'), b.Replace('/', '\\').TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static string LastSegment(string path)
    {
        var p = path.Replace('/', '\\').TrimEnd('\\');
        var idx = p.LastIndexOf('\\');
        return idx >= 0 ? p[(idx + 1)..] : p;
    }

    private static string StripExt(string fileName)
    {
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return fileName[..^7];
        var ext = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(ext) ? fileName : fileName[..^ext.Length];
    }
}
