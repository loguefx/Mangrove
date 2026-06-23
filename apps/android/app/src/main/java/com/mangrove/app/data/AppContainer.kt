package com.mangrove.app.data

import android.content.Context
import coil.ImageLoader
import coil.disk.DiskCache
import coil.memory.MemoryCache
import com.jakewharton.retrofit2.converter.kotlinx.serialization.asConverterFactory
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import okhttp3.Authenticator
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.Route
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit

/**
 * Process-wide singleton holding network + session state. Because the server URL is chosen at
 * runtime, the Retrofit/OkHttp stack is (re)built whenever the server changes.
 */
class AppContainer(context: Context) {
    private val appContext = context.applicationContext
    val prefs = Prefs(appContext)
    private val cookieJar = PersistentCookieJar(prefs)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    val downloadStore = DownloadStore(appContext)
    val downloadManager = DownloadManager(this, appContext, downloadStore, scope)

    @Volatile
    var user: UserDto? = null

    @Volatile
    private var backend: Backend? = null

    init {
        prefs.serverUrl?.let { runCatching { backend = Backend(normalize(it)) } }
        if (backend != null) downloadManager.resume()
    }

    val baseUrl: String? get() = backend?.baseUrl
    val imageLoader: ImageLoader? get() = backend?.imageLoader

    fun hasServer(): Boolean = backend != null
    fun hasSession(): Boolean = prefs.accessToken != null

    /** Validates and switches the active server. Throws if the URL is malformed. */
    fun setServer(rawUrl: String) {
        val normalized = normalize(rawUrl)
        backend = Backend(normalized)
        prefs.serverUrl = normalized
        downloadManager.resume()
    }

    fun absoluteUrl(path: String): String {
        val base = backend?.baseUrl ?: return path
        return base.trimEnd('/') + "/" + path.trimStart('/')
    }

    // ---- Session ----

    suspend fun login(username: String, password: String): UserDto {
        val b = requireBackend()
        val res = b.authApi.login(LoginRequest(username, password))
        prefs.accessToken = res.accessToken
        user = res.user
        return res.user
    }

    suspend fun restoreSession(): Boolean {
        val b = backend ?: return false
        return try {
            user = b.api.me()
            true
        } catch (e: Exception) {
            // Access token may be stale; the authenticator will have tried a refresh already.
            try {
                val refreshed = b.authApi.refresh()
                prefs.accessToken = refreshed.accessToken
                user = refreshed.user
                true
            } catch (e2: Exception) {
                false
            }
        }
    }

    suspend fun logout() {
        try {
            backend?.authApi?.logout()
        } catch (_: Exception) {
        }
        prefs.clearSession()
        cookieJar.clear()
        user = null
    }

    // ---- Data ----

    suspend fun dashboard() = requireBackend().api.dashboard()
    suspend fun libraries() = requireBackend().api.libraries()
    suspend fun series(
        libraryId: Int,
        filter: String?,
        sort: String = "name",
        genre: String? = null,
        status: String? = null,
    ) = requireBackend().api.series(libraryId, filter, sort, genre, status)
    suspend fun libraryGenres(libraryId: Int) = requireBackend().api.libraryGenres(libraryId)
    suspend fun seriesDetail(id: Int) = requireBackend().api.seriesDetail(id)

    suspend fun wantToRead() = requireBackend().api.wantToRead()
    suspend fun favoritesUnread() = requireBackend().api.favoritesUnread()
    suspend fun addFavorite(seriesId: Int) = requireBackend().api.addWantToRead(seriesId)
    suspend fun removeFavorite(seriesId: Int) = requireBackend().api.removeWantToRead(seriesId)
    suspend fun manifest(chapterId: Int) = requireBackend().api.chapterManifest(chapterId)
    suspend fun progress(chapterId: Int) = runCatching { requireBackend().api.progress(chapterId) }.getOrNull()
    suspend fun saveProgress(chapterId: Int, page: Int) =
        runCatching { requireBackend().api.saveProgress(ProgressRequest(chapterId, page)) }

    suspend fun getPreferences(): Map<String, String?> {
        val prefsMap = runCatching { requireBackend().api.getPreferences() }.getOrDefault(emptyMap())
        prefsMap["reader.dir"]?.let { prefs.readerDir = it } // cache so the reader works offline
        return prefsMap
    }

    suspend fun savePreference(key: String, value: String) {
        if (key == "reader.dir") prefs.readerDir = value
        runCatching { requireBackend().api.savePreferences(mapOf(key to value)) }
    }

    // ---- Admin ----

    val isAdmin: Boolean get() = user?.roles?.any { it.equals("Admin", ignoreCase = true) } == true

