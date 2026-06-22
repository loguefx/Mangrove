namespace Mangrove.Server.Scanning;

/// <summary>
/// Drains <see cref="ScanJobQueue"/> and runs each scan in its own DI scope so a long
/// SMB scan never blocks the HTTP request thread or the request-scoped DbContext.
/// </summary>
public sealed class ScanBackgroundService : BackgroundService
{
    private readonly ScanJobQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ScanBackgroundService> _log;

    public ScanBackgroundService(ScanJobQueue queue, IServiceScopeFactory scopes, ILogger<ScanBackgroundService> log)
    {
        _queue = queue;
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var libraryId in _queue.DequeueAllAsync(stoppingToken))
        {
            _queue.MarkRunning(libraryId);
            try
            {
                using var scope = _scopes.CreateScope();
                var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
                var r = await scanner.ScanAsync(libraryId, stoppingToken);
                _log.LogInformation(
                    "Background scan of library {Id} complete: {Added} added, {Updated} updated, {Series} series.",
                    libraryId, r.ChaptersAdded, r.ChaptersUpdated, r.SeriesCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Background scan of library {Id} failed.", libraryId);
            }
            finally
            {
                _queue.MarkDone(libraryId);
            }
        }
    }
}
