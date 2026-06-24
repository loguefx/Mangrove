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

/// <summary>Live progress of the running scan. <see cref="Total"/> is 0 while still indeterminate.</summary>
public readonly record struct ScanProgress(int Done, int Total, string? Phase);

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

    // Progress of the currently running scan, reported by the scanner as it works.
    private readonly object _progressLock = new();
    private int _progressLib;
    private int _done;
    private int _total;
    private string? _phase;

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
        SetProgress(libraryId, 0, 0, "Starting…");
    }

    public void MarkDone(int libraryId)
    {
        lock (_lock)
        {
            if (_running == libraryId) _running = 0;
        }
        lock (_progressLock)
        {
            if (_progressLib == libraryId)
            {
                _progressLib = 0;
                _done = _total = 0;
                _phase = null;
            }
        }
    }

    /// <summary>Reports progress for the running scan; the status endpoint surfaces this to the UI.</summary>
    public void SetProgress(int libraryId, int done, int total, string? phase)
    {
        lock (_progressLock)
        {
            _progressLib = libraryId;
            _done = done;
            _total = total;
            _phase = phase;
        }
    }

    public ScanProgress GetProgress(int libraryId)
    {
        lock (_progressLock)
            return _progressLib == libraryId ? new ScanProgress(_done, _total, _phase) : default;
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
