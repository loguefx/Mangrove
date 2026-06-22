package com.mangrove.app.data

import android.content.Context

/**
 * Lightweight synchronous storage for the server URL, the current access token, and the
 * persisted refresh cookie. SharedPreferences is used (not DataStore) because the OkHttp
 * interceptor/authenticator and cookie jar read these synchronously on network threads.
 */
class Prefs(context: Context) {
    private val sp = context.applicationContext.getSharedPreferences("mangrove", Context.MODE_PRIVATE)

    var serverUrl: String?
        get() = sp.getString(KEY_SERVER, null)
        set(value) = sp.edit().putString(KEY_SERVER, value).apply()

    var accessToken: String?
        get() = sp.getString(KEY_TOKEN, null)
        set(value) = sp.edit().putString(KEY_TOKEN, value).apply()

    fun loadCookies(): Set<String> = sp.getStringSet(KEY_COOKIES, emptySet()) ?: emptySet()

    fun saveCookies(values: Set<String>) {
        sp.edit().putStringSet(KEY_COOKIES, values).apply()
    }

    /** Clears session state (token + cookies) but keeps the saved server URL. */
    fun clearSession() {
        sp.edit().remove(KEY_TOKEN).remove(KEY_COOKIES).apply()
    }

    private companion object {
        const val KEY_SERVER = "server_url"
        const val KEY_TOKEN = "access_token"
        const val KEY_COOKIES = "cookies"
    }
}
