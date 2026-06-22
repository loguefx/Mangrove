using System.Collections.Concurrent;

namespace Mangrove.Server.Storage.Smb;

/// <summary>
/// Reuses SMB sessions keyed by {host, share, credentialKey} (spec §5). Connections are created
/// lazily and re-validated (reconnect with backoff) on each acquisition.
/// </summary>
public sealed class SmbConnectionPool : IDisposable
{
    private readonly ConcurrentDictionary<string, SmbConnection> _pool = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SmbConnectionPool> _logger;

    public SmbConnectionPool(ILogger<SmbConnectionPool> logger) => _logger = logger;

    public SmbConnection Acquire(string host, string share, SmbCredential credential)
    {
        var key = $"{host}|{share}|{credential.Key}".ToLowerInvariant();
        var connection = _pool.GetOrAdd(key,
            _ => new SmbConnection(host, share, credential, _logger));

        ConnectWithBackoff(connection, host, share);
        return connection;
    }

    private void ConnectWithBackoff(SmbConnection connection, string host, string share)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                connection.EnsureConnected();
                return;
            }
            catch (UnauthorizedAccessException)
            {
                throw; // credentials won't get better by retrying
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(ex,
                    "SMB connect attempt {Attempt}/{Max} to \\\\{Host}\\{Share} failed; retrying in {Delay}ms",
                    attempt, maxAttempts, host, share, delay.TotalMilliseconds);
                Thread.Sleep(delay);
            }
        }
        // Final attempt outside the swallow so the real error surfaces.
        connection.EnsureConnected();
    }

    public void Dispose()
    {
        foreach (var c in _pool.Values) c.Dispose();
        _pool.Clear();
    }
}
