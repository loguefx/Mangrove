package com.mangrove.app.data

import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.HttpUrl
import java.util.concurrent.ConcurrentHashMap

/**
 * A small persistent CookieJar so the rotating refresh cookie (mangrove_refresh) survives app
 * restarts. This lets the OkHttp authenticator silently call /api/auth/refresh without the user
 * re-entering credentials.
 */
class PersistentCookieJar(private val prefs: Prefs) : CookieJar {
    // key: name|domain|path -> Cookie
    private val store = ConcurrentHashMap<String, Cookie>()

    init {
        prefs.loadCookies().forEach { encoded ->
            decode(encoded)?.let { store[keyOf(it)] = it }
        }
    }

    @Synchronized
    override fun saveFromResponse(url: HttpUrl, cookies: List<Cookie>) {
        if (cookies.isEmpty()) return
        for (cookie in cookies) {
            val key = keyOf(cookie)
            if (cookie.expiresAt < System.currentTimeMillis()) store.remove(key)
            else store[key] = cookie
        }
        persist()
    }

    @Synchronized
    override fun loadForRequest(url: HttpUrl): List<Cookie> {
        val now = System.currentTimeMillis()
        val valid = mutableListOf<Cookie>()
        val expired = mutableListOf<String>()
        for ((key, cookie) in store) {
            if (cookie.expiresAt < now) expired += key
            else if (cookie.matches(url)) valid += cookie
        }
        if (expired.isNotEmpty()) {
            expired.forEach { store.remove(it) }
            persist()
        }
        return valid
    }

    @Synchronized
    fun clear() {
        store.clear()
        persist()
    }

    private fun persist() {
        prefs.saveCookies(store.values.map { encode(it) }.toSet())
    }

    private fun keyOf(c: Cookie) = "${c.name}|${c.domain}|${c.path}"

    private fun encode(c: Cookie): String = listOf(
        c.name, c.value, c.expiresAt.toString(), c.domain, c.path,
        c.secure.toString(), c.httpOnly.toString(), c.hostOnly.toString(),
    ).joinToString("\t")

    private fun decode(s: String): Cookie? {
        val p = s.split("\t")
        if (p.size < 8) return null
        return try {
            val b = Cookie.Builder()
                .name(p[0])
                .value(p[1])
                .expiresAt(p[2].toLong())
                .path(p[4])
            if (p[7].toBoolean()) b.hostOnlyDomain(p[3]) else b.domain(p[3])
            if (p[5].toBoolean()) b.secure()
            if (p[6].toBoolean()) b.httpOnly()
            b.build()
        } catch (e: Exception) {
            null
        }
    }
}
