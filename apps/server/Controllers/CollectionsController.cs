using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/collections")]
[Authorize]
public sealed class CollectionsController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly AccessService _access;

    public CollectionsController(MangroveDbContext db, AccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CollectionDto>>> List(CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var list = await _db.Collections
            .Where(c => c.OwnerId == userId || c.IsPublic)
            .OrderBy(c => c.Name)
            .Select(c => new CollectionDto(c.Id, c.Name, c.IsPublic, c.OwnerId, c.Items.Count))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CollectionDetailDto>> Get(int id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var isAdmin = User.IsAdmin();
        var col = await _db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (col is null) return NotFound();
        if (col.OwnerId != userId && !col.IsPublic) return Forbid();

        var libIds = await _access.AccessibleLibraryIdsAsync(userId, isAdmin, ct);
        var restriction = await _access.GetRestrictionAsync(userId, isAdmin, ct);

        var seriesQuery = _db.CollectionItems
            .Where(i => i.CollectionId == id)
            .OrderBy(i => i.Order)
            .Select(i => i.Series);

        var series = await _access.FilterSeries(seriesQuery, libIds, restriction)
            .Select(s => new SeriesDto(s.Id, s.LibraryId, s.Name, s.Summary, s.CoverPath != null,
                s.Volumes.Count, s.Volumes.SelectMany(v => v.Chapters).Count()))
            .ToListAsync(ct);

        return Ok(new CollectionDetailDto(col.Id, col.Name, col.IsPublic, col.OwnerId, series));
    }

    [HttpPost]
    public async Task<ActionResult<CollectionDto>> Create(CreateCollectionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Name is required." });
        var col = new Collection { OwnerId = User.GetUserId() ?? 0, Name = req.Name.Trim(), IsPublic = req.IsPublic };
        _db.Collections.Add(col);
        await _db.SaveChangesAsync(ct);
        return Ok(new CollectionDto(col.Id, col.Name, col.IsPublic, col.OwnerId, 0));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CollectionDto>> Update(int id, CreateCollectionRequest req, CancellationToken ct)
    {
        var col = await OwnedAsync(id, ct);
        if (col is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(req.Name)) col.Name = req.Name.Trim();
        col.IsPublic = req.IsPublic;
        await _db.SaveChangesAsync(ct);
        return Ok(new CollectionDto(col.Id, col.Name, col.IsPublic, col.OwnerId, col.Items.Count));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var col = await OwnedAsync(id, ct);
        if (col is null) return NotFound();
        _db.Collections.Remove(col);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/items/{seriesId:int}")]
    public async Task<IActionResult> AddItem(int id, int seriesId, CancellationToken ct)
    {
        var col = await OwnedAsync(id, ct);
        if (col is null) return NotFound();
        if (!await _db.Series.AnyAsync(s => s.Id == seriesId, ct)) return NotFound();
        if (await _db.CollectionItems.AnyAsync(i => i.CollectionId == id && i.SeriesId == seriesId, ct))
            return NoContent();

        var order = (await _db.CollectionItems.Where(i => i.CollectionId == id).MaxAsync(i => (int?)i.Order, ct) ?? 0) + 1;
        _db.CollectionItems.Add(new CollectionItem { CollectionId = id, SeriesId = seriesId, Order = order });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/items/{seriesId:int}")]
    public async Task<IActionResult> RemoveItem(int id, int seriesId, CancellationToken ct)
    {
        var col = await OwnedAsync(id, ct);
        if (col is null) return NotFound();
        var item = await _db.CollectionItems.FirstOrDefaultAsync(i => i.CollectionId == id && i.SeriesId == seriesId, ct);
        if (item is null) return NotFound();
        _db.CollectionItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Collection?> OwnedAsync(int id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var col = await _db.Collections.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (col is null) return null;
        return col.OwnerId == userId || User.IsAdmin() ? col : null;
    }
}
