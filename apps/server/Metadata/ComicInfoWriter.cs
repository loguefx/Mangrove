using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Mangrove.Server.Data;

namespace Mangrove.Server.Metadata;

/// <summary>
/// Serializes a <see cref="Series"/>' metadata to a <c>ComicInfo.xml</c> document. This is written
/// back into the library folder so automatically-fetched (or user-edited) metadata is cached on disk
/// and re-applied on future scans — even if the database is reset — mirroring what the scanner reads.
/// </summary>
public static class ComicInfoWriter
{
    public static byte[] Build(Series s)
    {
        var (writer, penciller) = ParsePeople(s.People);

        var root = new XElement("ComicInfo");
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) root.Add(new XElement(name, value));
        }

        Add("Series", s.Name);
        Add("Summary", s.Summary);
        Add("Writer", writer);
        Add("Penciller", penciller);
        Add("Genre", s.Genres);
        Add("Tags", s.Tags);
        Add("Publisher", s.Publisher);
        Add("LanguageISO", s.Language);
        Add("AgeRating", s.AgeRating);

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using (var w = XmlWriter.Create(ms, settings))
            doc.Save(w);
        return ms.ToArray();
    }

    private static (string? Writer, string? Penciller) ParsePeople(string? peopleJson)
    {
        if (string.IsNullOrWhiteSpace(peopleJson)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(peopleJson);
            var root = doc.RootElement;
            string? Get(string name) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
            return (Get("writer"), Get("penciller"));
        }
        catch
        {
            return (null, null);
        }
    }
}