    suspend fun settings() = requireBackend().api.settings()
    suspend fun saveSettings(items: List<SettingDto>) = requireBackend().api.saveSettings(items)
    suspend fun activity() = requireBackend().api.activity()
    suspend fun users() = requireBackend().api.users()
    suspend fun createUser(body: CreateUserRequest) = requireBackend().api.createUser(body)
    suspend fun updateUser(id: Int, body: UpdateUserRequest) = requireBackend().api.updateUser(id, body)
    suspend fun resetPassword(id: Int, password: String) =
        requireBackend().api.resetPassword(id, ResetPasswordRequest(password))
    suspend fun deleteUser(id: Int) = requireBackend().api.deleteUser(id)
    suspend fun createLibrary(body: CreateLibraryRequest) = requireBackend().api.createLibrary(body)
    suspend fun updateLibrary(id: Int, body: UpdateLibraryRequest) = requireBackend().api.updateLibrary(id, body)
    suspend fun deleteLibrary(id: Int) = requireBackend().api.deleteLibrary(id)
    suspend fun scanLibrary(id: Int) = requireBackend().api.scanLibrary(id)
    suspend fun createCredential(body: CreateCredentialRequest) = requireBackend().api.createCredential(body)
    suspend fun testStorage(body: StorageTestRequest) = requireBackend().api.testStorage(body)

    /** Authenticated binary GET (page images, covers) used by the download manager. */
    suspend fun fetchBytes(path: String): ByteArray? = backend?.getBytes(path)

    private fun requireBackend(): Backend = backend ?: error("No server configured")

    // ---- URL helpers ----

    private fun normalize(raw: String): String {
        var url = raw.trim()
        if (!url.startsWith("http://") && !url.startsWith("https://")) url = "http://$url"
        if (!url.endsWith("/")) url = "$url/"
        return url
    }

    /** Bundles a Retrofit/OkHttp/Coil stack bound to one base URL. */
    private inner class Backend(val baseUrl: String) {
        private val json = Json {
            ignoreUnknownKeys = true
            isLenient = true
            explicitNulls = false
        }

        private val logging = HttpLoggingInterceptor().apply {
            level = HttpLoggingInterceptor.Level.BASIC
        }

        // Client without the authenticator, used for login/refresh/logout (and as the refresh path).
        private val authClient: OkHttpClient = OkHttpClient.Builder()
            .cookieJar(cookieJar)
            .addInterceptor(logging)
            .build()

        val authApi: MangroveApi = retrofit(authClient).create(MangroveApi::class.java)

        private val bearerInterceptor = Interceptor { chain ->
            val token = prefs.accessToken
            val req = if (token != null) {
                chain.request().newBuilder().header("Authorization", "Bearer $token").build()
            } else {
                chain.request()
            }
            chain.proceed(req)
        }

        private val authenticator = Authenticator { _: Route?, response: Response ->
            refreshOnUnauthorized(response)
        }

        private val apiClient: OkHttpClient = OkHttpClient.Builder()
            .cookieJar(cookieJar)
            .addInterceptor(bearerInterceptor)
            .addInterceptor(logging)
            .authenticator(authenticator)
            .build()

        val api: MangroveApi = retrofit(apiClient).create(MangroveApi::class.java)

        val imageLoader: ImageLoader = ImageLoader.Builder(appContext)
            .okHttpClient(apiClient)
            // No crossfade: prefetched pages should snap into place instantly.
            .crossfade(false)
            // Generous caches keep many pages resident for smooth back-and-forth reading.
            .memoryCache { MemoryCache.Builder(appContext).maxSizePercent(0.30).build() }
            .diskCache {
                DiskCache.Builder()
                    .directory(appContext.cacheDir.resolve("image_cache"))
                    .maxSizeBytes(512L * 1024 * 1024)
                    .build()
            }
            .build()

        private fun retrofit(client: OkHttpClient): Retrofit = Retrofit.Builder()
            .baseUrl(baseUrl)
            .client(client)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()

        suspend fun getBytes(path: String): ByteArray? = withContext(Dispatchers.IO) {
            val url = baseUrl.trimEnd('/') + "/" + path.trimStart('/')
            val request = Request.Builder().url(url).get().build()
            apiClient.newCall(request).execute().use { resp ->
                if (!resp.isSuccessful) return@withContext null
                resp.body?.bytes()
            }
        }

        private val refreshLock = Any()

        private fun refreshOnUnauthorized(response: Response): Request? {
            if (responseCount(response) >= 2) return null
            synchronized(refreshLock) {
                val requestToken = response.request.header("Authorization")?.removePrefix("Bearer ")
                val current = prefs.accessToken
                // Another thread may have already refreshed; retry with the newest token.
                if (current != null && current != requestToken) {
                    return response.request.newBuilder()
                        .header("Authorization", "Bearer $current").build()
                }
                val refreshed = try {
                    runBlocking { authApi.refresh() }
                } catch (e: Exception) {
                    null
                }
                if (refreshed == null) {
                    prefs.clearSession()
                    return null
                }
                prefs.accessToken = refreshed.accessToken
                user = refreshed.user
                return response.request.newBuilder()
                    .header("Authorization", "Bearer ${refreshed.accessToken}").build()
            }
        }

        private fun responseCount(response: Response): Int {
            var count = 1
            var prior = response.priorResponse
            while (prior != null) {
                count++
                prior = prior.priorResponse
            }
            return count
        }
    }
}
