using System.Net;
using System.Net.Sockets;
using SMBLibrary;
using SMBLibrary.Client;
using FileAttributes = SMBLibrary.FileAttributes;

namespace Mangrove.Server.Storage.Smb;

/// <summary>
/// A pooled, authenticated SMB2/3 session bound to a single {host, share, credential}. All
/// operations are serialized through <see cref="Gate"/> because <see cref="SMB2Client"/> is not
/// thread-safe; this also enforces the per-share concurrency cap from spec §5.
/// </summary>
public sealed class SmbConnection : IDisposable
{
    private readonly string _host;
    private readonly string _share;
    private readonly SmbCredential _credential;
    private readonly ILogger _logger;

    private SMB2Client? _client;
    private ISMBFileStore? _fileStore;

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public SmbConnection(string host, string share, SmbCredential credential, ILogger logger)
    {
        _host = host;
        _share = share;
        _credential = credential;
        _logger = logger;
    }

    public uint MaxReadSize => _client?.MaxReadSize ?? 65536u;

    public bool IsHealthy => _client is { IsConnected: true } && _fileStore is not null;

    /// <summary>Connects + logs in + tree-connects, throwing a descriptive error on failure.</summary>
    public void EnsureConnected()
    {
        if (IsHealthy) return;

        Cleanup();

        var client = new SMB2Client();
        var address = ResolveHost(_host);

        var connected = client.Connect(address, SMBTransportType.DirectTCPTransport);
        if (!connected)
            throw new IOException($"Unable to reach SMB host '{_host}' ({address}) on port 445.");

        var loginStatus = client.Login(_credential.Domain ?? string.Empty,
            _credential.Username ?? string.Empty, _credential.Password ?? string.Empty);
        if (loginStatus != NTStatus.STATUS_SUCCESS)
        {
            client.Disconnect();
            throw new UnauthorizedAccessException(
                $"SMB login to '{_host}' failed for user '{_credential.Username}': {loginStatus}.");
        }

        var fileStore = client.TreeConnect(_share, out var treeStatus);
        if (treeStatus != NTStatus.STATUS_SUCCESS || fileStore is null)
        {
            client.Logoff();
            client.Disconnect();
            throw new IOException($"Unable to connect to share '\\\\{_host}\\{_share}': {treeStatus}.");
        }

        _client = client;
        _fileStore = fileStore;
        _logger.LogDebug("Opened SMB session to \\\\{Host}\\{Share}", _host, _share);
    }

    /// <summary>Lists immediate children of a relative path ("" = share root).</summary>
    public IReadOnlyList<StorageEntry> List(string relativePath, string hostForFullPath, string shareForFullPath)
    {
        EnsureConnected();
        var store = _fileStore!;

        var status = store.CreateFile(out var handle, out _, relativePath,
            AccessMask.GENERIC_READ, FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
        ThrowIfBad(status, $"open directory '{relativePath}'");

        try
        {
            status = store.QueryDirectory(out var entries, handle, "*",
                FileInformationClass.FileDirectoryInformation);
            if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                ThrowIfBad(status, $"list directory '{relativePath}'");

            var results = new List<StorageEntry>();
            foreach (var item in entries)
            {
                if (item is not FileDirectoryInformation info) continue;
                if (info.FileName is "." or "..") continue;

                var isDir = (info.FileAttributes & FileAttributes.Directory) != 0;
                var rel = string.IsNullOrEmpty(relativePath)
                    ? info.FileName
                    : relativePath + "\\" + info.FileName;
                var full = $"\\\\{hostForFullPath}\\{shareForFullPath}\\{rel}";
                results.Add(new StorageEntry(info.FileName, full, isDir,
                    isDir ? 0 : info.EndOfFile, info.LastWriteTime));
            }
            return results;
        }
        finally
        {
            store.CloseFile(handle);
        }
    }

    public StorageStat Stat(string relativePath)
    {
        EnsureConnected();
        var store = _fileStore!;

        // Try as a file/dir; FILE_OPEN fails with NOT_FOUND when absent.
        var status = store.CreateFile(out var handle, out _, relativePath,
            AccessMask.GENERIC_READ, FileAttributes.Normal,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN, CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT, null);

        if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND ||
            status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND ||
            status == NTStatus.STATUS_NO_SUCH_FILE)
            return new StorageStat(false, false, 0, default);

        ThrowIfBad(status, $"stat '{relativePath}'");
        try
        {
            var infoStatus = store.GetFileInformation(out var fileInfo, handle,
                FileInformationClass.FileNetworkOpenInformation);
            ThrowIfBad(infoStatus, $"query info '{relativePath}'");
            var net = (FileNetworkOpenInformation)fileInfo;
            var isDir = (net.FileAttributes & FileAttributes.Directory) != 0;
            var lastWrite = net.LastWriteTime ?? DateTime.UtcNow;
            return new StorageStat(true, isDir, net.EndOfFile, lastWrite);
        }
        finally
        {
            store.CloseFile(handle);
        }
    }

    /// <summary>Opens a file handle for reading. Caller owns + must close the handle.</summary>
    public object OpenFile(string relativePath)
    {
        EnsureConnected();
        var store = _fileStore!;
        var status = store.CreateFile(out var handle, out _, relativePath,
            AccessMask.GENERIC_READ, FileAttributes.Normal, ShareAccess.Read,
            CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
        ThrowIfBad(status, $"open file '{relativePath}'");
        return handle;
    }

    /// <summary>Reads up to <paramref name="maxCount"/> bytes at <paramref name="offset"/>.</summary>
    public byte[] Read(object handle, long offset, int maxCount)
    {
        var store = _fileStore!;
        var status = store.ReadFile(out var data, handle, offset, maxCount);
        if (status == NTStatus.STATUS_END_OF_FILE)
            return Array.Empty<byte>();
        ThrowIfBad(status, "read file");
        return data ?? Array.Empty<byte>();
    }

    public void CloseHandle(object handle)
    {
        try { _fileStore?.CloseFile(handle); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error closing SMB handle"); }
    }

    private static IPAddress ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        var addresses = Dns.GetHostAddresses(host);
        var v4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        return v4 ?? addresses.FirstOrDefault()
            ?? throw new IOException($"Could not resolve SMB host '{host}'.");
    }

    private void ThrowIfBad(NTStatus status, string what)
    {
        if (status == NTStatus.STATUS_SUCCESS) return;
        if (status is NTStatus.STATUS_USER_SESSION_DELETED)
        {
            // Session went away; drop it so the next call reconnects.
            Cleanup();
        }
        throw new IOException($"SMB operation failed ({what}): {status}.");
    }

    private void Cleanup()
    {
        try { _fileStore?.Disconnect(); } catch { /* ignore */ }
        try { _client?.Logoff(); } catch { /* ignore */ }
        try { _client?.Disconnect(); } catch { /* ignore */ }
        _fileStore = null;
        _client = null;
    }

    public void Dispose()
    {
        Cleanup();
        Gate.Dispose();
    }
}
