using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mangrove.Server.Hosting;

/// <summary>
/// Tiny CLI for managing the Windows service, e.g. <c>Mangrove.exe install</c>. Delegates to the
/// built-in <c>sc.exe</c> so there's no extra dependency. All control verbs require an elevated
/// (Administrator) prompt.
/// </summary>
public static class ServiceInstaller
{
    public const string ServiceName = "Mangrove";
    private const string DisplayName = "Mangrove Manga Server";
    private const string Description = "Serves the Mangrove manga/comic library API and web UI.";

    private static readonly string[] Verbs = { "install", "uninstall", "remove", "start", "stop", "restart", "status" };

    /// <summary>
    /// If the first argument is a service-control verb, handles it and returns true (with an exit
    /// code). Otherwise returns false so the host starts normally.
    /// </summary>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0) return false;

        var verb = args[0].ToLowerInvariant();
        if (Array.IndexOf(Verbs, verb) < 0) return false;

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Service management is only supported on Windows.");
            exitCode = 1;
            return true;
        }

        exitCode = HandleWindows(verb);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static int HandleWindows(string verb)
    {
        if (verb is not "status" && !IsAdministrator())
        {
            Console.Error.WriteLine(
                $"'{verb}' must be run from an elevated prompt. Right-click your terminal and choose " +
                "\"Run as administrator\", then run the command again.");
            return 1;
        }

        switch (verb)
        {
            case "install":
                return Install();
            case "uninstall":
            case "remove":
                return Uninstall();
            case "start":
                return Sc($"start {ServiceName}");
            case "stop":
                return Sc($"stop {ServiceName}");
            case "restart":
                Sc($"stop {ServiceName}");
                return Sc($"start {ServiceName}");
            case "status":
                return Sc($"query {ServiceName}");
            default:
                return 1;
        }
    }

    [SupportedOSPlatform("windows")]
    private static int Install()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.Error.WriteLine("Could not resolve the executable path.");
            return 1;
        }

        var dir = Path.GetDirectoryName(exePath)!;
        var port = Environment.GetEnvironmentVariable("MANGROVE_PORT") ?? "5173";

        // Double-quote the path so the stored ImagePath survives spaces (e.g. "C:\Program Files\...").
        var binPath = $"\"\\\"{exePath}\\\"\"";
        var create = Sc($"create {ServiceName} binPath= {binPath} start= auto DisplayName= \"{DisplayName}\"");
        if (create != 0)
        {
            Console.Error.WriteLine(
                "Install failed. If the service already exists, run 'Mangrove.exe uninstall' first.");
            return create;
        }

        Sc($"description {ServiceName} \"{Description}\"");
        // Auto-restart on crash: after 5s, 5s, then every 60s.
        Sc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/60000");

        // Start it right away so the endpoint is immediately browsable after install.
        var start = Sc($"start {ServiceName}");

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mangrove");
        Console.WriteLine();
        Console.WriteLine($"Installed '{ServiceName}' from:");
        Console.WriteLine($"  {dir}");
        Console.WriteLine("Your data (database, covers, settings) is kept separately so updates never erase it:");
        Console.WriteLine($"  {dataDir}");
        if (start == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Service started. It also starts automatically on boot.");
            Console.WriteLine("Open the web UI at:");
            Console.WriteLine($"  http://localhost:{port}");
            Console.WriteLine($"  http://{Environment.MachineName}:{port}   (from other devices on your network)");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Service installed but did not start. Try:  Mangrove.exe start");
        }
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static int Uninstall()
    {
        Sc($"stop {ServiceName}"); // ignore result; may already be stopped
        var delete = Sc($"delete {ServiceName}");
        if (delete == 0) Console.WriteLine($"Removed '{ServiceName}'.");
        return delete;
    }

    [SupportedOSPlatform("windows")]
    private static int Sc(string arguments)
    {
        var psi = new ProcessStartInfo("sc.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null)
        {
            Console.Error.WriteLine("Failed to launch sc.exe.");
            return 1;
        }
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (!string.IsNullOrWhiteSpace(stdout)) Console.Write(stdout);
        if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.Write(stderr);
        return proc.ExitCode;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
