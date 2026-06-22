namespace Mangrove.Server.Auth;

public sealed class JwtOptions
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "mangrove";
    public string Audience { get; init; } = "mangrove";
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);

    public static JwtOptions FromConfiguration(IConfiguration config, ILogger logger)
    {
        var secret = config["MANGROVE_JWT_SECRET"]
                     ?? Environment.GetEnvironmentVariable("MANGROVE_JWT_SECRET");

        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogWarning(
                "MANGROVE_JWT_SECRET is not set. Generating an ephemeral dev secret; tokens will be " +
                "invalidated on restart. SET A REAL SECRET in production.");
            secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
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
