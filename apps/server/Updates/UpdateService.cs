using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Mangrove.Server.Dtos;
using Mangrove.Server.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Mangrove.Server.Updates;

/// <summary>
/// Checks GitHub Releases for a newer Mangrove server build and can apply it in place. Because all
/// mutable state now lives outside the install folder (see <see cref="ServerPaths"/>), applying an
/// update only swaps the binaries: download the release asset, extract it, and hand off to a small
/// detached PowerShell updater that stops the service, copies the new files over the install folder
/// and restarts the service. Self-update only runs when hosted as a Windows service.
/// </summary>
public sealed class UpdateService
{
    private const string Repo = "loguefx/Mangrove";
    private const string AssetSuffix = "-win-x64.zip";

    private readonly IHttpClientFactory _http;
    private readonly ServerPaths _paths;
    private readonly ILogger<UpdateService> _log;

    public UpdateService(IHttpClientFactory http, ServerPaths paths, ILogger<UpdateService> log)
    {
        _http = http;
        _paths = paths;
        _log = log;
    }

    private static bool CanSelfUpdate =>
        OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService();

    public async Task<UpdateStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var current = AppConstants.Version;
        try
        {
            var release = await FetchLatestReleaseAsync(ct);
            if (release is null)
                return new UpdateStatusDto(current, null, false, CanSelfUpdate, null, null, null,
                    "Could not read the latest release from GitHub.");

            var available = IsNewer(release.Version, current);
            return new UpdateStatusDto(
                current, release.Version, available, CanSelfUpdate,
                release.Notes, release.HtmlUrl, release.PublishedAt);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check failed.");
            return new UpdateStatusDto(current, null, false, CanSelfUpdate, null, null, null,
                "Update check failed: " + ex.Message);
        }
    }

    public async Task<UpdateApplyResultDto> ApplyAsync(CancellationToken ct)
    {
        if (!CanSelfUpdate)
            return new UpdateApplyResultDto(false,
                "Automatic updates are only available when Mangrove runs as a Windows service. " +
                "Download the latest release and replace the files manually.");

        var release = await FetchLatestReleaseAsync(ct);
        if (release is null)
            return new UpdateApplyResultDto(false, "Could not read the latest release from GitHub.");
        if (!IsNewer(release.Version, AppConstants.Version))
            return new UpdateApplyResultDto(false, "Already running the latest version.");
        if (string.IsNullOrEmpty(release.AssetUrl))
            return new UpdateApplyResultDto(false, "The latest release has no Windows server download.");

        Directory.CreateDirectory(_paths.UpdatesDir);
        var zipPath = Path.Combine(_paths.UpdatesDir, $"mangrove-{release.Version}.zip");
        var stagingDir = Path.Combine(_paths.UpdatesDir, $"staging-{release.Version}");

        _log.LogInformation("Downloading update {Version} from {Url}", release.Version, release.AssetUrl);
        await DownloadAsync(release.AssetUrl, zipPath, ct);

        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);
        ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);

        LaunchUpdater(stagingDir);

        return new UpdateApplyResultDto(true,
            $"Update to {release.Version} started. The server will restart in a moment — refresh the page shortly.");
    }

    /// <summary>Writes and launches a detached PowerShell script that swaps the binaries and restarts the service.</summary>
    private void LaunchUpdater(string stagingDir)
    {
        var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var scriptPath = Path.Combine(_paths.UpdatesDir, "apply-update.ps1");
        var logPath = Path.Combine(_paths.UpdatesDir, "update.log");

        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
$svc      = '{ServiceInstaller.ServiceName}'
$install  = '{installDir.Replace("'", "''")}'
$staging  = '{stagingDir.Replace("'", "''")}'
$log      = '{logPath.Replace("'", "''")}'
function Log($m) {{ Add-Content -Path $log -Value (('{{0}}  {{1}}') -f (Get-Date -Format o), $m) }}

# Give the HTTP response time to flush before we take the service down.
Start-Sleep -Seconds 3
Log 'Stopping service'
sc.exe stop $svc | Out-Null
for ($i = 0; $i -lt 90; $i++) {{
    $q = (sc.exe query $svc) -join ""`n""
    if ($q -match 'STOPPED' -or $q -match 'FAILED 1060') {{ break }}
    Start-Sleep -Seconds 1
}}
Start-Sleep -Seconds 2

Log 'Copying new files'
Copy-Item -Path (Join-Path $staging '*') -Destination $install -Recurse -Force

Log 'Starting service'
sc.exe start $svc | Out-Null
Log 'Update complete'
";
        File.WriteAllText(scriptPath, script);

        // Launch fully detached so it outlives this process being stopped by the service controller.
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"")
        {
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = _paths.UpdatesDir,
        };
        Process.Start(psi);
        _log.LogInformation("Update applied; launched detached updater. Service will restart.");
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        var client = CreateClient();
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(destination);
        await resp.Content.CopyToAsync(fs, ct);
    }

    private async Task<ReleaseInfo?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        var client = CreateClient();
        using var resp = await client.GetAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest", ct);
        if (!resp.IsSuccessStatusCode) return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string? assetUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is not null && name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
        }

        return new ReleaseInfo(
            Version: tag.TrimStart('v', 'V'),
            Notes: root.TryGetProperty("body", out var b) ? b.GetString() : null,
            HtmlUrl: root.TryGetProperty("html_url", out var h) ? h.GetString() : null,
            PublishedAt: root.TryGetProperty("published_at", out var p) ? p.GetString() : null,
            AssetUrl: assetUrl);
    }

    private HttpClient CreateClient()
    {
        var client = _http.CreateClient("github");
        client.Timeout = TimeSpan.FromMinutes(10);
        if (!client.DefaultRequestHeaders.UserAgent.Any())
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mangrove-Server");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>True when <paramref name="candidate"/> is a strictly higher version than <paramref name="current"/>.</summary>
    private static bool IsNewer(string? candidate, string current)
    {
        if (!Version.TryParse(Normalize(candidate), out var c)) return false;
        if (!Version.TryParse(Normalize(current), out var cur)) return false;
        return c > cur;
    }

    private static string? Normalize(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return v;
        v = v.TrimStart('v', 'V');
        // Drop any pre-release/build suffix (e.g. "1.2.3-beta" -> "1.2.3").
        var dash = v.IndexOf('-');
        return dash >= 0 ? v[..dash] : v;
    }

    private sealed record ReleaseInfo(
        string Version, string? Notes, string? HtmlUrl, string? PublishedAt, string? AssetUrl);
}
