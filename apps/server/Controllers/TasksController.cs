using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Mangrove.Server.Scanning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

public sealed record ScanAllResult(int Libraries, string Status);

[ApiController]
[Route("api/tasks")]
[Authorize(Roles = "Admin")]
public sealed class TasksController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly ScanJobQueue _scanQueue;

    public TasksController(MangroveDbContext db, ScanJobQueue scanQueue)
    {
        _db = db;
        _scanQueue = scanQueue;
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
}
