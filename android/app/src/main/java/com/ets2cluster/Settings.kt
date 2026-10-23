package com.ets2cluster

import android.content.Context
import android.content.SharedPreferences

/** The remembered options (stored privately on the device). */
class Settings(ctx: Context) {
    private val p: SharedPreferences = ctx.getSharedPreferences("cluster", Context.MODE_PRIVATE)

    /** The PC running the TruckSim GPS Telemetry Server, e.g. 192.168.1.20 or 192.168.1.20:31377. "demo" shows made-up values. */
    var host: String
        get() = p.getString("host", "") ?: ""
        set(v) { p.edit().putString("host", v.trim()).apply() }
    var mph: Boolean
        get() = p.getBoolean("mph", false)
        set(v) { p.edit().putBoolean("mph", v).apply() }
    var showMake: Boolean
        get() = p.getBoolean("make", true)
        set(v) { p.edit().putBoolean("make", v).apply() }
    var checkUpdates: Boolean
        get() = p.getBoolean("updates", true)
        set(v) { p.edit().putBoolean("updates", v).apply() }
    var lastUpdateCheck: Long
        get() = p.getLong("lastCheck", 0L)
        set(v) { p.edit().putLong("lastCheck", v).apply() }
    var skippedVersion: String
        get() = p.getString("skipped", "") ?: ""
        set(v) { p.edit().putString("skipped", v).apply() }
    /** Odometer reading when the trip counter was last reset (NaN = not started). */
    var tripBaseKm: Double
        get() = p.getString("tripBase", null)?.toDoubleOrNull() ?: Double.NaN
        set(v) { p.edit().putString("tripBase", v.toString()).apply() }
}
