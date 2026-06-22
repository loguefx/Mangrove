namespace Mangrove.Server.Storage;

/// <summary>Reads from the local (or OS-mounted) filesystem.</summary>
public sealed class LocalStorageProvider : IStorageProvider
{
    public Task<IReadOnlyList<StorageEntry>> ListAsync(string path, CancellationToken ct = default)
    {
        var sp = StoragePath.ParseLocal(path);
        var dir = sp.RelativePath;
        var results = new List<StorageEntry>();

        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<StorageEntry>>(results);

        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            var info = new DirectoryInfo(d);
            results.Add(new StorageEntry(info.Name, info.FullName, true, 0, info.LastWriteTimeUtc));
        }
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var info = new FileInfo(f);
            results.Add(new StorageEntry(info.Name, info.FullName, false, info.Length, info.LastWriteTimeUtc));
        }
        return Task.FromResult<IReadOnlyList<StorageEntry>>(results);
    }

    public Task<StorageStat> StatAsync(string path, CancellationToken ct = default)
    {
        var sp = StoragePath.ParseLocal(path);
        var p = sp.RelativePath;
        if (Directory.Exists(p))
        {
            var di = new DirectoryInfo(p);
            return Task.FromResult(new StorageStat(true, true, 0, di.LastWriteTimeUtc));
        }
        if (File.Exists(p))
        {
            var fi = new FileInfo(p);
            return Task.FromResult(new StorageStat(true, false, fi.Length, fi.LastWriteTimeUtc));
        }
        return Task.FromResult(new StorageStat(false, false, 0, default));
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var sp = StoragePath.ParseLocal(path);
        Stream s = new FileStream(sp.RelativePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 16, useAsync: true);
        return Task.FromResult(s);
    }
}
