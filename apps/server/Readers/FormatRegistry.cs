using Mangrove.Server.Scanning;

namespace Mangrove.Server.Readers;

/// <summary>The reader family a chapter belongs to (spec §4 central FormatRegistry).</summary>
public enum MediaKind
{
    Unknown = 0,
    ComicArchive = 1, // cbz/zip/cbr/rar/cb7/7z/cbt/tar/tar.gz
    ImageFolder = 2,  // a folder of raw images
    Pdf = 3,
    Epub = 4,
}

/// <summary>
/// Maps extension/signature → media kind. The scanner and reader endpoints consult this so the
/// rest of the app never branches on raw file extensions.
/// </summary>
public static class FormatRegistry
{
    public const string ImageFolderFormat = "images";

    public static MediaKind Classify(string fileName)
    {
        var ext = SupportedFormats.NormalizedExtension(fileName);
        return ext switch
        {
            ".epub" => MediaKind.Epub,
            ".pdf" => MediaKind.Pdf,
            _ when SupportedFormats.IsArchive(fileName) => MediaKind.ComicArchive,
            _ => MediaKind.Unknown,
        };
    }

    /// <summary>Resolves the media kind from a stored chapter format string.</summary>
    public static MediaKind FromFormat(string format) => format.ToLowerInvariant() switch
    {
        ImageFolderFormat => MediaKind.ImageFolder,
        "epub" => MediaKind.Epub,
        "pdf" => MediaKind.Pdf,
        "cbz" or "zip" or "cbr" or "rar" or "cb7" or "7z" or "cbt" or "tar" or "tar.gz"
            => MediaKind.ComicArchive,
        _ => MediaKind.Unknown,
    };

    public static bool IsImageBased(MediaKind kind) =>
        kind is MediaKind.ComicArchive or MediaKind.ImageFolder or MediaKind.Pdf;
}
