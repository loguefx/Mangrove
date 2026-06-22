package com.mangrove.app.data

import retrofit2.http.Body
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

    @GET("api/chapters/{id}/manifest")
    suspend fun chapterManifest(@Path("id") id: Int): ChapterManifestDto

    @POST("api/progress")
    suspend fun saveProgress(@Body body: ProgressRequest)

    @GET("api/progress/{chapterId}")
    suspend fun progress(@Path("chapterId") chapterId: Int): ProgressDto
}
