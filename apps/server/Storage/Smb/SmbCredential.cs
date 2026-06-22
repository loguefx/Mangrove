namespace Mangrove.Server.Storage.Smb;

/// <summary>
/// Decrypted SMB credentials used to authenticate a session. <see cref="Key"/> is a stable
/// identifier (typically the DB credential id, or "anonymous") used for connection pooling so
/// we never keep the password in the pool key.
/// </summary>
public sealed record SmbCredential(string Key, string Username, string Password, string Domain)
{
    public static SmbCredential Anonymous { get; } =
        new("anonymous", string.Empty, string.Empty, string.Empty);
}
