package com.ets2cluster

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.view.View
import android.view.WindowManager
import android.widget.EditText

class MainActivity : Activity() {
    private lateinit var settings: Settings
    private lateinit var view: ClusterView
    private lateinit var client: TelemetryClient
    private val ui = Handler(Looper.getMainLooper())

    private var lastOdo = -1f; private var lastFuel = -1f
    private var tripFuelL = 0.0; private var tripDistKm = 0.0
    private var update: UpdateInfo? = null
    private var demoTick = 0
    private var demoRunning = false
    private var bannerOffered = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        settings = Settings(this)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        view = ClusterView(this) { showMenu() }
        view.mph = settings.mph; view.showMake = settings.showMake
        setContentView(view)
        client = TelemetryClient({ settings.host }, { tr -> ui.post { onTruck(tr) } }, { s -> ui.post { view.statusText = s; view.invalidate() } })
    }

    @Suppress("DEPRECATION")
    private fun immersive() {
        window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_FULLSCREEN or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
            View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) { super.onWindowFocusChanged(hasFocus); if (hasFocus) immersive() }

    override fun onResume() {
        super.onResume()
        immersive()
        view.mph = settings.mph; view.showMake = settings.showMake
        if (settings.host.equals("demo", true)) startDemo() else { demoRunning = false; client.start() }
        ui.post(clockTick)
        maybeCheckUpdate()
    }

    override fun onPause() {
        super.onPause()
        client.stop(); demoRunning = false; ui.removeCallbacks(clockTick)
    }

    // the local clock needs a redraw once a second even when nothing else moves
    private val clockTick = object : Runnable { override fun run() { view.invalidate(); ui.postDelayed(this, 1000) } }

    private fun startDemo() {
        client.stop()
        if (demoRunning) return
        demoRunning = true
        view.statusText = ""
        val r = object : Runnable {
            override fun run() {
                if (!demoRunning) return
                demoTick++
                onTruck(TelemetryParser.demo(if ((demoTick / 40) % 2 == 1) "over" else ""))
                ui.postDelayed(this, 200)
            }
        }
        ui.post(r)
    }

    private fun onTruck(tr: Truck) {
        // trip: distance and fuel burned since the trip was reset; refuelling (fuel going up) is ignored
        if (tr.connected && tr.odoKm > 0) {
            if (settings.tripBaseKm.isNaN()) settings.tripBaseKm = tr.odoKm.toDouble()
            if (lastOdo >= 0 && tr.odoKm >= lastOdo && tr.odoKm - lastOdo < 5) {
                tripDistKm += tr.odoKm - lastOdo
                if (lastFuel >= 0 && tr.fuelL < lastFuel) tripFuelL += lastFuel - tr.fuelL
            }
            lastOdo = tr.odoKm; lastFuel = tr.fuelL
            view.tripKm = (tr.odoKm - settings.tripBaseKm).coerceAtLeast(0.0)
            view.tripLPer100 = if (tripDistKm > 2) tripFuelL / tripDistKm * 100 else -1.0
        }
        view.truck = tr
        view.invalidate()
    }

    private fun resetTrip() {
        settings.tripBaseKm = if (lastOdo >= 0) lastOdo.toDouble() else Double.NaN
        tripDistKm = 0.0; tripFuelL = 0.0
        view.tripKm = 0.0; view.tripLPer100 = -1.0; view.invalidate()
    }

    private fun showMenu() {
        val items = arrayOf(
            "PC address (" + settings.host.ifEmpty { "not set" } + ")",
            if (settings.mph) "Units: miles (tap for km)" else "Units: kilometres (tap for miles)",
            if (settings.showMake) "Truck name: shown (tap to hide)" else "Truck name: hidden (tap to show)",
            if (settings.checkUpdates) "Check for updates: on (tap to turn off)" else "Check for updates: off (tap to turn on)",
            "Reset trip",
            "Check for updates now",
        )
        AlertDialog.Builder(this).setTitle("ETS2 Instrument Cluster").setItems(items) { _, i ->
            when (i) {
                0 -> askHost()
                1 -> { settings.mph = !settings.mph; view.mph = settings.mph; view.invalidate() }
                2 -> { settings.showMake = !settings.showMake; view.showMake = settings.showMake; view.invalidate() }
                3 -> {
                    settings.checkUpdates = !settings.checkUpdates
                    if (settings.checkUpdates) maybeCheckUpdate() else { update = null; view.updateText = ""; view.invalidate() }
                }
                4 -> resetTrip()
                5 -> maybeCheckUpdate(true)
            }
        }.setNegativeButton("Close", null).show()
    }

    private fun askHost() {
        val input = EditText(this)
        input.inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
        input.setText(settings.host); input.hint = "e.g. 192.168.1.20  (or demo)"
        AlertDialog.Builder(this).setTitle("PC address")
            .setMessage("The IP address of the PC running Euro Truck Simulator 2 and the TruckSim GPS Telemetry Server. Type demo to see made-up values.")
            .setView(input)
            .setPositiveButton("Save") { _, _ ->
                settings.host = input.text.toString()
                client.stop(); demoRunning = false
                if (settings.host.equals("demo", true)) startDemo() else { view.statusText = ""; client.start() }
            }.setNegativeButton("Cancel", null).show()
    }

    // updates: at most once a day, nothing is downloaded; the banner offers to open the release page
    private fun maybeCheckUpdate(force: Boolean = false) {
        if (!settings.checkUpdates && !force) return
        val now = System.currentTimeMillis()
        if (!force && now - settings.lastUpdateCheck < 24L * 3600 * 1000) { showBanner(); return }
        settings.lastUpdateCheck = now
        Thread {
            val found = try { UpdateCheck.check(BuildConfig.VERSION_NAME) } catch (e: Exception) { null }
            ui.post {
                update = found
                if (found != null && (force || found.latest != settings.skippedVersion)) showBanner()
                else if (force) AlertDialog.Builder(this).setMessage("You have the latest version, or the check could not reach GitHub.").setPositiveButton("OK", null).show()
            }
        }.start()
    }

    private fun showBanner() {
        val u = update ?: return
        view.updateText = "Update ${u.latest} available - long-press, then Check for updates now"
        view.invalidate()
        if (!bannerOffered) {
            bannerOffered = true
            AlertDialog.Builder(this).setTitle("Update available").setMessage("Version ${u.latest} is available. Open the download page?")
                .setPositiveButton("Open") { _, _ -> startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(u.pageUrl))) }
                .setNeutralButton("Skip this version") { _, _ -> settings.skippedVersion = u.latest; update = null; view.updateText = ""; view.invalidate() }
                .setNegativeButton("Later", null).show()
        }
    }
}
