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

/// <summary>A single search result shown in the "Identify" picker.</summary>
public sealed record OnlineMetadataCandidate(
    int AniListId,
    string Title,
    int? Year,
    string? Format,
    string? CoverUrl,
    string? Description);

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

    // Shared field selection so search-by-name, search-many and lookup-by-id all return the same shape.
    private const string MediaFields = @"
    id
    description(asHtml: false)
    genres
    isAdult
    format
    startDate { year }
    title { romaji english }
    coverImage { extraLarge large medium }
    tags { name rank isMediaSpoiler isGeneralSpoiler }
    staff { edges { role node { name { full } } } }";

    private static readonly string SearchOneQuery =
        $"query ($search: String) {{ Media(search: $search, type: MANGA) {{ {MediaFields} }} }}";

    private static readonly string SearchManyQuery =
        $"query ($search: String) {{ Page(perPage: 12) {{ media(search: $search, type: MANGA) {{ {MediaFields} }} }} }}";

    private static readonly string ByIdQuery =
        $"query ($id: Int) {{ Media(id: $id, type: MANGA) {{ {MediaFields} }} }}";

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

        var json = await TryPostAsync(SearchOneQuery, new { search = seriesName }, $"name '{seriesName}'", ct);
        if (json is null) return null;

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

    /// <summary>Full metadata for a specific AniList id (used when an admin picks an "Identify" result).</summary>
    public async Task<OnlineSeriesMetadata?> FetchByIdAsync(int anilistId, CancellationToken ct)
    {
        if (anilistId <= 0) return null;
        var json = await TryPostAsync(ByIdQuery, new { id = anilistId }, $"id {anilistId}", ct);
        if (json is null) return null;
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
            _log.LogWarning(ex, "Failed to parse AniList by-id response for {Id}", anilistId);
            return null;
        }
    }

    /// <summary>A single candidate looked up directly by AniList id (for the "Identify" picker).</summary>
    public async Task<OnlineMetadataCandidate?> GetCandidateAsync(int anilistId, CancellationToken ct)
    {
        if (anilistId <= 0) return null;
        var json = await TryPostAsync(ByIdQuery, new { id = anilistId }, $"id {anilistId}", ct);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("Media", out var media) || media.ValueKind != JsonValueKind.Object) return null;
            return MapCandidate(media);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse AniList candidate for id {Id}", anilistId);
            return null;
        }
    }

    /// <summary>Returns up to a dozen candidate matches for the "Identify" picker.</summary>
    public async Task<IReadOnlyList<OnlineMetadataCandidate>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<OnlineMetadataCandidate>();

        var json = await TryPostAsync(SearchManyQuery, new { search = query }, $"search '{query}'", ct);
        if (json is null) return Array.Empty<OnlineMetadataCandidate>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return Array.Empty<OnlineMetadataCandidate>();
            if (!data.TryGetProperty("Page", out var page) || !page.TryGetProperty("media", out var list)
                || list.ValueKind != JsonValueKind.Array)
                return Array.Empty<OnlineMetadataCandidate>();

            var results = new List<OnlineMetadataCandidate>();
            foreach (var media in list.EnumerateArray())
                results.Add(MapCandidate(media));
            return results;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse AniList search response for '{Query}'", query);
            return Array.Empty<OnlineMetadataCandidate>();
        }
    }

    private async Task<string?> TryPostAsync(string query, object variables, string what, CancellationToken ct)
    {
        try
        {
            return await PostAsync(query, variables, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AniList lookup failed for {What}", what);
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

    private async Task<string> PostAsync(string query, object variables, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _nextAllowedUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            var payload = JsonSerializer.Serialize(new { query, variables });
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

    private static OnlineMetadataCandidate MapCandidate(JsonElement media)
    {
        int id = media.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var i) ? i : 0;
        int? year = media.TryGetProperty("startDate", out var sd) && sd.TryGetProperty("year", out var y)
            && y.TryGetInt32(out var yr) ? yr : null;
        var format = media.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String
            ? f.GetString() : null;
        string? cover = null;
        if (media.TryGetProperty("coverImage", out var ci))
            cover = (ci.TryGetProperty("large", out var lg) ? lg.GetString() : null)
                ?? (ci.TryGetProperty("medium", out var md) ? md.GetString() : null);
        var desc = media.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? Truncate(CleanDescription(d.GetString()), 240) : null;
        return new OnlineMetadataCandidate(id, Title(media), year, format, cover, desc);
    }

    private static string Title(JsonElement media)
    {
        if (media.TryGetProperty("title", out var t))
        {
            var english = t.TryGetProperty("english", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var romaji = t.TryGetProperty("romaji", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            var name = english ?? romaji;
            if (!string.IsNullOrWhiteSpace(name)) return name!;
        }
        return "Untitled";
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max].TrimEnd() + "…");

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
