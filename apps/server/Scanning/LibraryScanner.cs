using System.Globalization;
using System.Text.Json;
using Mangrove.Server.Data;
using Mangrove.Server.Metadata;
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
    private readonly ScanJobQueue _queue;
    private readonly Metadata.AniListMetadataService _online;
    private readonly LibrarySidecarWriter _sidecar;
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
        ScanJobQueue queue,
        Metadata.AniListMetadataService online,
        LibrarySidecarWriter sidecar,
        ILogger<LibraryScanner> logger)
    {
        _db = db;
        _providers = providers;
        _readers = readers;
        _comicInfo = comicInfo;
        _epub = epub;
        _paths = paths;
        _parser = parser;
        _queue = queue;
        _online = online;
        _sidecar = sidecar;
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

        _queue.SetProgress(libraryId, 0, 0, "Collecting files…");
        var units = new List<ContentUnit>();
        foreach (var (root, prov) in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            await CollectUnitsAsync(prov, root, root, 0, units, ct);
        }

        // One smooth bar across the two slow passes (cover/metadata, then per-chapter indexing).
        var progressDone = 0;
        var progressTotal = units.Count; // assetDir count added once known, below

        // Quick pass: create every series and apply its folder cover + sidecar ComicInfo.xml up
        // front. This is cheap (a directory list + small XML per series) and is saved immediately,
        // so covers and metadata appear right away and survive even if the slower page-counting
        // pass below is interrupted (which is what previously left metadata unpopulated).
        var seriesCache = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        var seriesAssetDir = new Dictionary<string, (string Dir, IStorageProvider Provider)>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units)
        {
            var (sName, _) = ResolveSeries(unit);
            if (!seriesCache.ContainsKey(sName))
                seriesCache[sName] = await GetOrCreateSeriesAsync(library.Id, sName, ct);
            if (!seriesAssetDir.ContainsKey(sName) && TryGetSeriesDir(unit, out var sDir))
                seriesAssetDir[sName] = (sDir, unit.Provider);
        }
        progressTotal += seriesAssetDir.Count;
        foreach (var (sName, target) in seriesAssetDir)
        {
            ct.ThrowIfCancellationRequested();
            await TryApplyFolderAssetsAsync(seriesCache[sName], target.Dir, target.Provider, ct);
            _queue.SetProgress(libraryId, ++progressDone, progressTotal, "Reading covers & metadata…");
        }

        var added = 0;
        var updated = 0;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var unit in units)
        {
            ct.ThrowIfCancellationRequested();
            _queue.SetProgress(libraryId, ++progressDone, progressTotal, "Indexing chapters…");
            seenPaths.Add(unit.Path);

            var hash = ComputeHash(unit);
            var existing = await _db.MangaFiles
                .Include(f => f.Chapter)
                .FirstOrDefaultAsync(f => f.StoragePath == unit.Path
                    && f.Chapter.Volume.Series.LibraryId == library.Id, ct);

            if (existing is not null && existing.Hash == hash)
                continue; // unchanged — skip needless I/O (spec §8)

            var (seriesName, parsed) = ResolveSeries(unit);
            if (!seriesCache.TryGetValue(seriesName, out var series))
            {
                series = await GetOrCreateSeriesAsync(library.Id, seriesName, ct);
                seriesCache[seriesName] = series;
            }

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

        var removed = await RemoveMissingAsync(library.Id, seenPaths, ct);

        // Backup metadata: for series still missing a cover/summary after local sources, pull from an
        // online provider (Jellyfin-style). Cached per series so it isn't re-queried every scan.
        await ApplyOnlineMetadataAsync(libraryId, seriesCache.Values, ct);

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
    /// Reads a series' top-level folder once and applies its sidecar assets: a cover image
    /// (folder.jpg/cover.jpg/poster.jpg, only when the series has none yet) and a series-level
    /// <c>ComicInfo.xml</c>. Saved immediately so assets survive an interrupted scan.
    /// </summary>
    private async Task TryApplyFolderAssetsAsync(
        Series series, string dir, IStorageProvider provider, CancellationToken ct)
    {
        IReadOnlyList<StorageEntry> entries;
        try
        {
            entries = await provider.ListAsync(dir, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list series folder '{Dir}' for assets", dir);
            return;
        }

        // Decide whether the series needs a (new) poster: none yet, the file is gone, or the current
        // cover is a banner-shaped image (e.g. a wide folder.jpg backdrop) that crops to a blank card.
        var needsCover = string.IsNullOrEmpty(series.CoverPath) || !File.Exists(series.CoverPath);
        if (!needsCover && CurrentCoverIsBanner(series))
        {
            // Drop the banner so the portrait first-page cover (cached during chapter scanning) wins.
            series.CoverPath = null;
            await _db.SaveChangesAsync(ct);
            needsCover = true;
        }
        if (needsCover)
            await TryApplyFolderCoverAsync(series, entries, provider, ct);
        await TryApplySidecarMetadataAsync(series, entries, provider, ct);
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

            var bytes = ms.ToArray();
            // A wide banner makes a poor portrait poster; let the chapter's first page be the cover.
            if (ImageHelper.IsBannerAspect(bytes))
            {
                _logger.LogInformation(
                    "Ignoring banner-shaped folder cover for series '{Series}'; using page cover instead.",
                    series.Name);
                return false;
            }

            var resized = ImageHelper.ResizeCover(bytes);
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

    private sealed class ExternalIdsModel
    {
        public int? anilist { get; set; }
        public string? autoCheckedAt { get; set; }
    }

    /// <summary>
    /// Final scan pass: for any series that still lacks a cover, summary, or genres after local
    /// sources (folder.jpg / ComicInfo.xml), fetch metadata from the online provider and fill the
    /// gaps. Only empty fields are touched, user-locked series are skipped, and results are cached in
    /// <see cref="Series.ExternalIds"/> so subsequent scans don't re-query.
    /// </summary>
    private async Task ApplyOnlineMetadataAsync(int libraryId, IEnumerable<Series> seriesList, CancellationToken ct)
    {
        if (!await OnlineEnabledAsync(ct)) return;

        var pending = seriesList.Where(s => !s.MetadataLocked && NeedsOnline(s) && !AlreadyChecked(s)).ToList();
        if (pending.Count == 0) return;

        var done = 0;
        foreach (var series in pending)
        {
            ct.ThrowIfCancellationRequested();
            _queue.SetProgress(libraryId, ++done, pending.Count, "Fetching metadata online…");

            OnlineSeriesMetadata? meta;
            try
            {
                meta = await _online.FetchAsync(series.Name, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Online metadata lookup failed for '{Series}'", series.Name);
                continue;
            }

            if (meta is null)
            {
                MarkChecked(series, null); // remember the miss so we don't hammer the API each scan
                await _db.SaveChangesAsync(ct);
                continue;
            }

            if (string.IsNullOrEmpty(series.Summary) && !string.IsNullOrWhiteSpace(meta.Summary))
                series.Summary = meta.Summary;
            if (string.IsNullOrEmpty(series.Genres) && !string.IsNullOrWhiteSpace(meta.Genres))
                series.Genres = meta.Genres;
            if (string.IsNullOrEmpty(series.Tags) && !string.IsNullOrWhiteSpace(meta.Tags))
                series.Tags = meta.Tags;
            if (string.IsNullOrEmpty(series.AgeRating) && !string.IsNullOrWhiteSpace(meta.AgeRating))
            {
                series.AgeRating = meta.AgeRating;
                series.AgeRatingTier = AgeRatingMap.Tier(meta.AgeRating);
            }
            if (string.IsNullOrEmpty(series.People) && (meta.Writer is not null || meta.Penciller is not null))
                series.People = JsonSerializer.Serialize(new { writer = meta.Writer, penciller = meta.Penciller });

            byte[]? coverJpeg = null;
            if (!HasCover(series) && !string.IsNullOrWhiteSpace(meta.CoverUrl))
                coverJpeg = await TryApplyOnlineCoverAsync(series, meta.CoverUrl!, ct);

            MarkChecked(series, meta.AniListId);
            series.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Persist the fetched metadata back into the library as sidecars so it acts as a permanent
            // cache: folder.jpg becomes the cover, ComicInfo.xml carries the text fields. Both are what
            // the scanner reads first, so the data survives re-scans and DB resets.
            if (coverJpeg is not null) await _sidecar.WriteCoverAsync(series, coverJpeg, ct);
            await _sidecar.WriteComicInfoAsync(series, ct);
        }

        _logger.LogInformation("Online metadata: filled gaps for up to {Count} series in library {Id}.", pending.Count, libraryId);
    }

    /// <summary>Downloads + caches the online cover locally, returning the resized JPEG bytes (or null).</summary>
    private async Task<byte[]?> TryApplyOnlineCoverAsync(Series series, string url, CancellationToken ct)
    {
        var raw = await _online.DownloadImageAsync(url, ct);
        if (raw is null) return null;
        try
        {
            var resized = ImageHelper.ResizeCover(raw);
            var path = _paths.CoverFileForSeries(series.Id);
            await File.WriteAllBytesAsync(path, resized, ct);
            series.CoverPath = path;
            return resized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save online cover for series '{Series}'", series.Name);
            return null;
        }
    }

    private static bool HasCover(Series s) => !string.IsNullOrEmpty(s.CoverPath) && File.Exists(s.CoverPath);

    /// <summary>True if the series' current cached cover is a banner-shaped (non-poster) image.</summary>
    private static bool CurrentCoverIsBanner(Series s)
    {
        if (string.IsNullOrEmpty(s.CoverPath) || !File.Exists(s.CoverPath)) return false;
        try { return ImageHelper.IsBannerAspect(File.ReadAllBytes(s.CoverPath)); }
        catch { return false; }
    }

    private static bool NeedsOnline(Series s) =>
        string.IsNullOrEmpty(s.Summary) || string.IsNullOrEmpty(s.Genres) || !HasCover(s);

    /// <summary>True if we've already matched this series, or recently looked it up and found nothing.</summary>
    private static bool AlreadyChecked(Series s)
    {
        var model = ParseExternal(s.ExternalIds);
        if (model.anilist is > 0) return true;
        if (DateTime.TryParse(model.autoCheckedAt, out var when))
            return DateTime.UtcNow - when.ToUniversalTime() < TimeSpan.FromDays(30);
        return false;
    }

    private static void MarkChecked(Series s, int? anilistId)
    {
        var model = ParseExternal(s.ExternalIds);
        if (anilistId is > 0) model.anilist = anilistId;
        else model.autoCheckedAt = DateTime.UtcNow.ToString("o");
        s.ExternalIds = JsonSerializer.Serialize(model);
    }

    private static ExternalIdsModel ParseExternal(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ExternalIdsModel();
        try { return JsonSerializer.Deserialize<ExternalIdsModel>(json) ?? new ExternalIdsModel(); }
        catch { return new ExternalIdsModel(); }
    }

    private async Task<bool> OnlineEnabledAsync(CancellationToken ct)
    {
        var raw = await _db.AppSettings
            .Where(s => s.Key == "metadata.online.enabled")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return raw is null || !bool.TryParse(raw, out var b) || b; // default on
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
