using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Mangrove.Server.Scanning;

public enum ScanState
{
    Idle,
    Queued,
    Running,
}

/// <summary>A queued scan and whether it should be recorded in the task history.</summary>
public readonly record struct ScanRequest(int LibraryId, bool RecordHistory);

/// <summary>
/// In-process queue of library scans. Scans run one-at-a-time on a background worker
/// so the triggering HTTP request returns immediately and the API stays responsive.
/// </summary>
public sealed class ScanJobQueue
{
    private readonly Channel<ScanRequest> _channel = Channel.CreateUnbounded<ScanRequest>();
    private readonly ConcurrentDictionary<int, byte> _queued = new();
    private readonly object _lock = new();
    private int _running;

    public bool Enqueue(int libraryId, bool recordHistory = true)
    {
        lock (_lock)
        {
            if (_running == libraryId) return false;            // already scanning
            if (!_queued.TryAdd(libraryId, 0)) return false;     // already queued
        }
        _channel.Writer.TryWrite(new ScanRequest(libraryId, recordHistory));
        return true;
    }

    public IAsyncEnumerable<ScanRequest> DequeueAllAsync(CancellationToken ct) =>
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
