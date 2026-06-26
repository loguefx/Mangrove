using System.Text.Json;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Mangrove.Server.Metadata;
using Mangrove.Server.Readers;
using Mangrove.Server.Scanning;
using Mangrove.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

public sealed record ScanAllResult(int Libraries, string Status);
public sealed record RepairCoversResult(string Status);

[ApiController]
[Route("api/tasks")]
[Authorize(Roles = "Admin")]
public sealed class TasksController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly ScanJobQueue _scanQueue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        MangroveDbContext db,
        ScanJobQueue scanQueue,
        IServiceScopeFactory scopes,
        ILogger<TasksController> logger)
    {
        _db = db;
        _scanQueue = scanQueue;
        _scopes = scopes;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskLogDto>>> List([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var logs = await _db.JobLogs
            .OrderByDescending(j => j.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(j => new TaskLogDto(j.Id, j.Kind, j.Target, j.Status, j.Message, j.StartedAt, j.FinishedAt))
            .ToListAsync(ct);
        return Ok(logs);
    }

    [HttpPost("scan-all")]
    public async Task<ActionResult<ScanAllResult>> ScanAll(CancellationToken ct)
    {
        var ids = await _db.Libraries.Select(l => l.Id).ToListAsync(ct);
        foreach (var id in ids)
            _scanQueue.Enqueue(id);
        return Accepted(new ScanAllResult(ids.Count, "queued"));
    }

    /// <summary>
    /// Admin-only: find series whose cached cover is missing or a solid-black image (the old
    /// alpha-on-JPEG bug) and re-pull a fresh cover from the online provider. Runs in the background
    /// and reports to the Tasks log; covers refresh automatically on the dashboard once done.
    /// </summary>
    [HttpPost("repair-covers")]
    public ActionResult<RepairCoversResult> RepairCovers()
    {
        _ = Task.Run(() => RunRepairAsync(CancellationToken.None));
        return Accepted(new RepairCoversResult("started"));
    }

    private async Task RunRepairAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangroveDbContext>();
        var online = scope.ServiceProvider.GetRequiredService<AniListMetadataService>();
        var sidecar = scope.ServiceProvider.GetRequiredService<LibrarySidecarWriter>();
        var paths = scope.ServiceProvider.GetRequiredService<ServerPaths>();

        var job = new JobLog { Kind = "repair-covers", Target = "all", Status = "running", StartedAt = DateTime.UtcNow };
        db.JobLogs.Add(job);
        await db.SaveChangesAsync(ct);

        int broken = 0, repaired = 0, checkedCount = 0;
        try
        {
            var series = await db.Series.ToListAsync(ct);
            foreach (var s in series)
            {
                ct.ThrowIfCancellationRequested();
                checkedCount++;

                byte[]? current = null;
                if (!string.IsNullOrEmpty(s.CoverPath) && System.IO.File.Exists(s.CoverPath))
                {
                    try { current = await System.IO.File.ReadAllBytesAsync(s.CoverPath, ct); } catch { current = null; }
                }
                if (!ImageHelper.IsLikelyBlank(current)) continue; // cover is fine

                broken++;

                var anilistId = ParseAnilistId(s.ExternalIds);
                OnlineSeriesMetadata? meta;
                try
                {
                    meta = anilistId is > 0
                        ? await online.FetchByIdAsync(anilistId.Value, ct)
                        : await online.FetchAsync(s.Name, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cover repair lookup failed for '{Series}'", s.Name);
                    continue;
                }

                if (meta is null || string.IsNullOrWhiteSpace(meta.CoverUrl)) continue;

                var raw = await online.DownloadImageAsync(meta.CoverUrl!, ct);
                if (raw is null) continue;

                var resized = ImageHelper.ResizeCover(raw);
                if (ImageHelper.IsLikelyBlank(resized)) continue; // still bad: don't overwrite

                try
                {
                    var coverPath = paths.CoverFileForSeries(s.Id);
                    await System.IO.File.WriteAllBytesAsync(coverPath, resized, ct);
                    s.CoverPath = coverPath;
                    s.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    await sidecar.WriteCoverAsync(s, resized, ct);
                    repaired++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write repaired cover for '{Series}'", s.Name);
                }
            }

            job.Status = "ok";
            job.Message = $"Repaired {repaired} of {broken} broken cover(s) across {checkedCount} series.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cover repair task failed");
            job.Status = "error";
            job.Message = $"Failed after repairing {repaired} of {broken}: {ex.Message}";
        }
        finally
        {
            job.FinishedAt = DateTime.UtcNow;
            try { await db.SaveChangesAsync(CancellationToken.None); } catch { /* best effort */ }
            _logger.LogInformation("Cover repair finished: {Message}", job.Message);
        }
    }

    private static int? ParseAnilistId(string? externalIds)
    {
        if (string.IsNullOrWhiteSpace(externalIds)) return null;
        try
        {
            using var doc = JsonDocument.Parse(externalIds);
            if (doc.RootElement.TryGetProperty("anilist", out var el) && el.TryGetInt32(out var id) && id > 0)
                return id;
        }
        catch { /* ignore malformed */ }
        return null;
    }
}
