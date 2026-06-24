using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Mangrove.Server.Readers;
using Mangrove.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/chapters")]
[Authorize]
public sealed class ChaptersController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly StorageProviderFactory _providers;
    private readonly ReaderService _readers;
    private readonly AccessService _access;

    public ChaptersController(MangroveDbContext db, StorageProviderFactory providers, ReaderService readers, AccessService access)
    {
        _db = db;
        _providers = providers;
        _readers = readers;
        _access = access;
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ChapterDto>> Get(int id, CancellationToken ct)
    {
        var c = await _db.Chapters.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        return Ok(new ChapterDto(c.Id, c.Number, c.Title, c.PageCount, c.FileFormat, c.CoverPath != null));
    }

    [HttpGet("{id:int}/manifest")]
    public async Task<ActionResult<ChapterManifestDto>> Manifest(int id, CancellationToken ct)
    {
        var chapter = await LoadChapterAsync(id, ct);
        if (chapter is null) return NotFound();

        var direction = chapter.Volume.Series.Library.Type == LibraryType.Manga ? "rtl" : "ltr";
        var kind = FormatRegistry.FromFormat(chapter.FileFormat);
        var mediaType = kind == MediaKind.Epub ? "epub" : "image";

        // Sibling chapters in reading order, so the reader can preload the next chapter and offer
        // next/previous navigation. Ordered by volume then chapter number (Id as a stable tiebreak).
        var ordered = await _db.Chapters
            .Where(c => c.Volume.SeriesId == chapter.Volume.SeriesId)
            .OrderBy(c => c.Volume.Number).ThenBy(c => c.Number).ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var idx = ordered.IndexOf(id);
        int? prevId = idx > 0 ? ordered[idx - 1] : null;
        int? nextId = idx >= 0 && idx < ordered.Count - 1 ? ordered[idx + 1] : null;

        return Ok(new ChapterManifestDto(
            chapter.Id, chapter.PageCount, direction, chapter.FileFormat, mediaType, prevId, nextId,
            BuildChapterLabel(chapter), chapter.Volume.Series.Name));
    }

    /// <summary>A short human label for the reader overlay, e.g. "Vol. 1 Ch. 5" or a special's title.</summary>
    private static string BuildChapterLabel(Chapter c)
    {
        var parts = new List<string>();
        if (c.Volume.Number > 0) parts.Add($"Vol. {Fmt(c.Volume.Number)}");
        if (c.Number > 0) parts.Add($"Ch. {Fmt(c.Number)}");
        var label = string.Join(" ", parts);
        if (string.IsNullOrEmpty(label))
            return string.IsNullOrWhiteSpace(c.Title) ? "Chapter" : c.Title!;
        if (!string.IsNullOrWhiteSpace(c.Title)) label += $" · {c.Title}";
        return label;
    }

    private static string Fmt(float n) =>
        n.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    [HttpGet("{id:int}/cover")]
    [AllowAnonymous]
    public async Task<IActionResult> Cover(int id, CancellationToken ct)
    {
        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chapter?.CoverPath is null || !System.IO.File.Exists(chapter.CoverPath))
            return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(chapter.CoverPath, "image/jpeg");
    }

    [HttpGet("{id:int}/pages/{n:int}")]
    public async Task<IActionResult> Page(int id, int n, CancellationToken ct)
    {
        if (!await _access.CanAccessChapterAsync(User.GetUserId() ?? 0, User.IsAdmin(), id, ct))
            return NotFound();

        var chapter = await LoadChapterAsync(id, ct);
        var file = chapter?.Files.FirstOrDefault();
        if (chapter is null || file is null) return NotFound();

        var library = chapter.Volume.Series.Library;
        var provider = _providers.ForLibrary(library, library.Credential);

        var page = await _readers.GetPageAsync(file.Format, file.StoragePath, provider, n, ct);
        if (page is null) return NotFound();

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(page.Value.Bytes, page.Value.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? 0;
        if (!await _access.CanAccessChapterAsync(userId, User.IsAdmin(), id, ct)) return NotFound();

        var canDownload = User.IsAdmin() ||
            await _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.Role.CanDownload, ct);
        if (!canDownload) return Forbid();

        var chapter = await LoadChapterAsync(id, ct);
        var file = chapter?.Files.FirstOrDefault();
        if (chapter is null || file is null) return NotFound();
        if (file.Format == Readers.FormatRegistry.ImageFolderFormat)
            return BadRequest(new { error = "Image-folder chapters can't be downloaded as a single file." });

        var library = chapter.Volume.Series.Library;
        var provider = _providers.ForLibrary(library, library.Credential);
        var stream = await provider.OpenReadAsync(file.StoragePath, ct);
        var name = System.IO.Path.GetFileName(file.StoragePath.Replace('\\', '/'));
        return File(stream, "application/octet-stream", name, enableRangeProcessing: true);
    }

    private Task<Chapter?> LoadChapterAsync(int id, CancellationToken ct) =>
        _db.Chapters
            .Include(c => c.Files)
            .Include(c => c.Volume).ThenInclude(v => v.Series).ThenInclude(s => s.Library).ThenInclude(l => l.Credential)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
}
