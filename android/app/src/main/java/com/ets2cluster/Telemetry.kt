package com.ets2cluster

import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/** Everything the cluster shows, read from the TruckSim GPS Telemetry Server. */
class Truck {
    var connected = false; var paused = false
    var make = ""
    var speedKmh = 0f; var rpm = 0f; var rpmMax = 0f
    var speedLimit = 0
    var cruiseOn = false; var cruiseKmh = 0f
    var gear = 0; var forwardGears = 0; var shifter = ""
    var fuelL = 0f; var fuelCapL = 0f; var fuelAvgLPerKm = 0f; var rangeKm = 0f; var adBlueL = 0f; var adBlueCapL = 0f; var odoKm = 0f
    var oilC = 0f; var waterC = 0f; var brakeC = 0f; var airPsi = 0f; var batteryV = 0f
    var parkBrake = false; var motorBrake = false; var engineOn = false; var wipers = false
    var lowBeam = false; var highBeam = false; var beacon = false; var blinkerL = false; var blinkerR = false
    var retarder = 0; var retarderSteps = 0
    var airWarn = false; var oilWarn = false; var waterWarn = false; var batteryWarn = false; var fuelWarn = false; var adBlueWarn = false
    /** Game clock as minutes since midnight (-1 unknown). */
    var gameClockMin = -1
    /** Game minutes until arrival / until a rest is needed (-1 unknown). */
    var etaMin = -1f; var restMin = -1f

    val fuelFraction get() = if (fuelCapL > 0) (fuelL / fuelCapL).coerceIn(0f, 1f) else 0f
    val adBlueFraction get() = if (adBlueCapL > 0) (adBlueL / adBlueCapL).coerceIn(0f, 1f) else 0f

    /** Changes only when something visible changes, so the cluster is redrawn only when needed. */
    fun signature(): String = listOf(
        connected, paused, make, "%.1f".format(speedKmh), speedLimit, cruiseOn, "%.0f".format(cruiseKmh), (rpm / 5).toInt(), "%.0f".format(rpmMax), gear, shifter,
        "%.1f".format(fuelL), "%.1f".format(adBlueL), "%.3f".format(fuelAvgLPerKm), "%.0f".format(rangeKm), "%.1f".format(odoKm), "%.0f".format(waterC), "%.0f".format(oilC),
        parkBrake, motorBrake, retarder, wipers, lowBeam, highBeam, beacon, blinkerL, blinkerR, airWarn, oilWarn, waterWarn, batteryWarn, fuelWarn, adBlueWarn,
        gameClockMin, "%.0f".format(etaMin), "%.0f".format(restMin),
    ).joinToString("|")
}

object TelemetryParser {
    private val dateRe = Regex("""^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})""")
    private val monthDays = intArrayOf(0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334)   // days before each month in year 1 (not a leap year)

    /** Minutes since 0001-01-01 00:00 for a game date, -1 when it is empty or not a date. */
    fun minutes(s: String): Float {
        val m = dateRe.find(s) ?: return -1f
        val (y, mo, d, h, mi) = m.destructured
        if (y.toInt() == 1 && mo.toInt() == 1 && d.toInt() == 1 && h.toInt() == 0 && mi.toInt() == 0) return -1f
        val days = monthDays[(mo.toInt() - 1).coerceIn(0, 11)] + d.toInt() - 1
        return ((days * 24 + h.toInt()) * 60 + mi.toInt()).toFloat()
    }

