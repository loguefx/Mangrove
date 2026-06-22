using System.Text;
using System.Xml.Linq;
using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Readers;
using Mangrove.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

/// <summary>
/// Hand-rolled OPDS 1.2 catalog (spec §5, §9) so third-party readers (KOReader, Panels, Mihon)
/// can browse and download. Authenticated with HTTP Basic, separate from the web/app JWT flow.
/// </summary>
[ApiController]
[Route("api/opds")]
[AllowAnonymous]
public sealed class OpdsController : ControllerBase
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Opds = "http://opds-spec.org/2010/catalog";
    private const string NavType = "application/atom+xml;profile=opds-catalog;kind=navigation";
    private const string AcqType = "application/atom+xml;profile=opds-catalog;kind=acquisition";

    private readonly MangroveDbContext _db;
    private readonly AuthService _auth;
    private readonly AccessService _access;
    private readonly StorageProviderFactory _providers;

    public OpdsController(MangroveDbContext db, AuthService auth, AccessService access, StorageProviderFactory providers)
    {
        _db = db;
        _auth = auth;
        _access = access;
        _providers = providers;
    }

    [HttpGet("")]
    [HttpGet("catalog")]
    public async Task<IActionResult> Root(CancellationToken ct)
    {
        var user = await AuthenticateAsync(ct);
        if (user is null) return Challenge401();

        var feed = NewFeed("mangrove:opds:root", "Mangrove", Self("api/opds"));
        feed.Add(Entry("mangrove:opds:libraries", "Libraries", "Browse all libraries you can access",
            NavLink(Self("api/opds/libraries"))));
        feed.Add(Entry("mangrove:opds:want", "Want to read", "Series on your want-to-read list",
            NavLink(Self("api/opds/want-to-read"))));
        return Feed(feed, NavType);
    }

    [HttpGet("libraries")]
    public async Task<IActionResult> Libraries(CancellationToken ct)
    {
        var user = await AuthenticateAsync(ct);
        if (user is null) return Challenge401();

        var isAdmin = IsAdmin(user);
        var libIds = await _access.AccessibleLibraryIdsAsync(user.Id, isAdmin, ct);
        var libs = await _db.Libraries.Where(l => libIds.Contains(l.Id)).OrderBy(l => l.Name).ToListAsync(ct);

        var feed = NewFeed("mangrove:opds:libraries", "Libraries", Self("api/opds/libraries"));
        foreach (var lib in libs)
            feed.Add(Entry($"mangrove:opds:library:{lib.Id}", lib.Name, $"{lib.Type} library",
                NavLink(Self($"api/opds/libraries/{lib.Id}"))));
        return Feed(feed, NavType);
    }

    [HttpGet("libraries/{id:int}")]
    public async Task<IActionResult> Library(int id, CancellationToken ct)
    {
        var user = await AuthenticateAsync(ct);
        if (user is null) return Challenge401();

        var isAdmin = IsAdmin(user);
        var libIds = await _access.AccessibleLibraryIdsAsync(user.Id, isAdmin, ct);
        if (!libIds.Contains(id)) return Challenge401();
        var restriction = await _access.GetRestrictionAsync(user.Id, isAdmin, ct);

        var series = await _access.FilterSeries(_db.Series.Where(s => s.LibraryId == id), libIds, restriction)
            .OrderBy(s => s.SortName).ToListAsync(ct);

        var feed = NewFeed($"mangrove:opds:library:{id}", "Library", Self($"api/opds/libraries/{id}"));
        foreach (var s in series)
            feed.Add(SeriesEntry(s));
        return Feed(feed, NavType);
    }

    [HttpGet("want-to-read")]
    public async Task<IActionResult> WantToRead(CancellationToken ct)
    {
        var user = await AuthenticateAsync(ct);
        if (user is null) return Challenge401();

        var isAdmin = IsAdmin(user);
        var libIds = await _access.AccessibleLibraryIdsAsync(user.Id, isAdmin, ct);
        var restriction = await _access.GetRestrictionAsync(user.Id, isAdmin, ct);
        var q = _db.WantToRead.Where(w => w.UserId == user.Id).Select(w => w.Series);
        var series = await _access.FilterSeries(q, libIds, restriction).OrderBy(s => s.SortName).ToListAsync(ct);

        var feed = NewFeed("mangrove:opds:want", "Want to read", Self("api/opds/want-to-read"));
        foreach (var s in series) feed.Add(SeriesEntry(s));
        return Feed(feed, NavType);
    }

    [HttpGet("series/{id:int}")]
    public async Task<IActionResult> Series(int id, CancellationToken ct)
    {
        var user = await AuthenticateAsync(ct);
        if (user is null) return Challenge401();
        if (!await _access.CanAccessSeriesAsync(user.Id, IsAdmin(user), id, ct)) return Challenge401();

        var series = await _db.Series
            .Include(s => s.Volumes).ThenInclude(v => v.Chapters)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series is null) return NotFound();

        var canDownload = IsAdmin(user) ||
            await _db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.Role.CanDownload, ct);

        var feed = NewFeed($"mangrove:opds:series:{id}", series.Name, Self($"api/opds/series/{id}"));
        foreach (var v in series.Volumes.OrderBy(v => v.Number))
        foreach (var c in v.Chapters.OrderBy(c => c.Number))
        {
            var title = c.Title ?? (v.Number > 0 ? $"Vol {v.Number} Ch {c.Number}" : $"Chapter {c.Number}");
            var entry = new XElement(Atom + "entry",
                new XElement(Atom + "title", title),
                new XElement(Atom + "id", $"mangrove:opds:chapter:{c.Id}"),
                new XElement(Atom + "updated", c.CreatedAt.ToString("o")),
                CoverLinks(c.Id));
            if (canDownload && c.FileFormat != FormatRegistry.ImageFolderFormat)
            {
                entry.Add(new XElement(Atom + "link",
                    new XAttribute("rel", "http://opds-spec.org/acquisition"),
                    new XAttribute("href", AbsoluteUrl($"api/opds/chapters/{c.Id}/file")),
                    new XAttribute("type", MimeForFormat(c.FileFormat))));
            }
            feed.Add(entry);
        }
        return Feed(feed, AcqType);
    }

    [HttpGet("chapters/{id:int}/file")]
    public async Task<IActionResult> File(int id, CancellationToken ct)
    {
        var user = await AuthenticateAsync(ct);
        if (user is null) return Challenge401();
        if (!await _access.CanAccessChapterAsync(user.Id, IsAdmin(user), id, ct)) return Challenge401();

        var canDownload = IsAdmin(user) ||
            await _db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.Role.CanDownload, ct);
        if (!canDownload) return StatusCode(StatusCodes.Status403Forbidden);

        var chapter = await _db.Chapters
            .Include(c => c.Files)
            .Include(c => c.Volume).ThenInclude(v => v.Series).ThenInclude(s => s.Library).ThenInclude(l => l.Credential)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        var file = chapter?.Files.FirstOrDefault();
        if (chapter is null || file is null || file.Format == FormatRegistry.ImageFolderFormat) return NotFound();

        var library = chapter.Volume.Series.Library;
        var provider = _providers.ForLibrary(library, library.Credential);
        var stream = await provider.OpenReadAsync(file.StoragePath, ct);
        var name = System.IO.Path.GetFileName(file.StoragePath.Replace('\\', '/'));
        return File(stream, MimeForFormat(file.Format), name, enableRangeProcessing: true);
    }

    [HttpGet("chapters/{id:int}/cover")]
    public async Task<IActionResult> Cover(int id, CancellationToken ct)
    {
        var user = await AuthenticateAsync(ct);
        if (user is null) return Challenge401();
        if (!await _access.CanAccessChapterAsync(user.Id, IsAdmin(user), id, ct)) return Challenge401();

        var chapter = await _db.Chapters.Include(c => c.Volume).ThenInclude(v => v.Series)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        var path = chapter?.CoverPath ?? chapter?.Volume.Series.CoverPath;
        if (path is null || !System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "image/jpeg");
    }

    // ---- helpers ----

    private XElement SeriesEntry(Data.Series s)
    {
        var entry = new XElement(Atom + "entry",
            new XElement(Atom + "title", s.Name),
            new XElement(Atom + "id", $"mangrove:opds:series:{s.Id}"),
            new XElement(Atom + "updated", s.UpdatedAt.ToString("o")),
            NavLink(Self($"api/opds/series/{s.Id}")));
        if (!string.IsNullOrWhiteSpace(s.Summary))
            entry.Add(new XElement(Atom + "content", new XAttribute("type", "text"), s.Summary));
        if (s.CoverPath != null)
        {
            entry.Add(new XElement(Atom + "link",
                new XAttribute("rel", "http://opds-spec.org/image"),
                new XAttribute("href", AbsoluteUrl($"api/series/{s.Id}/cover")),
                new XAttribute("type", "image/jpeg")));
            entry.Add(new XElement(Atom + "link",
                new XAttribute("rel", "http://opds-spec.org/image/thumbnail"),
                new XAttribute("href", AbsoluteUrl($"api/series/{s.Id}/cover")),
                new XAttribute("type", "image/jpeg")));
        }
        return entry;
    }

    private XElement CoverLinks(int chapterId) =>
        new(Atom + "link",
            new XAttribute("rel", "http://opds-spec.org/image/thumbnail"),
            new XAttribute("href", AbsoluteUrl($"api/opds/chapters/{chapterId}/cover")),
            new XAttribute("type", "image/jpeg"));

    private XElement NewFeed(string id, string title, XElement selfLink)
    {
        return new XElement(Atom + "feed",
            new XAttribute(XNamespace.Xmlns + "opds", Opds.NamespaceName),
            new XElement(Atom + "id", id),
            new XElement(Atom + "title", title),
            new XElement(Atom + "updated", DateTime.UtcNow.ToString("o")),
            new XElement(Atom + "link",
                new XAttribute("rel", "start"),
                new XAttribute("href", AbsoluteUrl("api/opds")),
                new XAttribute("type", NavType)),
            selfLink);
    }

    private static XElement Entry(string id, string title, string summary, XElement link) =>
        new(Atom + "entry",
            new XElement(Atom + "title", title),
            new XElement(Atom + "id", id),
            new XElement(Atom + "updated", DateTime.UtcNow.ToString("o")),
            new XElement(Atom + "content", new XAttribute("type", "text"), summary),
            link);

    private XElement Self(string path) =>
        new(Atom + "link",
            new XAttribute("rel", "self"),
            new XAttribute("href", AbsoluteUrl(path)),
            new XAttribute("type", NavType));

    private XElement NavLink(XElement self) =>
        new(Atom + "link",
            new XAttribute("href", ((string)self.Attribute("href")!)),
            new XAttribute("type", NavType));

    private ContentResult Feed(XElement feed, string contentType)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), feed);
        return new ContentResult
        {
            Content = doc.Declaration + Environment.NewLine + doc,
            ContentType = contentType,
            StatusCode = 200,
        };
    }

    private string AbsoluteUrl(string path) => $"{Request.Scheme}://{Request.Host}/{path.TrimStart('/')}";

    private static bool IsAdmin(User user) => user.UserRoles.Any(ur => ur.Role.Type == RoleType.Admin);

    private async Task<User?> AuthenticateAsync(CancellationToken ct)
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var sep = raw.IndexOf(':');
            if (sep < 0) return null;
            return await _auth.ValidateCredentialsAsync(raw[..sep], raw[(sep + 1)..], ct);
        }
        catch { return null; }
    }

    private IActionResult Challenge401()
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"Mangrove OPDS\", charset=\"UTF-8\"";
        return StatusCode(StatusCodes.Status401Unauthorized);
    }

    private static string MimeForFormat(string format) => format.ToLowerInvariant() switch
    {
        "cbz" or "zip" => "application/vnd.comicbook+zip",
        "cbr" or "rar" => "application/vnd.comicbook-rar",
        "epub" => "application/epub+zip",
        "pdf" => "application/pdf",
        "cb7" or "7z" => "application/x-7z-compressed",
        "cbt" or "tar" => "application/x-tar",
        _ => "application/octet-stream",
    };
}
