namespace Mangrove.Server.Auth;

public sealed class JwtOptions
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "mangrove";
    public string Audience { get; init; } = "mangrove";
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);

    public static JwtOptions FromConfiguration(
        IConfiguration config, Mangrove.Server.Security.ServerSecrets secrets, ILogger logger)
    {
        var secret = config["MANGROVE_JWT_SECRET"]
                     ?? Environment.GetEnvironmentVariable("MANGROVE_JWT_SECRET");

        if (string.IsNullOrWhiteSpace(secret))
        {
            // Fall back to the persisted secret so sessions survive restarts/updates.
            secret = secrets.JwtSecret;
        }
        else if (secret.Length < 32)
        {
            // Stretch short secrets to a safe length for HMAC-SHA256.
            secret = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
        }

        return new JwtOptions { Secret = secret };
    }
}
