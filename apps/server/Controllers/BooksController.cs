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
[Route("api/books")]
[Authorize]
public sealed class BooksController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly StorageProviderFactory _providers;
    private readonly EpubService _epub;
    private readonly AccessService _access;

    public BooksController(MangroveDbContext db, StorageProviderFactory providers, EpubService epub, AccessService access)
    {
        _db = db;
        _providers = providers;
        _epub = epub;
        _access = access;
    }

    [HttpGet("{chapterId:int}/manifest")]
    public async Task<ActionResult<EpubManifestDto>> Manifest(int chapterId, CancellationToken ct)
    {
        var (chapter, file, provider) = await ResolveAsync(chapterId, ct);
        if (chapter is null || file is null) return NotFound();

        await using var stream = await provider!.OpenReadAsync(file.StoragePath, ct);
        var manifest = await _epub.ReadManifestAsync(stream, ct);

        return Ok(new EpubManifestDto(
            chapterId,
            manifest.Title,
            manifest.Author,
            manifest.Spine.Select(s => new EpubSpineItemDto(s.Href, s.Label)).ToList(),
            manifest.Toc.Select(t => new EpubTocItemDto(t.Label, t.Href)).ToList()));
    }

    [HttpGet("{chapterId:int}/content/{*href}")]
    public async Task<IActionResult> Content(int chapterId, string href, CancellationToken ct)
    {
        var (chapter, file, provider) = await ResolveAsync(chapterId, ct);
        if (chapter is null || file is null) return NotFound();

        // Strip any fragment and decode.
        href = Uri.UnescapeDataString(href);
        var hashIndex = href.IndexOf('#');
        if (hashIndex >= 0) href = href[..hashIndex];

        await using var stream = await provider!.OpenReadAsync(file.StoragePath, ct);
        var resource = await _epub.ReadResourceAsync(stream, href, ct);
        if (resource is null) return NotFound();

        Response.Headers.CacheControl = "private, max-age=3600";
        return File(resource.Value.Bytes, resource.Value.ContentType);
    }

    private async Task<(Chapter? chapter, MangaFile? file, IStorageProvider? provider)> ResolveAsync(
        int chapterId, CancellationToken ct)
    {
        if (!await _access.CanAccessChapterAsync(User.GetUserId() ?? 0, User.IsAdmin(), chapterId, ct))
            return (null, null, null);

        var chapter = await _db.Chapters
            .Include(c => c.Files)
            .Include(c => c.Volume).ThenInclude(v => v.Series).ThenInclude(s => s.Library).ThenInclude(l => l.Credential)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct);

        var file = chapter?.Files.FirstOrDefault();
        if (chapter is null || file is null) return (null, null, null);

        var library = chapter.Volume.Series.Library;
        var provider = _providers.ForLibrary(library, library.Credential);
        return (chapter, file, provider);
    }
}
