package com.ets2cluster

import org.json.JSONArray
import java.net.HttpURLConnection
import java.net.URL

/** A newer release found on GitHub. */
class UpdateInfo(val latest: String, val tag: String, val pageUrl: String)

/**
 * Asks GitHub for the newest release that has an Android app attached (one small request to the public API; nothing about you or your device is sent
 * beyond a normal web request). It never downloads or installs anything: the user chooses to open the release page.
 */
object UpdateCheck {
    const val REPO = "johnevansau320/ETS2-Intrument-Cluster"
    const val RELEASES_PAGE = "https://github.com/$REPO/releases/latest"

    /** "v1.3.0" -> [1, 3, 0]; null when it is not a version. */
    fun version(tag: String): List<Int>? {
        val parts = tag.trim().trimStart('v', 'V').split('.')
        if (parts.size < 2 || parts.size > 4) return null
        val nums = parts.map { it.toIntOrNull() ?: return null }
        return (nums + listOf(0, 0, 0)).take(3)
    }

    /** true when [a] is a newer version than [b]. */
    fun newer(a: List<Int>, b: List<Int>): Boolean {
        for (i in 0 until 3) { if (a[i] != b[i]) return a[i] > b[i] }
        return false
    }

    /** Picks the newest release (the list is newest first) that has an .apk attached and is newer than [current]; null if none. */
    fun fromReleases(json: String, current: String): UpdateInfo? {
        val cur = version(current) ?: return null
        val arr = JSONArray(json)
        for (i in 0 until arr.length()) {
            val r = arr.optJSONObject(i) ?: continue
            if (r.optBoolean("draft") || r.optBoolean("prerelease")) continue
            val assets = r.optJSONArray("assets") ?: continue
            var hasApk = false
            for (k in 0 until assets.length()) if (assets.optJSONObject(k)?.optString("name", "")?.endsWith(".apk", ignoreCase = true) == true) hasApk = true
            if (!hasApk) continue
            val tag = r.optString("tag_name", "")
            val v = version(tag) ?: continue
            var url = r.optString("html_url", RELEASES_PAGE)
            if (!url.startsWith("https://github.com/")) url = RELEASES_PAGE   // only ever open a page on github.com
            return if (newer(v, cur)) UpdateInfo(v.joinToString("."), tag, url) else null
        }
        return null
    }

    /** Blocking: call from a background thread. Throws when GitHub cannot be reached. */
    fun check(current: String): UpdateInfo? {
        val c = URL("https://api.github.com/repos/$REPO/releases?per_page=10").openConnection() as HttpURLConnection
        c.connectTimeout = 8000; c.readTimeout = 10000
        c.setRequestProperty("User-Agent", "ETS2-Instrument-Cluster-Android/$current")
        c.setRequestProperty("Accept", "application/vnd.github+json")
        val body = try { c.inputStream.bufferedReader().use { it.readText() } } finally { c.disconnect() }
        return fromReleases(body, current)
    }
}
