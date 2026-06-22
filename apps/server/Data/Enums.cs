namespace Mangrove.Server.Data;

public enum RoleType
{
    Admin = 0,
    User = 1,
    ReadOnly = 2,
}

public enum LibraryType
{
    Manga = 0,
    Comic = 1,
    Book = 2,
    Mixed = 3,
}

public enum StorageKind
{
    Local = 0,
    Smb = 1,
}

public enum CredentialKind
{
    Local = 0,
    Smb = 1,
}
