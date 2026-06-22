namespace Mangrove.Server;

/// <summary>Resolves runtime paths from environment (spec §12), creating directories as needed.</summary>
public sealed class ServerPaths
{
    public string DbPath { get; }
    public string CacheDir { get; }
    public string CoversDir { get; }

    public ServerPaths(IConfiguration config)
    {
        var dataRoot = AppContext.BaseDirectory;

        DbPath = config["MANGROVE_DB_PATH"]
                 ?? Environment.GetEnvironmentVariable("MANGROVE_DB_PATH")
                 ?? Path.Combine(Directory.GetCurrentDirectory(), "mangrove.db");

        CacheDir = config["MANGROVE_CACHE_DIR"]
                   ?? Environment.GetEnvironmentVariable("MANGROVE_CACHE_DIR")
                   ?? Path.Combine(Directory.GetCurrentDirectory(), "cache");

        CoversDir = Path.Combine(CacheDir, "covers");

        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(CoversDir);

        var dbDir = Path.GetDirectoryName(Path.GetFullPath(DbPath));
        if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);
    }

    public string CoverFileForChapter(int chapterId) =>
        Path.Combine(CoversDir, $"chapter-{chapterId}.jpg");

    public string CoverFileForSeries(int seriesId) =>
        Path.Combine(CoversDir, $"series-{seriesId}.jpg");
}
