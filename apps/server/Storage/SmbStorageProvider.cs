using Mangrove.Server.Storage.Smb;

namespace Mangrove.Server.Storage;

/// <summary>
/// <see cref="IStorageProvider"/> backed by SMBLibrary (TalAloni) — a pure-C# SMB2/3 client.
/// Reads shares with no OS mount (spec §5). Bound to one credential; the connection pool keys
/// sessions per {host, share, credential}.
/// </summary>
public sealed class SmbStorageProvider : IStorageProvider
{
    private readonly SmbConnectionPool _pool;
    private readonly SmbCredential _credential;

    public SmbStorageProvider(SmbConnectionPool pool, SmbCredential credential)
    {
        _pool = pool;
        _credential = credential;
    }

    public Task<IReadOnlyList<StorageEntry>> ListAsync(string path, CancellationToken ct = default)
    {
        var sp = StoragePath.ParseRemote(path);
        var conn = _pool.Acquire(sp.Host, sp.Share, _credential);
        conn.Gate.Wait(ct);
        try
        {
            return Task.FromResult(conn.List(sp.RelativePath, sp.Host, sp.Share));
        }
        finally
        {
            conn.Gate.Release();
        }
    }

    public Task<StorageStat> StatAsync(string path, CancellationToken ct = default)
    {
        var sp = StoragePath.ParseRemote(path);
        var conn = _pool.Acquire(sp.Host, sp.Share, _credential);
        conn.Gate.Wait(ct);
        try
        {
            return Task.FromResult(conn.Stat(sp.RelativePath));
        }
        finally
        {
            conn.Gate.Release();
        }
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var sp = StoragePath.ParseRemote(path);
        var conn = _pool.Acquire(sp.Host, sp.Share, _credential);

        conn.Gate.Wait(ct);
        object handle;
        long length;
        try
        {
            handle = conn.OpenFile(sp.RelativePath);
            length = conn.Stat(sp.RelativePath).Size;
        }
        catch
        {
            conn.Gate.Release();
            throw;
        }
        conn.Gate.Release();

        Stream stream = new SmbReadStream(conn, handle, length);
        return Task.FromResult(stream);
    }
}
