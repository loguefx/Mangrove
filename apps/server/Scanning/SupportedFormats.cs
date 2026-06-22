namespace Mangrove.Server.Scanning;

public static class SupportedFormats
{
    /// <summary>Archive comic formats from spec §4. Phase 1 reads pages from CBZ/ZIP; the others
    /// are recognized and recorded now and gain page readers in Phase 2.</summary>
    public static readonly HashSet<string> Archive = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".zip", ".cbr", ".rar", ".cb7", ".7z", ".cbt", ".tar", ".tar.gz",
    };

    /// <summary>Comic archive formats whose pages we can decode (SharpCompress: zip/rar/7z/tar).</summary>
    public static readonly HashSet<string> Readable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".zip", ".cbr", ".rar", ".cb7", ".7z", ".cbt", ".tar", ".tar.gz",
    };

    /// <summary>Book formats (spec §4).</summary>
    public static readonly HashSet<string> Book = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epub", ".pdf",
    };

    /// <summary>Any file the scanner should treat as a readable chapter.</summary>
    public static bool IsSupportedFile(string fileName) => IsArchive(fileName) || IsBook(fileName);

    public static bool IsArchive(string fileName)
    {
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return true;
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && Archive.Contains(ext);
    }

    public static bool IsBook(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && Book.Contains(ext);
    }

    public static bool IsReadable(string fileName)
    {
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return true;
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && Readable.Contains(ext);
    }

    public static string NormalizedExtension(string fileName)
    {
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return ".tar.gz";
        return Path.GetExtension(fileName).ToLowerInvariant();
    }
}
