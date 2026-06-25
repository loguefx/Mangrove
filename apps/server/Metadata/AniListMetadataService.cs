using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mangrove.Server.Metadata;

/// <summary>Metadata pulled from an online provider, used as a backup when a series has no
/// <c>folder.jpg</c> cover or <c>ComicInfo.xml</c> sidecar/embedded metadata.</summary>
public sealed record OnlineSeriesMetadata(
    int? AniListId,
    string? Summary,
    string? Genres,
    string? Tags,
    string? Writer,
    string? Penciller,
    string? AgeRating,
    string? CoverUrl);

/// <summary>
/// Looks up series metadata from AniList's public GraphQL API (no API key required). This gives
/// Jellyfin-style automatic metadata for libraries that don't ship their own covers/ComicInfo.xml.
/// Calls are gently rate-limited and best-effort: any failure simply yields no metadata.
/// </summary>
public sealed class AniListMetadataService
{
    private const string Endpoint = "https://graphql.anilist.co";

    // AniList allows ~90 requests/minute (currently degraded to ~30). Keep a comfortable gap between
    // requests so a large first scan never trips the limiter.
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(800);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextAllowedUtc = DateTime.MinValue;

    private const string Query = @"
query ($search: String) {
  Media(search: $search, type: MANGA) {
    id
    description(asHtml: false)
    genres
    isAdult
    coverImage { extraLarge large }
    tags { name rank isMediaSpoiler isGeneralSpoiler }
    staff { edges { role node { name { full } } } }
  }
}";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<AniListMetadataService> _log;

    public AniListMetadataService(IHttpClientFactory http, ILogger<AniListMetadataService> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<OnlineSeriesMetadata?> FetchAsync(string seriesName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(seriesName)) return null;

        string json;
        try
        {
            json = await PostAsync(seriesName, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AniList lookup failed for '{Series}'", seriesName);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("Media", out var media) || media.ValueKind != JsonValueKind.Object)
                return null;
            return Map(media);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse AniList response for '{Series}'", seriesName);
            return null;
        }
    }

    /// <summary>Downloads a remote cover image (best-effort), returning raw bytes or null.</summary>
    public async Task<byte[]?> DownloadImageAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            return await client.GetByteArrayAsync(url, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to download cover image from {Url}", url);
            return null;
        }
    }

    private async Task<string> PostAsync(string seriesName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _nextAllowedUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            var payload = JsonSerializer.Serialize(new { query = Query, variables = new { search = seriesName } });
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var resp = await client.SendAsync(req, ct);

            // 404 with a Media query just means "no match" — AniList still returns valid JSON.
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retry = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
                _nextAllowedUtc = DateTime.UtcNow + retry;
                _log.LogInformation("AniList rate limit hit; backing off {Seconds}s", retry.TotalSeconds);
                return "{}";
            }

            _nextAllowedUtc = DateTime.UtcNow + MinInterval;
            return await resp.Content.ReadAsStringAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static OnlineSeriesMetadata Map(JsonElement media)
    {
        int? id = media.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var i) ? i : null;

        string? summary = media.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? CleanDescription(d.GetString())
            : null;

        string? genres = null;
        if (media.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array)
        {
            var list = g.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s));
            var joined = string.Join(", ", list);
            if (joined.Length > 0) genres = joined;
        }

        string? tags = null;
        if (media.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
        {
            var picked = t.EnumerateArray()
                .Where(x => !(x.TryGetProperty("isMediaSpoiler", out var s1) && s1.ValueKind == JsonValueKind.True)
                         && !(x.TryGetProperty("isGeneralSpoiler", out var s2) && s2.ValueKind == JsonValueKind.True))
                .Where(x => x.TryGetProperty("rank", out var r) && r.TryGetInt32(out var rank) && rank >= 60)
                .Select(x => x.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(10);
            var joined = string.Join(", ", picked);
            if (joined.Length > 0) tags = joined;
        }

        string? writer = null, penciller = null;
        if (media.TryGetProperty("staff", out var staff) && staff.TryGetProperty("edges", out var edges)
            && edges.ValueKind == JsonValueKind.Array)
        {
            foreach (var edge in edges.EnumerateArray())
            {
                var role = edge.TryGetProperty("role", out var rl) ? rl.GetString() ?? "" : "";
                var person = edge.TryGetProperty("node", out var node) && node.TryGetProperty("name", out var nm)
                    && nm.TryGetProperty("full", out var full) ? full.GetString() : null;
                if (string.IsNullOrWhiteSpace(person)) continue;

                if (writer is null && role.Contains("Story", StringComparison.OrdinalIgnoreCase)) writer = person;
                if (penciller is null && role.Contains("Art", StringComparison.OrdinalIgnoreCase)) penciller = person;
            }
        }

        var isAdult = media.TryGetProperty("isAdult", out var a) && a.ValueKind == JsonValueKind.True;
        string? ageRating = isAdult ? "Adult" : null;

        string? coverUrl = null;
        if (media.TryGetProperty("coverImage", out var ci))
        {
            coverUrl = (ci.TryGetProperty("extraLarge", out var xl) ? xl.GetString() : null)
                ?? (ci.TryGetProperty("large", out var lg) ? lg.GetString() : null);
        }

        return new OnlineSeriesMetadata(id, summary, genres, tags, writer, penciller, ageRating, coverUrl);
    }

    /// <summary>AniList descriptions contain light HTML and markdown spoiler markers; normalize to plain text.</summary>
    private static string? CleanDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                   .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                   .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);
        s = Regex.Replace(s, "<.*?>", string.Empty);          // strip remaining tags
        s = Regex.Replace(s, "~!|!~", string.Empty);          // strip spoiler markers
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, "\n{3,}", "\n\n").Trim();        // collapse excess blank lines
        return s.Length == 0 ? null : s;
    }
}
