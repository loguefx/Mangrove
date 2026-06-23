package com.mangrove.app.data

import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path
import retrofit2.http.Query

interface MangroveApi {
    @POST("api/auth/login")
    suspend fun login(@Body body: LoginRequest): AuthResponse

    @POST("api/auth/refresh")
    suspend fun refresh(): AuthResponse

    @POST("api/auth/logout")
    suspend fun logout()

    @GET("api/me")
    suspend fun me(): UserDto

    @GET("api/me/preferences")
    suspend fun getPreferences(): Map<String, String?>

    @PUT("api/me/preferences")
    suspend fun savePreferences(@Body updates: Map<String, String?>)

    @GET("api/dashboard")
    suspend fun dashboard(): DashboardDto

    @GET("api/libraries")
    suspend fun libraries(): List<LibraryDto>

    @GET("api/libraries/{id}/series")
    suspend fun series(
        @Path("id") libraryId: Int,
        @Query("filter") filter: String? = null,
        @Query("sort") sort: String = "name",
    ): List<SeriesDto>

    @GET("api/series/{id}")
    suspend fun seriesDetail(@Path("id") id: Int): SeriesDetailDto

    // ---- Favorites (want-to-read) ----

    @GET("api/want-to-read")
    suspend fun wantToRead(): List<SeriesDto>

    @GET("api/want-to-read/unread")
    suspend fun favoritesUnread(): List<FavoriteUnread>

    @POST("api/want-to-read/{seriesId}")
    suspend fun addWantToRead(@Path("seriesId") seriesId: Int)

    @DELETE("api/want-to-read/{seriesId}")
    suspend fun removeWantToRead(@Path("seriesId") seriesId: Int)

    @GET("api/chapters/{id}/manifest")
    suspend fun chapterManifest(@Path("id") id: Int): ChapterManifestDto

    @POST("api/progress")
    suspend fun saveProgress(@Body body: ProgressRequest)

    @GET("api/progress/{chapterId}")
    suspend fun progress(@Path("chapterId") chapterId: Int): ProgressDto

    // ---- Admin ----

    @GET("api/settings")
    suspend fun settings(): List<SettingDto>

    @PUT("api/settings")
    suspend fun saveSettings(@Body settings: List<SettingDto>)

    @GET("api/stats/activity")
    suspend fun activity(): List<ActivityDto>

    @GET("api/users")
    suspend fun users(): List<AdminUserDto>

    @POST("api/users")
    suspend fun createUser(@Body body: CreateUserRequest): AdminUserDto

    @PUT("api/users/{id}")
    suspend fun updateUser(@Path("id") id: Int, @Body body: UpdateUserRequest): AdminUserDto

    @POST("api/users/{id}/reset-password")
    suspend fun resetPassword(@Path("id") id: Int, @Body body: ResetPasswordRequest)

    @DELETE("api/users/{id}")
    suspend fun deleteUser(@Path("id") id: Int)

    @POST("api/libraries")
    suspend fun createLibrary(@Body body: CreateLibraryRequest): LibraryDto

    @PUT("api/libraries/{id}")
    suspend fun updateLibrary(@Path("id") id: Int, @Body body: UpdateLibraryRequest): LibraryDto

    @DELETE("api/libraries/{id}")
    suspend fun deleteLibrary(@Path("id") id: Int)

    @POST("api/libraries/{id}/scan")
    suspend fun scanLibrary(@Path("id") id: Int): ScanStatusDto

    @POST("api/credentials")
    suspend fun createCredential(@Body body: CreateCredentialRequest): CredentialDto

    @POST("api/storage/test")
    suspend fun testStorage(@Body body: StorageTestRequest): StorageTestResponse
}
