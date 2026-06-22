namespace Mangrove.Server.Auth;

/// <summary>BCrypt password hashing (spec §7 allows Argon2id or bcrypt).</summary>
public sealed class PasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }
}
