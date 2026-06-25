using System.IO.Compression;
using System.Net.Http;
using Mangrove.Server;
using Mangrove.Server.Data;
using Mangrove.Server.Readers;
using Mangrove.Server.Scanning;
using Mangrove.Server.Security;
using Mangrove.Server.Storage;
using Mangrove.Server.Storage.Smb;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mangrove.Server.Tests;

// Integration test for the scanner over a real local CBZ (spec §8 + "scanner" test requirement).
public class LibraryScannerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MangroveDbContext _db;
    private readonly string _root;
    private readonly string _cache;

    public LibraryScannerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new MangroveDbContext(new DbContextOptionsBuilder<MangroveDbContext>()
            .UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        var baseDir = Path.Combine(Path.GetTempPath(), "mangrove-test-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(baseDir, "library");
        _cache = Path.Combine(baseDir, "cache"); // cache must live outside the scanned library root
        Directory.CreateDirectory(_root);
    }

    private LibraryScanner BuildScanner()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MANGROVE_CACHE_DIR"] = _cache,
                ["MANGROVE_DB_PATH"] = Path.Combine(_root, "test.db"),
            })
            .Build();

        var paths = new ServerPaths(config);
        var protector = new CredentialProtector(new byte[32]);
        var pool = new SmbConnectionPool(NullLogger<SmbConnectionPool>.Instance);
        var factory = new StorageProviderFactory(pool, protector);
        var readers = new ReaderService(
            new ArchiveReader(), new ArchiveCache(), new ImageFolderReader(), new PdfPageReader(), new EpubService());

        // Keep scans hermetic: disable the online metadata backup so tests never hit the network.
        if (!_db.AppSettings.Any(s => s.Key == "metadata.online.enabled"))
        {
            _db.AppSettings.Add(new AppSetting { Key = "metadata.online.enabled", Value = "false" });
            _db.SaveChanges();
        }
        var online = new Mangrove.Server.Metadata.AniListMetadataService(
            new StubHttpClientFactory(),
            NullLogger<Mangrove.Server.Metadata.AniListMetadataService>.Instance);
        var sidecar = new LibrarySidecarWriter(_db, factory, NullLogger<LibrarySidecarWriter>.Instance);
        return new LibraryScanner(_db, factory, readers, new ComicInfoReader(), new EpubService(),
            paths, new FilenameParser(), new ScanJobQueue(), online, sidecar,
            NullLogger<LibraryScanner>.Instance);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private void CreateCbz(string seriesFolder, string fileName, int pageCount)
    {
        var dir = Path.Combine(_root, seriesFolder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        for (var i = 1; i <= pageCount; i++)
        {
            var entry = zip.CreateEntry($"{i:00}.jpg");
            using var s = entry.Open();
            s.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 }); // dummy jpeg-ish bytes
        }
    }

    private async Task WriteSidecarAsync(
        string seriesFolder, string? summary = null, string? writer = null, string? penciller = null,
        string? publisher = null, string? genre = null, string? tags = null, string? languageIso = null,
        string? ageRating = null)
    {
        var dir = Path.Combine(_root, seriesFolder);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "ComicInfo.xml"),
            BuildComicInfoXml(summary, writer, penciller, publisher, genre, tags, languageIso, ageRating));
    }

    private void CreateCbzWithComicInfo(
        string seriesFolder, string fileName, int pageCount, string? summary = null, string? writer = null,
        string? publisher = null)
    {
        var dir = Path.Combine(_root, seriesFolder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        for (var i = 1; i <= pageCount; i++)
        {
            var entry = zip.CreateEntry($"{i:00}.jpg");
            using var s = entry.Open();
            s.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 });
        }
        var ci = zip.CreateEntry("ComicInfo.xml");
        using var cs = new StreamWriter(ci.Open());
        cs.Write(BuildComicInfoXml(summary, writer, null, publisher, null, null, null, null));
    }

    private static string BuildComicInfoXml(
        string? summary, string? writer, string? penciller, string? publisher, string? genre,
        string? tags, string? languageIso, string? ageRating)
    {
        string El(string name, string? value) =>
            string.IsNullOrEmpty(value) ? "" : $"  <{name}>{System.Security.SecurityElement.Escape(value)}</{name}>\n";
        return "<?xml version=\"1.0\"?>\n<ComicInfo>\n"
            + El("Summary", summary)
            + El("Writer", writer)
            + El("Penciller", penciller)
            + El("Publisher", publisher)
            + El("Genre", genre)
            + El("Tags", tags)
            + El("LanguageISO", languageIso)
            + El("AgeRating", ageRating)
            + "</ComicInfo>\n";
    }

    private async Task<int> CreateLibraryAsync()
    {
        var lib = new Library
        {
            Name = "Test",
            Type = LibraryType.Manga,
            StorageKind = StorageKind.Local,
            RootPath = _root,
        };
        _db.Libraries.Add(lib);
        await _db.SaveChangesAsync();
        return lib.Id;
    }

    [Fact]
    public async Task Scan_GroupsFilesIntoSeriesVolumeChapter()
    {
        CreateCbz("Demo Series", "Demo Series Vol.01 Ch.0001.cbz", 3);
        var libId = await CreateLibraryAsync();

        var result = await BuildScanner().ScanAsync(libId);

        Assert.Equal(1, result.ChaptersAdded);
        Assert.Equal(1, result.SeriesCount);

        var series = await _db.Series.Include(s => s.Volumes).ThenInclude(v => v.Chapters)
            .SingleAsync();
        Assert.Equal("Demo Series", series.Name);
        var chapter = series.Volumes.SelectMany(v => v.Chapters).Single();
        Assert.Equal(3, chapter.PageCount);
        Assert.Equal("cbz", chapter.FileFormat);
    }

    [Fact]
    public async Task Scan_UsesFolderImageAsSeriesCover_NotChapterPage()
    {
        // A series folder with chapter archives AND a folder.jpg cover. The series cover must be the
        // folder image (regression: it was previously falling back to a chapter's first page).
        CreateCbz("Demo Series", "Demo Series Vol.01 Ch.0001.cbz", 3);
        var folderBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xDB, 11, 22, 33, 44, 55, 66, 77, 88, 99, 0, 1, 2 };
        await File.WriteAllBytesAsync(Path.Combine(_root, "Demo Series", "folder.jpg"), folderBytes);

        var libId = await CreateLibraryAsync();
        await BuildScanner().ScanAsync(libId);

        var series = await _db.Series.SingleAsync();
        Assert.False(string.IsNullOrEmpty(series.CoverPath));
        // Undecodable bytes pass through ResizeCover unchanged, so the cached cover is byte-identical
        // to folder.jpg — proving the folder image (not a chapter page) was chosen.
        Assert.Equal(folderBytes, await File.ReadAllBytesAsync(series.CoverPath!));

        // The folder image must not be mistaken for a chapter.
        Assert.Equal(1, await _db.Chapters.CountAsync());
    }

    [Fact]
    public async Task Scan_ReadsSidecarComicInfo_AndFillsSeriesMetadata()
    {
        CreateCbz("Blue Lock", "Blue Lock - Chapter 001.cbz", 2);
        await WriteSidecarAsync("Blue Lock",
            summary: "Soccer battle royale.",
            writer: "Muneyuki Kaneshiro",
            penciller: "Yusuke Nomura",
            publisher: "Kodansha",
            genre: "Sports, Drama",
            tags: "soccer, shounen",
            languageIso: "en",
            ageRating: "Teen");

        var libId = await CreateLibraryAsync();
        await BuildScanner().ScanAsync(libId);

        var series = await _db.Series.SingleAsync(s => s.Name == "Blue Lock");
        Assert.Equal("Soccer battle royale.", series.Summary);
        Assert.Equal("Kodansha", series.Publisher);
        Assert.Equal("Sports, Drama", series.Genres);
        Assert.Equal("soccer, shounen", series.Tags);
        Assert.Equal("en", series.Language);
        Assert.Equal("Teen", series.AgeRating);
        Assert.Contains("Muneyuki Kaneshiro", series.People);
        Assert.Contains("Yusuke Nomura", series.People);
    }

    [Fact]
    public async Task Scan_SidecarIsFallback_CbzMetadataWins()
    {
        // The CBZ carries its own ComicInfo.xml (Summary). The sidecar provides a different Summary plus
        // a Publisher the CBZ lacks. The CBZ summary must win; the sidecar fills only the gap (Publisher).
        CreateCbzWithComicInfo("Chainsaw Man", "Chainsaw Man - Chapter 001.cbz", 2,
            summary: "From the CBZ.");
        await WriteSidecarAsync("Chainsaw Man",
            summary: "From the sidecar.", publisher: "Shueisha");

        var libId = await CreateLibraryAsync();
        await BuildScanner().ScanAsync(libId);

        var series = await _db.Series.SingleAsync(s => s.Name == "Chainsaw Man");
        Assert.Equal("From the CBZ.", series.Summary);   // CBZ takes precedence
        Assert.Equal("Shueisha", series.Publisher);      // sidecar fills the gap
    }

    [Fact]
    public async Task Scan_IsIdempotent_SkipsUnchangedFiles()
    {
        CreateCbz("Demo Series", "Demo Series Vol.01 Ch.0001.cbz", 2);
        var libId = await CreateLibraryAsync();
        var scanner = BuildScanner();

        var first = await scanner.ScanAsync(libId);
        Assert.Equal(1, first.ChaptersAdded);

        var second = await scanner.ScanAsync(libId);
        Assert.Equal(0, second.ChaptersAdded);
        Assert.Equal(0, second.ChaptersUpdated);
    }

    [Fact]
    public async Task Scan_TreatsRawImageFolderAsChapter()
    {
        // root/Picto/Chapter 1/{01,02,03}.jpg  -> series "Picto", one image-folder chapter.
        var chapterDir = Path.Combine(_root, "Picto", "Chapter 1");
        Directory.CreateDirectory(chapterDir);
        for (var i = 1; i <= 3; i++)
            await File.WriteAllBytesAsync(Path.Combine(chapterDir, $"{i:00}.jpg"),
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 9, 9 });

        var libId = await CreateLibraryAsync();
        var result = await BuildScanner().ScanAsync(libId);

        Assert.Equal(1, result.ChaptersAdded);
        var series = await _db.Series.Include(s => s.Volumes).ThenInclude(v => v.Chapters).SingleAsync();
        Assert.Equal("Picto", series.Name);
        var chapter = series.Volumes.SelectMany(v => v.Chapters).Single();
        Assert.Equal(3, chapter.PageCount);
        Assert.Equal("images", chapter.FileFormat);
    }

    [Fact]
    public async Task Scan_RemovesChaptersForDeletedFiles()
    {
        CreateCbz("Demo Series", "Demo Series Vol.01 Ch.0001.cbz", 2);
        var libId = await CreateLibraryAsync();
        var scanner = BuildScanner();
        await scanner.ScanAsync(libId);

        // Delete the file and rescan.
        File.Delete(Path.Combine(_root, "Demo Series", "Demo Series Vol.01 Ch.0001.cbz"));
        var result = await scanner.ScanAsync(libId);

        Assert.Equal(1, result.ChaptersRemoved);
        Assert.Equal(0, await _db.Chapters.CountAsync());
    }

    private static void CreateCbzIn(string root, string seriesFolder, string fileName, int pageCount)
    {
        var dir = Path.Combine(root, seriesFolder);
        Directory.CreateDirectory(dir);
        using var zip = ZipFile.Open(Path.Combine(dir, fileName), ZipArchiveMode.Create);
        for (var i = 1; i <= pageCount; i++)
        {
            var entry = zip.CreateEntry($"{i:00}.jpg");
            using var s = entry.Open();
            s.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 });
        }
    }

    [Fact]
    public async Task Scan_WalksMultipleLibraryPaths_AndMergesSeriesAcrossThem()
    {
        // A library spanning two folders, with the same series split across both (the "ran out of
        // space, continue on another share" case).
        var root2 = Path.Combine(Directory.GetParent(_root)!.FullName, "library2");
        Directory.CreateDirectory(root2);

        CreateCbz("Blue Lock", "Blue Lock Ch.0001.cbz", 2);
        CreateCbzIn(root2, "Blue Lock", "Blue Lock Ch.0002.cbz", 2);
        CreateCbzIn(root2, "Another Series", "Another Series Ch.0001.cbz", 2);

        var lib = new Library
        {
            Name = "Multi",
            Type = LibraryType.Manga,
            StorageKind = StorageKind.Local,
            RootPath = _root,
            Paths = new List<LibraryPath>
            {
                new() { Path = _root },
                new() { Path = root2 },
            },
        };
        _db.Libraries.Add(lib);
        await _db.SaveChangesAsync();

        var result = await BuildScanner().ScanAsync(lib.Id);

        Assert.Equal(3, result.ChaptersAdded);
        Assert.Equal(2, result.SeriesCount);

        var blueLock = await _db.Series
            .Include(s => s.Volumes).ThenInclude(v => v.Chapters)
            .FirstAsync(s => s.Name == "Blue Lock");
        // Both chapters (one from each folder) landed under the same series.
        Assert.Equal(2, blueLock.Volumes.SelectMany(v => v.Chapters).Count());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        try { Directory.Delete(Directory.GetParent(_root)!.FullName, recursive: true); } catch { /* best effort */ }
    }
}
