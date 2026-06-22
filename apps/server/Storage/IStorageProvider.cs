namespace Mangrove.Server.Storage;

public sealed record StorageEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTime LastModified);

public sealed record StorageStat(bool Exists, bool IsDirectory, long Size, DateTime LastModified);

/// <summary>
/// Storage abstraction (spec §3). The scanner, cover extractor and reader endpoints only ever
/// talk to this interface, so SMB vs local is completely transparent to the rest of the app.
/// </summary>
public interface IStorageProvider
{
    /// <summary>Lists the immediate children of a directory.</summary>
    Task<IReadOnlyList<StorageEntry>> ListAsync(string path, CancellationToken ct = default);

    /// <summary>Returns metadata about a file or directory.</summary>
    Task<StorageStat> StatAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Opens a seekable, read-only stream for a file. For SMB this streams ranges on demand
    /// straight from the share (spec §5), never copying the whole file unless asked to.
    /// </summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken ct = default);
}
