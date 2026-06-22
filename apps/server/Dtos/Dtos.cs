using Mangrove.Server.Data;

namespace Mangrove.Server.Dtos;

// ---- Auth ----
public sealed record RegisterFirstRequest(string Username, string? Email, string Password);
public sealed record LoginRequest(string Username, string Password);

public sealed record UserDto(int Id, string Username, string? Email, IReadOnlyList<string> Roles);
public sealed record AuthResponse(string AccessToken, int ExpiresInSeconds, UserDto User);
public sealed record SetupStatusResponse(bool SetupComplete, string AppName);

// ---- Credentials ----
public sealed record CreateCredentialRequest(string Label, string Username, string Password, string? Domain, CredentialKind Kind);
public sealed record CredentialDto(int Id, string Label, string Username, string? Domain, CredentialKind Kind);

// ---- Libraries ----
public sealed record CreateLibraryRequest(
    string Name, LibraryType Type, StorageKind StorageKind, string RootPath, int? CredentialId, bool FolderWatch);

public sealed record LibraryDto(
    int Id, string Name, LibraryType Type, StorageKind StorageKind, string RootPath,
    int? CredentialId, bool FolderWatch, DateTime? LastScanAt, int SeriesCount);

public sealed record ScanResponse(int FilesSeen, int ChaptersAdded, int ChaptersUpdated, int ChaptersRemoved, int SeriesCount);

/// <summary>Result of queuing a scan. <paramref name="State"/> is one of "queued", "running", "idle".</summary>
public sealed record ScanStatusDto(int LibraryId, string State, bool Queued);

// ---- Storage test ----
public sealed record StorageTestRequest(
    StorageKind StorageKind, string RootPath, string? Username, string? Password, string? Domain);

public sealed record StorageTestEntry(string Name, bool IsDirectory, long Size);
public sealed record StorageTestResponse(bool Success, string Message, IReadOnlyList<StorageTestEntry> Entries);

// ---- Browse ----
public sealed record SeriesDto(int Id, int LibraryId, string Name, string? Summary, bool HasCover, int VolumeCount, int ChapterCount);
public sealed record SeriesDetailDto(
    int Id, int LibraryId, string Name, string? Summary, bool HasCover, IReadOnlyList<VolumeDto> Volumes,
    string? Genres = null, string? Tags = null, string? Publisher = null, string? AgeRating = null,
    double? AverageRating = null, int RatingCount = 0, int? MyStars = null, string? MyReview = null,
    bool WantToRead = false);
public sealed record VolumeDto(int Id, float Number, string? Name, IReadOnlyList<ChapterDto> Chapters);
public sealed record ChapterDto(int Id, float Number, string? Title, int PageCount, string FileFormat, bool HasCover);

// ---- Reading ----
public sealed record ChapterManifestDto(int Id, int PageCount, string ReadingDirection, string Format, string MediaType);
public sealed record ProgressRequest(int ChapterId, int Page, double? ScrollOffset, bool? IsRead);
public sealed record ProgressDto(int ChapterId, int Page, double? ScrollOffset, bool IsRead, DateTime UpdatedAt);

// ---- EPUB ----
public sealed record EpubSpineItemDto(string Href, string? Label);
public sealed record EpubTocItemDto(string Label, string Href);
public sealed record EpubManifestDto(
    int ChapterId, string Title, string? Author, IReadOnlyList<EpubSpineItemDto> Spine, IReadOnlyList<EpubTocItemDto> Toc);

// ---- Metadata edit ----
public sealed record UpdateSeriesRequest(
    string? Name, string? Summary, string? Publisher, string? Language, string? Genres, string? Tags, string? AgeRating);

// ---- Phase 3: users / admin ----
public sealed record AdminUserDto(
    int Id, string Username, string? Email, IReadOnlyList<string> Roles, bool IsLocked,
    DateTime CreatedAt, DateTime? LastActiveAt, IReadOnlyList<int> LibraryIds,
    int? MaxAgeRating, bool IncludeUnknowns);
public sealed record CreateUserRequest(
    string Username, string? Email, string Password, IReadOnlyList<string>? Roles,
    IReadOnlyList<int>? LibraryIds);
public sealed record UpdateUserRequest(
    string? Email, IReadOnlyList<string>? Roles, bool? IsLocked,
    IReadOnlyList<int>? LibraryIds, int? MaxAgeRating, bool? IncludeUnknowns);
public sealed record ResetPasswordRequest(string Password);

// ---- Phase 3: organize ----
public sealed record CollectionDto(int Id, string Name, bool IsPublic, int OwnerId, int ItemCount);
public sealed record CollectionDetailDto(int Id, string Name, bool IsPublic, int OwnerId, IReadOnlyList<SeriesDto> Series);
public sealed record CreateCollectionRequest(string Name, bool IsPublic);
public sealed record ReadingListDto(int Id, string Name, bool IsPublic, int OwnerId, int ItemCount);
public sealed record ReadingListItemDto(int Id, int ChapterId, int Order, string SeriesName, float ChapterNumber, string? Title, int PageCount, bool HasCover);
public sealed record ReadingListDetailDto(int Id, string Name, bool IsPublic, int OwnerId, IReadOnlyList<ReadingListItemDto> Items);
public sealed record CreateReadingListRequest(string Name, bool IsPublic);
public sealed record ReorderRequest(IReadOnlyList<int> ItemIds);
public sealed record RatingRequest(int SeriesId, int Stars);
public sealed record ReviewRequest(int SeriesId, string? Body);
public sealed record ReviewDto(int UserId, string Username, int SeriesId, int Stars, string? Body, DateTime UpdatedAt);

// ---- Phase 3: stats / settings ----
public sealed record ServerStatsDto(
    int Users, int Libraries, int Series, int Volumes, int Chapters, long TotalBytes, int TotalPages);
public sealed record UserStatsDto(int ChaptersRead, int PagesRead, int InProgress, int WantToReadCount);
public sealed record SettingDto(string Key, string? Value);
public sealed record TaskLogDto(int Id, string Kind, string? Target, string Status, string? Message, DateTime StartedAt, DateTime? FinishedAt);

// ---- Search / dashboard ----
public sealed record SearchResultDto(int Id, string Name, int LibraryId, bool HasCover, string Kind);
public sealed record ContinueReadingDto(
    int ChapterId, int SeriesId, string SeriesName, float ChapterNumber, int Page, int PageCount, bool HasCover);
public sealed record DashboardDto(
    IReadOnlyList<ContinueReadingDto> ContinueReading, IReadOnlyList<SeriesDto> RecentlyAdded);

/// <summary>A favorited series that has new, unread chapters to catch up on.</summary>
public sealed record FavoriteUnreadDto(
    int SeriesId, string SeriesName, bool HasCover, int NewChapters, int NextChapterId, float NextChapterNumber);
