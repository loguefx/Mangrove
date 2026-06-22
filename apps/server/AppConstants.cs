namespace Mangrove.Server;

/// <summary>
/// Single source of truth for the product name. Changing this constant (and swapping the
/// two brand SVGs) rebrands the entire product, as described in the spec.
/// </summary>
public static class AppConstants
{
    public const string AppName = "Mangrove";
    public const string ApiVersion = "v1";

    /// <summary>Product version, read from the assembly (set via &lt;Version&gt; in the csproj).</summary>
    public static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
