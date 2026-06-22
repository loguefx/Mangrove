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
)

@Serializable
data class LibraryDto(
    val id: Int,
    val name: String,
    val seriesCount: Int = 0,
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
)

@Serializable
data class ChapterManifestDto(
    val id: Int,
    val pageCount: Int,
    val readingDirection: String = "ltr",
    val format: String = "",
    val mediaType: String = "image",
)

@Serializable
data class ProgressRequest(val chapterId: Int, val page: Int)

@Serializable
data class ProgressDto(
    val chapterId: Int = 0,
    val page: Int = 0,
    val isRead: Boolean = false,
)
