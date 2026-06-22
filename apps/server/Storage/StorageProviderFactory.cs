using Mangrove.Server.Data;
using Mangrove.Server.Security;
using Mangrove.Server.Storage.Smb;

namespace Mangrove.Server.Storage;

/// <summary>
/// Builds the correct <see cref="IStorageProvider"/> for a library or an ad-hoc connection test,
/// decrypting SMB credentials on the way. Everything downstream only sees the interface.
/// </summary>
public sealed class StorageProviderFactory
{
    private readonly SmbConnectionPool _pool;
    private readonly CredentialProtector _protector;

    public StorageProviderFactory(SmbConnectionPool pool, CredentialProtector protector)
    {
        _pool = pool;
        _protector = protector;
    }

    public IStorageProvider ForLibrary(Library library, Credential? credential)
    {
        if (library.StorageKind == StorageKind.Local)
            return new LocalStorageProvider();

        var smbCred = credential is null
            ? SmbCredential.Anonymous
            : new SmbCredential(
                credential.Id.ToString(),
                credential.Username,
                _protector.Decrypt(credential.PasswordEnc),
                credential.Domain ?? string.Empty);

        return new SmbStorageProvider(_pool, smbCred);
    }

    public IStorageProvider ForSmb(string username, string password, string? domain, string credentialKey)
    {
        var cred = string.IsNullOrEmpty(username)
            ? SmbCredential.Anonymous
            : new SmbCredential(credentialKey, username, password, domain ?? string.Empty);
        return new SmbStorageProvider(_pool, cred);
    }

    public IStorageProvider Local() => new LocalStorageProvider();
}
