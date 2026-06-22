using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

public sealed record CblImportRequest(string? Name, string Xml);
public sealed record CblImportResult(int ListId, string Name, int Matched, int Unmatched);

[ApiController]
[Route("api/reading-lists")]
[Authorize]
public sealed class ReadingListsController : ControllerBase
{
    private readonly MangroveDbContext _db;
    public ReadingListsController(MangroveDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReadingListDto>>> List(CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var lists = await _db.ReadingLists
            .Where(r => r.OwnerId == userId || r.IsPublic)
            .OrderBy(r => r.Name)
            .Select(r => new ReadingListDto(r.Id, r.Name, r.IsPublic, r.OwnerId, r.Items.Count))
            .ToListAsync(ct);
        return Ok(lists);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReadingListDetailDto>> Get(int id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var list = await _db.ReadingLists.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (list is null) return NotFound();
        if (list.OwnerId != userId && !list.IsPublic && !User.IsAdmin()) return Forbid();

        var items = await _db.ReadingListItems
            .Where(i => i.ReadingListId == id)
            .OrderBy(i => i.Order)
            .Select(i => new ReadingListItemDto(
                i.Id, i.ChapterId, i.Order,
                i.Chapter.Volume.Series.Name, i.Chapter.Number, i.Chapter.Title,
                i.Chapter.PageCount, i.Chapter.CoverPath != null || i.Chapter.Volume.Series.CoverPath != null))
            .ToListAsync(ct);

        return Ok(new ReadingListDetailDto(list.Id, list.Name, list.IsPublic, list.OwnerId, items));
    }

    [HttpPost]
    public async Task<ActionResult<ReadingListDto>> Create(CreateReadingListRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Name is required." });
        var list = new ReadingList { OwnerId = User.GetUserId() ?? 0, Name = req.Name.Trim(), IsPublic = req.IsPublic };
        _db.ReadingLists.Add(list);
        await _db.SaveChangesAsync(ct);
        return Ok(new ReadingListDto(list.Id, list.Name, list.IsPublic, list.OwnerId, 0));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReadingListDto>> Update(int id, CreateReadingListRequest req, CancellationToken ct)
    {
        var list = await OwnedAsync(id, ct);
        if (list is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(req.Name)) list.Name = req.Name.Trim();
        list.IsPublic = req.IsPublic;
        await _db.SaveChangesAsync(ct);
        return Ok(new ReadingListDto(list.Id, list.Name, list.IsPublic, list.OwnerId, list.Items.Count));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var list = await OwnedAsync(id, ct);
        if (list is null) return NotFound();
        _db.ReadingLists.Remove(list);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/items/{chapterId:int}")]
    public async Task<IActionResult> AddItem(int id, int chapterId, CancellationToken ct)
    {
        var list = await OwnedAsync(id, ct);
        if (list is null) return NotFound();
        if (!await _db.Chapters.AnyAsync(c => c.Id == chapterId, ct)) return NotFound();
        var order = (await _db.ReadingListItems.Where(i => i.ReadingListId == id).MaxAsync(i => (int?)i.Order, ct) ?? 0) + 1;
        _db.ReadingListItems.Add(new ReadingListItem { ReadingListId = id, ChapterId = chapterId, Order = order });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> RemoveItem(int id, int itemId, CancellationToken ct)
    {
        var list = await OwnedAsync(id, ct);
        if (list is null) return NotFound();
        var item = await _db.ReadingListItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ReadingListId == id, ct);
        if (item is null) return NotFound();
        _db.ReadingListItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:int}/reorder")]
    public async Task<IActionResult> Reorder(int id, ReorderRequest req, CancellationToken ct)
    {
        var list = await OwnedAsync(id, ct);
        if (list is null) return NotFound();
        var items = await _db.ReadingListItems.Where(i => i.ReadingListId == id).ToListAsync(ct);
        var byId = items.ToDictionary(i => i.Id);
        var order = 1;
        foreach (var itemId in req.ItemIds)
            if (byId.TryGetValue(itemId, out var item)) item.Order = order++;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("import-cbl")]
    public async Task<ActionResult<CblImportResult>> ImportCbl(CblImportRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Xml)) return BadRequest(new { error = "Empty CBL." });

        XDocument doc;
        try { doc = XDocument.Parse(req.Xml); }
        catch (Exception ex) { return BadRequest(new { error = $"Invalid CBL XML: {ex.Message}" }); }

        var root = doc.Root;
        if (root is null || !root.Name.LocalName.Equals("ReadingList", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Not a ReadingList (CBL) document." });

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = LocalElement(root, "Name")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Imported list";

        var list = new ReadingList { OwnerId = User.GetUserId() ?? 0, Name = name!, IsPublic = false };
        _db.ReadingLists.Add(list);
        await _db.SaveChangesAsync(ct);

        var books = root.Descendants().Where(e => e.Name.LocalName.Equals("Book", StringComparison.OrdinalIgnoreCase));
        int matched = 0, unmatched = 0, order = 1;
        foreach (var book in books)
        {
            var series = (string?)book.Attribute("Series");
            var numberStr = (string?)book.Attribute("Number");
            var volumeStr = (string?)book.Attribute("Volume");
            if (string.IsNullOrWhiteSpace(series)) { unmatched++; continue; }

            float? number = float.TryParse(numberStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
            float? volume = float.TryParse(volumeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

            var q = _db.Chapters.Where(c => c.Volume.Series.Name == series);
            if (number is { } num) q = q.Where(c => c.Number == num);
            if (volume is { } vol) q = q.Where(c => c.Volume.Number == vol);

            var chapterId = await q.OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
            if (chapterId is null) { unmatched++; continue; }

            _db.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, ChapterId = chapterId.Value, Order = order++ });
            matched++;
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new CblImportResult(list.Id, list.Name, matched, unmatched));
    }

    [HttpGet("{id:int}/export-cbl")]
    public async Task<IActionResult> ExportCbl(int id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var list = await _db.ReadingLists.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (list is null) return NotFound();
        if (list.OwnerId != userId && !list.IsPublic && !User.IsAdmin()) return Forbid();

        var items = await _db.ReadingListItems
            .Where(i => i.ReadingListId == id)
            .OrderBy(i => i.Order)
            .Select(i => new { Series = i.Chapter.Volume.Series.Name, Chapter = i.Chapter.Number, Volume = i.Chapter.Volume.Number })
            .ToListAsync(ct);

        var books = new XElement("Books");
        foreach (var it in items)
        {
            books.Add(new XElement("Book",
                new XAttribute("Series", it.Series),
                new XAttribute("Number", it.Chapter.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Volume", it.Volume == 0 ? "" : it.Volume.ToString(CultureInfo.InvariantCulture))));
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ReadingList", new XElement("Name", list.Name), books));

        var bytes = Encoding.UTF8.GetBytes(doc.Declaration + Environment.NewLine + doc);
        var fileName = $"{Sanitize(list.Name)}.cbl";
        return File(bytes, "application/xml", fileName);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "reading-list" : name;
    }

    private static XElement? LocalElement(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private async Task<ReadingList?> OwnedAsync(int id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        var list = await _db.ReadingLists.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (list is null) return null;
        return list.OwnerId == userId || User.IsAdmin() ? list : null;
    }
}
