using Mangrove.Server.Dtos;
using Mangrove.Server.Updates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mangrove.Server.Controllers;

/// <summary>Admin-only endpoints for checking and applying server updates from GitHub Releases.</summary>
[ApiController]
[Route("api/admin/update")]
[Authorize(Roles = "Admin")]
public sealed class UpdateController : ControllerBase
{
    private readonly UpdateService _updates;
    public UpdateController(UpdateService updates) => _updates = updates;

    [HttpGet("status")]
    public async Task<ActionResult<UpdateStatusDto>> Status(CancellationToken ct) =>
        Ok(await _updates.GetStatusAsync(ct));

    [HttpPost("apply")]
    public async Task<ActionResult<UpdateApplyResultDto>> Apply(CancellationToken ct)
    {
        var result = await _updates.ApplyAsync(ct);
        return result.Started ? Ok(result) : BadRequest(result);
    }
}
