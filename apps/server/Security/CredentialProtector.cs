using System.Security.Cryptography;
using System.Text;

namespace Mangrove.Server.Security;

/// <summary>
/// Encrypts SMB credential passwords at rest with AES-256-GCM (spec §5). The 256-bit key comes
/// from the MANGROVE_DATAPROTECTION_KEY environment variable; in dev a key is derived/persisted
/// so the app still runs, but operators are warned to set a real key.
/// </summary>
public sealed class CredentialProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public CredentialProtector(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Data protection key must be 32 bytes (256-bit).", nameof(key));
        _key = key;
    }

    /// <summary>Builds a 32-byte key from the env var (base64 or raw text), or the persisted secret.</summary>
    public static CredentialProtector FromConfiguration(
        IConfiguration config, ServerSecrets secrets, ILogger logger)
    {
        var raw = config["MANGROVE_DATAPROTECTION_KEY"]
                  ?? Environment.GetEnvironmentVariable("MANGROVE_DATAPROTECTION_KEY");

        byte[] key;
        if (string.IsNullOrWhiteSpace(raw))
        {
            // No env override: use the persisted key (seeded from the historical machine-derived
            // value, so credentials encrypted by older builds still decrypt).
            return new CredentialProtector(Convert.FromBase64String(secrets.DataProtectionKeyBase64));
        }
        else if (TryDecodeBase64(raw, out var decoded) && decoded.Length == 32)
        {
            key = decoded;
        }
        else
        {
            key = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        }

        return new CredentialProtector(key);
    }

    public string Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return string.Empty;
        var data = Convert.FromBase64String(encoded);
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext is too short.");

        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var cipher = data.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static bool TryDecodeBase64(string s, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        Span<byte> buffer = stackalloc byte[64];
        if (Convert.TryFromBase64String(s, buffer, out var written))
        {
            bytes = buffer[..written].ToArray();
            return true;
        }
        return false;
    }
}
