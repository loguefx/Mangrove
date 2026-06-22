namespace Mangrove.Server.Data;

// NOTE: Phase 1 implements the core of the data model from spec §6 (auth, storage, libraries,
// the Series -> Volume -> Chapter -> MangaFile hierarchy, reading progress and settings).
// The "organize" entities (Collection, ReadingList, Rating/Review, Bookmark, Annotation,
// WantToRead, AgeRestriction) are introduced in Phase 3 per the roadmap (spec §14).

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActiveAt { get; set; }
    public bool IsLocked { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }

    public List<UserRole> UserRoles { get; set; } = new();
    public List<LibraryAccess> LibraryAccess { get; set; } = new();
    public List<RefreshToken> RefreshTokens { get; set; } = new();
    public List<ReadingProgress> ReadingProgress { get; set; } = new();
}

public class Role
{
    public int Id { get; set; }
    public RoleType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool CanDownload { get; set; }
    public bool CanManageLibraries { get; set; }

    public List<UserRole> UserRoles { get; set; } = new();
}

public class UserRole
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    /// <summary>SHA-256 hash of the opaque refresh token; the raw token is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt == null && DateTime.UtcNow < ExpiresAt;
}

public class Credential
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    /// <summary>AES-GCM encrypted password (base64). Never returned to clients.</summary>
    public string PasswordEnc { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public CredentialKind Kind { get; set; } = CredentialKind.Smb;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Library> Libraries { get; set; } = new();
}

public class Library
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public LibraryType Type { get; set; } = LibraryType.Manga;
    public StorageKind StorageKind { get; set; } = StorageKind.Local;
    /// <summary>
    /// Primary/first folder of the library. Kept in sync with <see cref="Paths"/>[0] for backwards
    /// compatibility and display; the scanner walks every entry in <see cref="Paths"/>.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
    public int? CredentialId { get; set; }
    public Credential? Credential { get; set; }
    public bool FolderWatch { get; set; }
    public DateTime? LastScanAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>All storage folders that make up this library (a series can span several of them).</summary>
    public List<LibraryPath> Paths { get; set; } = new();
    public List<Series> Series { get; set; } = new();
    public List<LibraryAccess> LibraryAccess { get; set; } = new();
}

/// <summary>
/// One storage folder belonging to a <see cref="Library"/>. A library can have several (e.g. when a
/// NAS share fills up and content continues on another share). Each path may optionally use its own
/// credential; when null it falls back to the library's credential.
/// </summary>
public class LibraryPath
{
    public int Id { get; set; }
    public int LibraryId { get; set; }
    public Library Library { get; set; } = null!;
    public string Path { get; set; } = string.Empty;
    public int? CredentialId { get; set; }
    public Credential? Credential { get; set; }
}

public class LibraryAccess
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int LibraryId { get; set; }
    public Library Library { get; set; } = null!;
}

public class Series
{
    public int Id { get; set; }
    public int LibraryId { get; set; }
    public Library Library { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string SortName { get; set; } = string.Empty;
    public string? LocalizedName { get; set; }
    public string? CoverPath { get; set; }
    public string? AgeRating { get; set; }
    /// <summary>Numeric tier derived from <see cref="AgeRating"/> for SQL filtering (spec §7).</summary>
    public int AgeRatingTier { get; set; }
    public string? Summary { get; set; }
    public string? Publisher { get; set; }
    public string? Language { get; set; }
    public string? Genres { get; set; }
    public string? Tags { get; set; }
    /// <summary>JSON blob of people (writer/penciller/etc).</summary>
    public string? People { get; set; }
    /// <summary>JSON blob of external ids (anilist/mal/comicvine).</summary>
    public string? ExternalIds { get; set; }
    /// <summary>When true, scans won't overwrite metadata — user edits win (spec §8).</summary>
    public bool MetadataLocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Volume> Volumes { get; set; } = new();
}

public class Volume
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    /// <summary>Volume number; 0 designates loose chapters / no explicit volume.</summary>
    public float Number { get; set; }
    public string? Name { get; set; }

    public List<Chapter> Chapters { get; set; } = new();
}

public class Chapter
{
    public int Id { get; set; }
    public int VolumeId { get; set; }
    public Volume Volume { get; set; } = null!;
    public float Number { get; set; }
    public string? Title { get; set; }
    public int PageCount { get; set; }
    public string FileFormat { get; set; } = string.Empty;
    public string? Range { get; set; }
    public string? CoverPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<MangaFile> Files { get; set; } = new();
    public List<ReadingProgress> ReadingProgress { get; set; } = new();
}

public class MangaFile
{
    public int Id { get; set; }
    public int ChapterId { get; set; }
    public Chapter Chapter { get; set; } = null!;
    /// <summary>Canonical storage path (UNC, smb:// or local) understood by IStorageProvider.</summary>
    public string StoragePath { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Format { get; set; } = string.Empty;
    /// <summary>Cheap content signature (size + mtime) used for incremental scans.</summary>
    public string Hash { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class ReadingProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ChapterId { get; set; }
    public Chapter Chapter { get; set; } = null!;
    public int PageNum { get; set; }
    public double? ScrollOffset { get; set; }
    public bool IsRead { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>
/// Per-user key/value preferences (e.g. reading direction) so settings sync across
/// every device/client for that account rather than living in browser localStorage.
/// </summary>
public class UserPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public class JobLog
{
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
}
