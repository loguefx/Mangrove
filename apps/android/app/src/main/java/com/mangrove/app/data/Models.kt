package com.mangrove.app.data

import kotlinx.serialization.Serializable

// JSON keys from the ASP.NET API are camelCase; property names match. Unknown keys are ignored
// by the configured Json instance, so these DTOs only declare the fields the app uses.

@Serializable
data class LoginRequest(val username: String, val password: String)

@Serializable
data class UserDto(
    val id: Int,
    val username: String,
    val email: String? = null,
    val roles: List<String> = emptyList(),
)

@Serializable
data class AuthResponse(
    val accessToken: String,
    val expiresInSeconds: Int = 1800,
    val user: UserDto,
)

@Serializable
data class SeriesDto(
    val id: Int,
    val libraryId: Int,
    val name: String,
    val summary: String? = null,
    val hasCover: Boolean = false,
    val volumeCount: Int = 0,
    val chapterCount: Int = 0,
    val readChapters: Int = 0,
)

@Serializable
data class LibraryPathDto(
    val id: Int = 0,
    val path: String = "",
    val credentialId: Int? = null,
)

@Serializable
data class LibraryDto(
    val id: Int,
    val name: String,
    val seriesCount: Int = 0,
    val type: Int = 0,
    val storageKind: Int = 0,
    val rootPath: String = "",
    val credentialId: Int? = null,
    val folderWatch: Boolean = false,
    val lastScanAt: String? = null,
    val paths: List<LibraryPathDto> = emptyList(),
)

@Serializable
data class ContinueReadingDto(
    val chapterId: Int,
    val seriesId: Int,
    val seriesName: String,
    val chapterNumber: Float = 0f,
    val page: Int = 0,
    val pageCount: Int = 0,
    val hasCover: Boolean = false,
)

@Serializable
data class DashboardDto(
    val continueReading: List<ContinueReadingDto> = emptyList(),
    val recentlyAdded: List<SeriesDto> = emptyList(),
)

@Serializable
data class FavoriteUnread(
    val seriesId: Int,
    val seriesName: String,
    val hasCover: Boolean = false,
    val newChapters: Int = 0,
    val nextChapterId: Int = 0,
    val nextChapterNumber: Float = 0f,
)

@Serializable
data class ChapterDto(
    val id: Int,
    val number: Float = 0f,
    val title: String? = null,
    val pageCount: Int = 0,
    val fileFormat: String = "",
    val hasCover: Boolean = false,
)

@Serializable
data class VolumeDto(
    val id: Int,
    val number: Float = 0f,
    val name: String? = null,
    val chapters: List<ChapterDto> = emptyList(),
)

@Serializable
data class SeriesDetailDto(
    val id: Int,
    val libraryId: Int,
    val name: String,
    val summary: String? = null,
    val hasCover: Boolean = false,
    val volumes: List<VolumeDto> = emptyList(),
    val genres: String? = null,
    val tags: String? = null,
    val publisher: String? = null,
    val ageRating: String? = null,
    val language: String? = null,
    val writer: String? = null,
    val penciller: String? = null,
    val wantToRead: Boolean = false,
)

@Serializable
data class ChapterManifestDto(
    val id: Int,
    val pageCount: Int,
    val readingDirection: String = "ltr",
    val format: String = "",
    val mediaType: String = "image",
    val prevChapterId: Int? = null,
    val nextChapterId: Int? = null,
    val chapterLabel: String? = null,
    val seriesName: String? = null,
)

@Serializable
data class ProgressRequest(val chapterId: Int, val page: Int)

@Serializable
data class ProgressDto(
    val chapterId: Int = 0,
    val page: Int = 0,
    val isRead: Boolean = false,
    val updatedAt: String? = null,
)

// ---- Admin ----

@Serializable
data class SettingDto(val key: String, val value: String? = null)

@Serializable
data class AdminUserDto(
    val id: Int,
    val username: String,
    val email: String? = null,
    val roles: List<String> = emptyList(),
    val isLocked: Boolean = false,
    val libraryIds: List<Int> = emptyList(),
    val maxAgeRating: Int? = null,
    val includeUnknowns: Boolean = true,
)

@Serializable
data class CreateUserRequest(
    val username: String,
    val email: String? = null,
    val password: String,
    val roles: List<String>? = null,
    val libraryIds: List<Int>? = null,
)

@Serializable
data class UpdateUserRequest(
    val email: String? = null,
    val roles: List<String>? = null,
    val isLocked: Boolean? = null,
    val libraryIds: List<Int>? = null,
    val maxAgeRating: Int? = null,
    val includeUnknowns: Boolean? = null,
)

@Serializable
data class ResetPasswordRequest(val password: String)

@Serializable
data class CreateLibraryRequest(
    val name: String,
    val type: Int = 0,
    val storageKind: Int = 0,
    val credentialId: Int? = null,
    val folderWatch: Boolean = false,
    val paths: List<String> = emptyList(),
)

@Serializable
data class UpdateLibraryRequest(
    val name: String? = null,
    val folderWatch: Boolean? = null,
    val credentialId: Int? = null,
    val paths: List<String>? = null,
)

@Serializable
data class ScanStatusDto(
    val libraryId: Int = 0,
    val state: String = "idle",
    val queued: Boolean = false,
    val done: Int = 0,
    val total: Int = 0,
    val phase: String? = null,
)

@Serializable
data class CredentialDto(
    val id: Int,
    val label: String = "",
    val username: String = "",
    val domain: String? = null,
    val kind: Int = 1,
)

@Serializable
data class CreateCredentialRequest(
    val label: String,
    val username: String,
    val password: String,
    val domain: String? = null,
    val kind: Int = 1,
)

@Serializable
data class StorageTestRequest(
    val storageKind: Int,
    val rootPath: String,
    val username: String? = null,
    val password: String? = null,
    val domain: String? = null,
)

@Serializable
data class StorageTestResponse(
    val success: Boolean = false,
    val message: String = "",
)

@Serializable
data class ActivityDto(
    val userId: Int,
    val username: String,
    val seriesId: Int? = null,
    val seriesName: String? = null,
    val chapterId: Int = 0,
    val chapterNumber: Float = 0f,
    val chapterTitle: String? = null,
    val page: Int = 0,
    val pageCount: Int = 0,
    val isRead: Boolean = false,
    val caughtUp: Boolean = false,
    val status: String = "reading",
    val updatedAt: String = "",
)
