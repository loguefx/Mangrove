using Mangrove.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Scanning;

/// <summary>
/// Periodically queues incremental scans of every library so new chapters/series are picked up and
/// shown automatically — no manual scan needed. The cadence comes from the <c>scan.intervalMinutes</c>
/// app setting (0 disables it); a <c>scan.onStartup</c> setting triggers one scan shortly after boot.
/// Scans are incremental (unchanged files are skipped), so routine runs are cheap.
/// </summary>
public sealed class PeriodicScanService : BackgroundService
{
    private const int MinIntervalMinutes = 5;

    private readonly ScanJobQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PeriodicScanService> _log;

    public PeriodicScanService(ScanJobQueue queue, IServiceScopeFactory scopes, ILogger<PeriodicScanService> log)
    {
        _queue = queue;
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (DB migration, host binding) before touching storage.
        if (!await DelayAsync(TimeSpan.FromSeconds(15), stoppingToken)) return;

        if (await GetBoolSettingAsync("scan.onStartup", false, stoppingToken))
            await EnqueueAllAsync(stoppingToken);

        // Treat boot as the last scan point so the first periodic scan happens one interval later.
        var lastRun = DateTime.UtcNow;

        // Poll on a short cadence and re-read the configured interval every tick, so changing
        // scan.intervalMinutes in the admin settings takes effect promptly (no restart needed).
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = await GetIntervalAsync(stoppingToken);
            if (interval is not null && DateTime.UtcNow - lastRun >= interval.Value)
            {
                await EnqueueAllAsync(stoppingToken);
                lastRun = DateTime.UtcNow;
            }

            if (!await DelayAsync(TimeSpan.FromSeconds(30), stoppingToken)) return;
        }
    }

    private async Task EnqueueAllAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MangroveDbContext>();
            var ids = await db.Libraries.Select(l => l.Id).ToListAsync(ct);

            var queued = 0;
            foreach (var id in ids)
                if (_queue.Enqueue(id, recordHistory: false)) queued++;

            if (queued > 0) _log.LogInformation("Auto-scan: queued {Count} libraries for an incremental scan.", queued);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Auto-scan: failed to queue libraries.");
        }
    }

    /// <summary>Returns the configured interval, or null when automatic scanning is disabled.</summary>
    private async Task<TimeSpan?> GetIntervalAsync(CancellationToken ct)
    {
        var minutes = await GetIntSettingAsync("scan.intervalMinutes", 15, ct);
        if (minutes <= 0) return null;
        return TimeSpan.FromMinutes(Math.Max(minutes, MinIntervalMinutes));
    }

    private async Task<int> GetIntSettingAsync(string key, int fallback, CancellationToken ct)
    {
        var raw = await GetSettingAsync(key, ct);
        return int.TryParse(raw, out var v) ? v : fallback;
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool fallback, CancellationToken ct)
    {
        var raw = await GetSettingAsync(key, ct);
        return bool.TryParse(raw, out var v) ? v : fallback;
    }

    private async Task<string?> GetSettingAsync(string key, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MangroveDbContext>();
            return await db.AppSettings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Delays for the given duration; returns false if cancellation was requested.</summary>
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
