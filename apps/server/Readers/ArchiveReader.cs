using SharpCompress.Archives;

namespace Mangrove.Server.Readers;

public sealed record ArchivePage(string Key, string ContentType);

/// <summary>
/// Reads image pages out of a CBZ/ZIP archive (Phase 1). Built on SharpCompress; additional
/// formats (cbr/cb7/tar) arrive in Phase 2 — see spec §4. Operates on a seekable stream provided
/// by an <see cref="Storage.IStorageProvider"/>, so the archive can live on SMB or local disk.
/// </summary>
public sealed class ArchiveReader
{
    /// <summary>Returns the image page keys in natural (human) order.</summary>
    public IReadOnlyList<string> ListPages(Stream archiveStream)
    {
        using var archive = ArchiveFactory.OpenArchive(archiveStream);
        return archive.Entries
            .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key) && ImageFormats.IsImage(e.Key!))
            .Select(e => e.Key!)
            .OrderBy(k => k, NaturalComparer.Instance)
            .ToList();
    }

    public int CountPages(Stream archiveStream) => ListPages(archiveStream).Count;

    /// <summary>Reads a single page (0-based, natural order) into memory with its content type.</summary>
    public (byte[] Bytes, string ContentType)? ReadPage(Stream archiveStream, int index)
    {
        using var archive = ArchiveFactory.OpenArchive(archiveStream);
        var keys = archive.Entries
            .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key) && ImageFormats.IsImage(e.Key!))
            .Select(e => e.Key!)
            .OrderBy(k => k, NaturalComparer.Instance)
            .ToList();

        if (index < 0 || index >= keys.Count) return null;
        var key = keys[index];
        var entry = archive.Entries.First(e => e.Key == key);

        using var es = entry.OpenEntryStream();
        using var ms = new MemoryStream();
        es.CopyTo(ms);
        return (ms.ToArray(), ImageFormats.ContentType(key));
    }

    /// <summary>Reads the first page as the cover image.</summary>
    public (byte[] Bytes, string ContentType)? ReadCover(Stream archiveStream) => ReadPage(archiveStream, 0);
}
