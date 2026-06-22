using System.Globalization;
using System.Text.RegularExpressions;

namespace Mangrove.Server.Scanning;

public sealed record ParsedInfo(string Series, float? Volume, float? Chapter, bool IsSpecial);

/// <summary>
/// Parses comic/manga filenames into {series, volume, chapter} using Kavita-style heuristics
/// (spec §8). Deliberately conservative: when a token is absent we leave it null so the scanner
/// can fall back to folder structure.
/// </summary>
public sealed partial class FilenameParser
{
    [GeneratedRegex(@"(?:\b|_)(?:v|vol|volume)\.?\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeRegex();

    [GeneratedRegex(@"(?:\b|_)(?:c|ch|chap|chapter|episode|ep|#)\.?\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterRegex();

    // A bare trailing number like "Title 12" or "Title - 012".
    [GeneratedRegex(@"(?:^|[\s_\-])(\d{1,4}(?:\.\d+)?)\s*$")]
    private static partial Regex TrailingNumberRegex();

    private static readonly string[] SpecialMarkers =
    {
        "special", "omake", "extra", "oneshot", "one-shot", "sp", "prologue", "epilogue",
    };

    public ParsedInfo Parse(string fileName)
    {
        var name = StripExtension(fileName);
        var working = name.Replace('_', ' ').Trim();

        var isSpecial = SpecialMarkers.Any(m =>
            working.Contains(m, StringComparison.OrdinalIgnoreCase));

        float? volume = null;
        var vMatch = VolumeRegex().Match(working);
        if (vMatch.Success && TryFloat(vMatch.Groups[1].Value, out var v)) volume = v;

        float? chapter = null;
        var cMatch = ChapterRegex().Match(working);
        if (cMatch.Success && TryFloat(cMatch.Groups[1].Value, out var c)) chapter = c;

        // Series name = text before the first recognized token.
        var cutIndex = working.Length;
        if (vMatch.Success) cutIndex = Math.Min(cutIndex, vMatch.Index);
        if (cMatch.Success) cutIndex = Math.Min(cutIndex, cMatch.Index);

        var series = working[..cutIndex];

        if (chapter is null && !vMatch.Success)
        {
            // No explicit chapter token: try a bare trailing number, but keep the series text.
            var tMatch = TrailingNumberRegex().Match(working);
            if (tMatch.Success && TryFloat(tMatch.Groups[1].Value, out var t))
            {
                chapter = t;
                series = working[..tMatch.Index];
            }
        }

        series = CleanSeries(series);
        if (string.IsNullOrWhiteSpace(series))
            series = CleanSeries(working);

        return new ParsedInfo(series, volume, chapter, isSpecial);
    }

    private static string CleanSeries(string s)
    {
        s = s.Trim(' ', '-', '_', '.', ',', '(', '[');
        s = Regex.Replace(s, @"\s{2,}", " ");
        return s.Trim();
    }

    private static string StripExtension(string fileName)
    {
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return fileName[..^7];
        var ext = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(ext) ? fileName : fileName[..^ext.Length];
    }

    private static bool TryFloat(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
