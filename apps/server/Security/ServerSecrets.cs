using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mangrove.Server.Security;

/// <summary>
/// Long-lived secrets persisted to <c>secrets.json</c> in the data directory so they survive
/// restarts and updates. Persisting the JWT signing secret stops every restart from logging all
/// users out; persisting the data-protection key keeps stored NAS credentials decryptable.
/// Explicit environment variables (<c>MANGROVE_JWT_SECRET</c>, <c>MANGROVE_DATAPROTECTION_KEY</c>)
/// always take precedence over the file.
/// </summary>
public sealed class ServerSecrets
{
    public string JwtSecret { get; set; } = string.Empty;
    public string DataProtectionKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Loads the secrets file, generating and saving any missing values. The data-protection key is
    /// seeded with the historical machine-derived value so credentials encrypted by older builds
    /// (which had no persisted key) keep decrypting after the upgrade.
    /// </summary>
    public static ServerSecrets LoadOrCreate(string path, ILogger logger)
    {
        ServerSecrets secrets = new();
        try
        {
            if (File.Exists(path))
                secrets = JsonSerializer.Deserialize<ServerSecrets>(File.ReadAllText(path)) ?? new ServerSecrets();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read secrets file '{Path}'; regenerating.", path);
            secrets = new ServerSecrets();
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(secrets.JwtSecret))
        {
            secrets.JwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(secrets.DataProtectionKeyBase64))
        {
            // Preserve back-compat: older builds derived this key from the machine name on the fly.
            var legacy = SHA256.HashData(Encoding.UTF8.GetBytes("mangrove-dev-key|" + Environment.MachineName));
            secrets.DataProtectionKeyBase64 = Convert.ToBase64String(legacy);
            changed = true;
        }

        if (changed)
        {
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not persist secrets file '{Path}'.", path);
            }
        }

        return secrets;
    }
}
