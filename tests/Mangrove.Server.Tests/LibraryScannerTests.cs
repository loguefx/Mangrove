using System.IO.Compression;
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
            new ArchiveReader(), new ImageFolderReader(), new PdfPageReader(), new EpubService());
        return new LibraryScanner(_db, factory, readers, new ComicInfoReader(), new EpubService(),
            paths, new FilenameParser(), NullLogger<LibraryScanner>.Instance);
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

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        try { Directory.Delete(Directory.GetParent(_root)!.FullName, recursive: true); } catch { /* best effort */ }
    }
}
