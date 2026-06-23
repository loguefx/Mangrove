using Microsoft.Extensions.Hosting.WindowsServices;

namespace Mangrove.Server;

/// <summary>
/// Resolves runtime paths (spec §12), creating directories as needed. All mutable state — the
/// database, cover cache and secrets — lives under a single <see cref="DataDir"/> that is kept
/// OUTSIDE the install folder for service installs, so updating the binaries never wipes user data
/// (favorites, reading progress, accounts). On first run after upgrading, an existing database that
/// still sits next to the executable is migrated into the new data directory automatically.
/// </summary>
public sealed class ServerPaths
{
    public string DataDir { get; }
    public string DbPath { get; }
    public string CacheDir { get; }
    public string CoversDir { get; }
    public string SecretsPath { get; }
    public string UpdatesDir { get; }

    public ServerPaths(IConfiguration config, ILogger? logger = null)
    {
        DataDir = ResolveDataDir(config);
        Directory.CreateDirectory(DataDir);

        // Explicit overrides still win (back-compat / advanced setups); otherwise derive from DataDir.
        DbPath = config["MANGROVE_DB_PATH"]
                 ?? Environment.GetEnvironmentVariable("MANGROVE_DB_PATH")
                 ?? Path.Combine(DataDir, "mangrove.db");

        CacheDir = config["MANGROVE_CACHE_DIR"]
                   ?? Environment.GetEnvironmentVariable("MANGROVE_CACHE_DIR")
                   ?? Path.Combine(DataDir, "cache");

        CoversDir = Path.Combine(CacheDir, "covers");
        SecretsPath = Path.Combine(DataDir, "secrets.json");
        UpdatesDir = Path.Combine(DataDir, "updates");

        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(CoversDir);

        var dbDir = Path.GetDirectoryName(Path.GetFullPath(DbPath));
        if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);

        MigrateLegacyData(logger);
    }

    /// <summary>
    /// Picks the data directory: an explicit <c>MANGROVE_DATA_DIR</c> wins; otherwise a Windows
    /// service stores data under <c>%ProgramData%\Mangrove</c> (survives updates), while a dev/console
    /// run keeps data next to the working directory (preserving the local dev database).
    /// </summary>
    private static string ResolveDataDir(IConfiguration config)
    {
        var configured = config["MANGROVE_DATA_DIR"]
                         ?? Environment.GetEnvironmentVariable("MANGROVE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "Mangrove");
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// One-time rescue: if the chosen data directory has no database yet but one still sits next to
    /// the executable (the pre-upgrade layout), copy the DB (+ WAL/SHM) and cover cache across so an
    /// in-place upgrade keeps all existing data. Best-effort; failures are logged, not fatal.
    /// </summary>
    private void MigrateLegacyData(ILogger? logger)
    {
        try
        {
            var legacyDir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(legacyDir)) return;
            if (PathsEqual(legacyDir, DataDir)) return;     // already using the legacy location
            if (File.Exists(DbPath)) return;                // new location already populated

            var legacyDb = Path.Combine(legacyDir, "mangrove.db");
            if (!File.Exists(legacyDb)) return;

            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var src = legacyDb + suffix;
                if (File.Exists(src)) File.Copy(src, DbPath + suffix, overwrite: true);
            }

            var legacyCache = Path.Combine(legacyDir, "cache");
            if (Directory.Exists(legacyCache)) CopyDirectory(legacyCache, CacheDir);

            logger?.LogInformation(
                "Migrated existing data from '{Legacy}' to '{DataDir}'. Future updates will not touch it.",
                legacyDir, DataDir);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to migrate legacy data into '{DataDir}'.", DataDir);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public string CoverFileForChapter(int chapterId) =>
        Path.Combine(CoversDir, $"chapter-{chapterId}.jpg");

    public string CoverFileForSeries(int seriesId) =>
        Path.Combine(CoversDir, $"series-{seriesId}.jpg");
}