    fun parse(json: String): Truck {
        val root = JSONObject(json)
        val game = root.optJSONObject("game") ?: JSONObject()
        val t = root.optJSONObject("truck") ?: JSONObject()
        val nav = root.optJSONObject("navigation") ?: JSONObject()
        val tr = Truck()
        tr.connected = game.optBoolean("connected"); tr.paused = game.optBoolean("paused")
        tr.make = t.optString("make", "")
        tr.speedKmh = t.optDouble("speed", 0.0).toFloat(); tr.rpm = t.optDouble("engineRpm", 0.0).toFloat(); tr.rpmMax = t.optDouble("engineRpmMax", 0.0).toFloat()
        tr.speedLimit = Math.round(nav.optDouble("speedLimit", 0.0)).toInt()
        tr.cruiseOn = t.optBoolean("cruiseControlOn"); tr.cruiseKmh = t.optDouble("cruiseControlSpeed", 0.0).toFloat()
        tr.gear = t.optInt("displayedGear"); tr.forwardGears = t.optInt("forwardGears"); tr.shifter = t.optString("shifterType", "")
        tr.fuelL = t.optDouble("fuel", 0.0).toFloat(); tr.fuelCapL = t.optDouble("fuelCapacity", 0.0).toFloat()
        tr.fuelAvgLPerKm = t.optDouble("fuelAverageConsumption", 0.0).toFloat(); tr.rangeKm = t.optDouble("fuelRange", 0.0).toFloat()
        tr.adBlueL = t.optDouble("adblue", 0.0).toFloat(); tr.adBlueCapL = t.optDouble("adblueCapacity", 0.0).toFloat(); tr.odoKm = t.optDouble("odometer", 0.0).toFloat()
        tr.oilC = t.optDouble("oilTemperature", 0.0).toFloat(); tr.waterC = t.optDouble("waterTemperature", 0.0).toFloat(); tr.brakeC = t.optDouble("brakeTemperature", 0.0).toFloat()
        tr.airPsi = t.optDouble("airPressure", 0.0).toFloat(); tr.batteryV = t.optDouble("batteryVoltage", 0.0).toFloat()
        tr.parkBrake = t.optBoolean("parkBrakeOn"); tr.motorBrake = t.optBoolean("motorBrakeOn"); tr.engineOn = t.optBoolean("engineOn"); tr.wipers = t.optBoolean("wipersOn")
        tr.lowBeam = t.optBoolean("lightsBeamLowOn"); tr.highBeam = t.optBoolean("lightsBeamHighOn"); tr.beacon = t.optBoolean("lightsBeaconOn")
        tr.blinkerL = t.optBoolean("blinkerLeftOn"); tr.blinkerR = t.optBoolean("blinkerRightOn")
        tr.retarder = t.optInt("retarderBrake"); tr.retarderSteps = t.optInt("retarderStepCount")
        tr.airWarn = t.optBoolean("airPressureWarningOn") || t.optBoolean("airPressureEmergencyOn"); tr.oilWarn = t.optBoolean("oilPressureWarningOn")
        tr.waterWarn = t.optBoolean("waterTemperatureWarningOn"); tr.batteryWarn = t.optBoolean("batteryVoltageWarningOn")
        tr.fuelWarn = t.optBoolean("fuelWarningOn"); tr.adBlueWarn = t.optBoolean("adblueWarningOn")
        val gm = minutes(game.optString("time", ""))
        tr.gameClockMin = if (gm < 0) -1 else (gm.toInt() % 1440)
        tr.etaMin = minutes(nav.optString("estimatedTime", "")); tr.restMin = minutes(game.optString("nextRestStopTime", ""))
        return tr
    }

    /** The demonstration truck (address "demo"), also used to try the layout without the game. */
    fun demo(state: String = ""): Truck {
        val t = Truck().apply {
            connected = true; make = "Volvo"; speedKmh = 87f; speedLimit = 90; cruiseOn = true; cruiseKmh = 90f; rpm = 1180f; rpmMax = 2200f; gear = 12; forwardGears = 12; shifter = "automatic"
            fuelL = 1110f; fuelCapL = 1500f; fuelAvgLPerKm = 0.277f; rangeKm = 3020f; adBlueL = 51f; adBlueCapL = 60f; odoKm = 2369f; waterC = 87f; oilC = 96f; lowBeam = true; blinkerR = true
            gameClockMin = 6 * 60 + 50; etaMin = 72f; restMin = 8 * 60 + 12f
        }
        if (state == "over") { t.speedKmh = 98f; t.rpm = 1480f; t.blinkerR = false; t.highBeam = true }
        return t
    }
}

/** Polls the telemetry server a few times a second on a background thread and reports every answer through [onTruck]. */
class TelemetryClient(private val host: () -> String, private val onTruck: (Truck) -> Unit, private val onStatus: (String) -> Unit) {
    @Volatile private var running = false
    private var thread: Thread? = null

    fun start() {
        if (running) return
        running = true
        thread = Thread({
            while (running) {
                var delay = 140L
                try {
                    var h = host().trim()
                    if (h.isEmpty()) { onStatus("Long-press the cluster, then Settings, to enter the PC's address"); onTruck(Truck()); delay = 600; Thread.sleep(delay); continue }
                    if (!h.contains(":")) h += ":31377"
                    if (!h.startsWith("http", ignoreCase = true)) h = "http://$h"
                    val c = URL(h.trimEnd('/') + "/api/ets2/telemetry").openConnection() as HttpURLConnection
                    c.connectTimeout = 1500; c.readTimeout = 2000
                    val body = try { c.inputStream.bufferedReader().use { it.readText() } } finally { c.disconnect() }
                    val tr = TelemetryParser.parse(body)
                    onStatus(if (tr.connected) "" else "Waiting for the game")
                    onTruck(tr)
                } catch (e: InterruptedException) {
                    return@Thread
                } catch (e: Exception) {
                    onStatus("Telemetry server not reachable")
                    onTruck(Truck())
                    delay = 1000
                }
                try { Thread.sleep(delay) } catch (e: InterruptedException) { return@Thread }
            }
        }, "telemetry")
        thread!!.isDaemon = true
        thread!!.start()
    }

    fun stop() {
        running = false
        thread?.interrupt()
        thread = null
    }
}
