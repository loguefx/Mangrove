using System.Globalization;
using System.Xml.Linq;
using SharpCompress.Archives;

namespace Mangrove.Server.Readers;

public sealed record ComicInfo(
    string? Series,
    float? Number,
    float? Volume,
    string? Title,
    string? Summary,
    string? Writer,
    string? Penciller,
    string? Genre,
    string? Tags,
    string? Publisher,
    string? Language,
    string? AgeRating,
    int? Count);

/// <summary>Parses the <c>ComicInfo.xml</c> metadata sidecar found inside comic archives (spec §8).</summary>
public sealed class ComicInfoReader
{
    public ComicInfo? ReadFromArchive(Stream archiveStream)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archiveStream);
            var entry = archive.Entries.FirstOrDefault(e =>
                !e.IsDirectory && e.Key is not null &&
                Path.GetFileName(e.Key).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;

            using var es = entry.OpenEntryStream();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            ms.Position = 0;
            return Parse(ms);
        }
        catch
        {
            return null;
        }
    }

    public ComicInfo? Parse(Stream xml)
    {
        XDocument doc;
        try { doc = XDocument.Load(xml); }
        catch { return null; }

        var root = doc.Root;
        if (root is null) return null;

        string? S(string name) => root.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim() is { Length: > 0 } v ? v : null;

        float? F(string name) =>
            float.TryParse(S(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

        int? I(string name) => int.TryParse(S(name), out var i) ? i : null;

        return new ComicInfo(
            Series: S("Series"),
            Number: F("Number"),
            Volume: F("Volume"),
            Title: S("Title"),
            Summary: S("Summary"),
            Writer: S("Writer"),
            Penciller: S("Penciller"),
            Genre: S("Genre"),
            Tags: S("Tags"),
            Publisher: S("Publisher"),
            Language: S("LanguageISO") ?? S("Language"),
            AgeRating: S("AgeRating"),
            Count: I("Count"));
    }
}
