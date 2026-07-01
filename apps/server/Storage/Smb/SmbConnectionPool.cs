using System.Collections.Concurrent;

namespace Mangrove.Server.Storage.Smb;

/// <summary>
/// Reuses SMB sessions keyed by {host, share, credentialKey} (spec §5). Each key keeps a small set of
/// connections instead of a single one: because every read is serialized through a connection's gate
/// (the SMB client isn't thread-safe), one connection alone forces ALL reads on a share to run one at
/// a time. That makes the reader slow — e.g. buffering a chapter archive in the background blocks the
/// very page reads it's meant to speed up. Spreading work across a few connections lets the background
/// buffering and on-demand page reads (and multiple readers) proceed in parallel, which is the
/// per-share concurrency cap from spec §5. Connections are created lazily and re-validated on each use.
/// </summary>
public sealed class SmbConnectionPool : IDisposable
{
    /// <summary>How many parallel SMB channels to keep per share. Small enough to stay friendly to
    /// NAS connection limits, large enough to overlap background buffering with live page reads.</summary>
    private const int ConnectionsPerShare = 4;

    private sealed class Slot
    {
        public required SmbConnection[] Connections;
        public int Cursor;
    }

    private readonly ConcurrentDictionary<string, Slot> _pool = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SmbConnectionPool> _logger;

    public SmbConnectionPool(ILogger<SmbConnectionPool> logger) => _logger = logger;

    public SmbConnection Acquire(string host, string share, SmbCredential credential)
    {
        var key = $"{host}|{share}|{credential.Key}".ToLowerInvariant();
        var slot = _pool.GetOrAdd(key, _ => new Slot
        {
            Connections = Enumerable
                .Range(0, ConnectionsPerShare)
                .Select(_ => new SmbConnection(host, share, credential, _logger))
                .ToArray(),
        });

        // Round-robin across the share's connections so concurrent operations land on different
        // channels and don't serialize behind each other on a single gate.
        var index = (int)((uint)Interlocked.Increment(ref slot.Cursor) % (uint)slot.Connections.Length);
        var connection = slot.Connections[index];

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
        foreach (var slot in _pool.Values)
            foreach (var c in slot.Connections)
                c.Dispose();
        _pool.Clear();
    }
}
