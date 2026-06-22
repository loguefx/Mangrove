using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Mangrove.Server.Scanning;

public enum ScanState
{
    Idle,
    Queued,
    Running,
}

/// <summary>
/// In-process queue of library scans. Scans run one-at-a-time on a background worker
/// so the triggering HTTP request returns immediately and the API stays responsive.
/// </summary>
public sealed class ScanJobQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
    private readonly ConcurrentDictionary<int, byte> _queued = new();
    private readonly object _lock = new();
    private int _running;

    public bool Enqueue(int libraryId)
    {
        lock (_lock)
        {
            if (_running == libraryId) return false;            // already scanning
            if (!_queued.TryAdd(libraryId, 0)) return false;     // already queued
        }
        _channel.Writer.TryWrite(libraryId);
        return true;
    }

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void MarkRunning(int libraryId)
    {
        lock (_lock)
        {
            _queued.TryRemove(libraryId, out _);
            _running = libraryId;
        }
    }

    public void MarkDone(int libraryId)
    {
        lock (_lock)
        {
            if (_running == libraryId) _running = 0;
        }
    }

    public ScanState StateOf(int libraryId)
    {
        lock (_lock)
        {
            if (_running == libraryId) return ScanState.Running;
            return _queued.ContainsKey(libraryId) ? ScanState.Queued : ScanState.Idle;
        }
    }

    public bool IsBusy(int libraryId) => StateOf(libraryId) != ScanState.Idle;
}
