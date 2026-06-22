namespace Mangrove.Server;

/// <summary>
/// Single source of truth for the product name. Changing this constant (and swapping the
/// two brand SVGs) rebrands the entire product, as described in the spec.
/// </summary>
public static class AppConstants
{
    public const string AppName = "Mangrove";
    public const string ApiVersion = "v1";
}
