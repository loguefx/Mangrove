using Mangrove.Server.Storage;

namespace Mangrove.Server.Readers;

/// <summary>
/// Single entry point the scanner and reader endpoints use to count pages, fetch a page image, or
/// extract a cover — regardless of whether the chapter is a comic archive, a raw image folder, a
/// PDF, or an EPUB. Keeps format dispatch in one place (spec §4).
/// </summary>
public sealed class ReaderService
{
    private readonly ArchiveReader _archive;
    private readonly ImageFolderReader _folder;
    private readonly PdfPageReader _pdf;
    private readonly EpubService _epub;

    public ReaderService(ArchiveReader archive, ImageFolderReader folder, PdfPageReader pdf, EpubService epub)
    {
        _archive = archive;
        _folder = folder;
        _pdf = pdf;
        _epub = epub;
    }

    public async Task<int> CountPagesAsync(
        string format, string storagePath, IStorageProvider provider, CancellationToken ct = default)
    {
        switch (FormatRegistry.FromFormat(format))
        {
            case MediaKind.ComicArchive:
            {
                await using var stream = await provider.OpenReadAsync(storagePath, ct);
                using var buffered = await BufferAsync(stream, ct);
                return _archive.CountPages(buffered);
            }
            case MediaKind.ImageFolder:
                return await _folder.CountPagesAsync(storagePath, provider, ct);
            case MediaKind.Pdf:
            {
                var bytes = await ReadAllAsync(storagePath, provider, ct);
                return _pdf.CountPages(bytes);
            }
            case MediaKind.Epub:
            {
                await using var stream = await provider.OpenReadAsync(storagePath, ct);
                var manifest = await _epub.ReadManifestAsync(stream, ct);
                return manifest.Spine.Count;
            }
            default:
                return 0;
        }
    }

    /// <summary>Returns a rendered/extracted page image. Returns null for EPUB (use book endpoints).</summary>
    public async Task<(byte[] Bytes, string ContentType)?> GetPageAsync(
        string format, string storagePath, IStorageProvider provider, int index, CancellationToken ct = default)
    {
        switch (FormatRegistry.FromFormat(format))
        {
            case MediaKind.ComicArchive:
            {
                await using var stream = await provider.OpenReadAsync(storagePath, ct);
                using var buffered = await BufferAsync(stream, ct);
                return _archive.ReadPage(buffered, index);
            }
            case MediaKind.ImageFolder:
                return await _folder.ReadPageAsync(storagePath, provider, index, ct);
            case MediaKind.Pdf:
            {
                var bytes = await ReadAllAsync(storagePath, provider, ct);
                return _pdf.RenderPage(bytes, index);
            }
            default:
                return null;
        }
    }

    /// <summary>Returns raw (unsized) cover bytes for caching/resizing during a scan.</summary>
    public async Task<byte[]?> GetRawCoverAsync(
        string format, string storagePath, IStorageProvider provider, CancellationToken ct = default)
    {
        switch (FormatRegistry.FromFormat(format))
        {
            case MediaKind.ComicArchive:
            {
                await using var stream = await provider.OpenReadAsync(storagePath, ct);
                using var buffered = await BufferAsync(stream, ct);
                return _archive.ReadCover(buffered)?.Bytes;
            }
            case MediaKind.ImageFolder:
                return (await _folder.ReadCoverAsync(storagePath, provider, ct))?.Bytes;
            case MediaKind.Pdf:
            {
                var bytes = await ReadAllAsync(storagePath, provider, ct);
                return _pdf.RenderCover(bytes);
            }
            case MediaKind.Epub:
            {
                await using var stream = await provider.OpenReadAsync(storagePath, ct);
                return await _epub.ReadCoverAsync(stream, ct);
            }
            default:
                return null;
        }
    }

    private static async Task<byte[]> ReadAllAsync(string path, IStorageProvider provider, CancellationToken ct)
    {
        await using var stream = await provider.OpenReadAsync(path, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
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
