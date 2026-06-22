using System.Text;
using VersOne.Epub;

namespace Mangrove.Server.Readers;

public sealed record EpubSpineItem(string Href, string? Label);
public sealed record EpubTocItem(string Label, string Href);

public sealed record EpubManifest(
    string Title,
    string? Author,
    string? Description,
    IReadOnlyList<EpubSpineItem> Spine,
    IReadOnlyList<EpubTocItem> Toc);

public sealed record EpubMetadata(
    string? Title, string? Author, string? Description, string? Publisher, string? Language);

/// <summary>
/// EPUB2/3 parsing via VersOne.Epub (spec §2/§4). Provides the reading order (spine), table of
/// contents, metadata, and per-resource streaming for the web EPUB reader.
/// </summary>
public sealed class EpubService
{
    public async Task<EpubManifest> ReadManifestAsync(Stream stream, CancellationToken ct = default)
    {
        var book = await EpubReader.ReadBookAsync(await BufferAsync(stream, ct));

        var spine = book.ReadingOrder
            .Select(f => new EpubSpineItem(NormalizeHref(f.FilePath), null))
            .ToList();

        var toc = new List<EpubTocItem>();
        if (book.Navigation is not null)
            FlattenNavigation(book.Navigation, toc);

        return new EpubManifest(
            book.Title ?? "Untitled",
            book.Author,
            book.Description,
            spine,
            toc);
    }

    public async Task<EpubMetadata> ReadMetadataAsync(Stream stream, CancellationToken ct = default)
    {
        var book = await EpubReader.ReadBookAsync(await BufferAsync(stream, ct));
        var meta = book.Schema.Package.Metadata;
        return new EpubMetadata(
            book.Title,
            book.Author,
            book.Description,
            meta.Publishers?.FirstOrDefault()?.Publisher,
            meta.Languages?.FirstOrDefault()?.Language);
    }

    public async Task<(byte[] Bytes, string ContentType)?> ReadResourceAsync(
        Stream stream, string href, CancellationToken ct = default)
    {
        var book = await EpubReader.ReadBookAsync(await BufferAsync(stream, ct));
        var target = NormalizeHref(href);

        foreach (var file in book.Content.AllFiles.Local)
        {
            if (!string.Equals(NormalizeHref(file.FilePath), target, StringComparison.OrdinalIgnoreCase))
                continue;
            return (ReadBytes(file), MapContentType(file.ContentMimeType, file.FilePath));
        }
        return null;
    }

    public async Task<byte[]?> ReadCoverAsync(Stream stream, CancellationToken ct = default)
    {
        var book = await EpubReader.ReadBookAsync(await BufferAsync(stream, ct));
        if (book.CoverImage is { Length: > 0 }) return book.CoverImage;

        // Fall back to the first image in reading order.
        var firstImage = book.Content.Images.Local.FirstOrDefault();
        return firstImage is null ? null : ReadBytes(firstImage);
    }

    private static byte[] ReadBytes(VersOne.Epub.EpubLocalContentFile file) => file switch
    {
        VersOne.Epub.EpubLocalByteContentFile bytes => bytes.Content,
        VersOne.Epub.EpubLocalTextContentFile text => Encoding.UTF8.GetBytes(text.Content),
        _ => Array.Empty<byte>(),
    };

    private static void FlattenNavigation(
        IEnumerable<EpubNavigationItem> items, List<EpubTocItem> output)
    {
        foreach (var item in items)
        {
            if (item.Link?.ContentFilePath is { } path)
            {
                var href = NormalizeHref(path);
                if (!string.IsNullOrEmpty(item.Link.Anchor)) href += "#" + item.Link.Anchor;
                output.Add(new EpubTocItem(item.Title, href));
            }
            if (item.NestedItems is { Count: > 0 })
                FlattenNavigation(item.NestedItems, output);
        }
    }

    private static string NormalizeHref(string href)
    {
        href = href.Replace('\\', '/').TrimStart('.', '/');
        return href;
    }

    private static string MapContentType(string? mime, string filePath)
    {
        if (!string.IsNullOrEmpty(mime)) return mime;
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".xhtml" or ".html" or ".htm" => "application/xhtml+xml",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
    }

    private static async Task<Stream> BufferAsync(Stream stream, CancellationToken ct)
    {
        if (stream is MemoryStream ms) { ms.Position = 0; return ms; }
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return buffer;
    }
}
