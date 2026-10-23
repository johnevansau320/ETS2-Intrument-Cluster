package com.ets2cluster

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RadialGradient
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.os.SystemClock
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.View
import java.util.Calendar
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.ceil
import kotlin.math.cos
import kotlin.math.exp
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * The instrument cluster, drawn on a fixed 1280 x 720 canvas that is scaled to fit the screen: speedometer with needle, gear, rev strip,
 * diesel and AdBlue gauges, consumption, range, cruise, ETA, clocks, odometer and trip, lamps and warnings. The layout is the same as the Windows app.
 */
class ClusterView(ctx: Context, private val onLongPress: () -> Unit) : View(ctx) {
    companion object {
        const val DW = 1280f; const val DH = 720f
        private const val CX = 640f; private const val CY = 440f
        private const val R = 330f   // radius of the speed dial

        private val BG = Color.rgb(3, 6, 10)
        private val WHITE = Color.rgb(236, 241, 246); private val DIM = Color.rgb(124, 136, 148); private val GREY = Color.rgb(58, 68, 79)
        private val GREEN = Color.rgb(56, 214, 108); private val AMBER = Color.rgb(255, 176, 46); private val RED = Color.rgb(255, 59, 48); private val BLUE = Color.rgb(58, 160, 255)
    }

    var truck = Truck()
    var mph = false
    var showMake = true
    var tripKm = 0.0
    var tripLPer100 = -1.0
    var statusText = ""
    var updateText = ""

    // smoothed values: the needle glides between telemetry updates
    private var dispSpeed = 0f; private var dispRpm = 0f; private var dispFuel = 0f; private var dispAdBlue = 0f
    private var lastFrame = 0L

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val tp = Paint(Paint.ANTI_ALIAS_FLAG).apply { textAlign = Paint.Align.LEFT }
    private val bold = Typeface.create("sans-serif-condensed", Typeface.BOLD)
    private val light = Typeface.create("sans-serif-condensed", Typeface.NORMAL)
    private val path = Path()
    private val oval = RectF()
    private val box = RectF()

    private val gestures = GestureDetector(ctx, object : GestureDetector.SimpleOnGestureListener() {
        override fun onLongPress(e: MotionEvent) { onLongPress() }
        override fun onDown(e: MotionEvent) = true
    })

    override fun onTouchEvent(event: MotionEvent): Boolean = gestures.onTouchEvent(event) || super.onTouchEvent(event)

    /** Moves the smoothed values towards the telemetry; true while anything is still moving. */
    private fun step(dt: Float): Boolean {
        val k = (1 - exp(-dt * 12f.toDouble())).toFloat()
        var moving = false
        fun ease(cur: Float, target: Float, eps: Float): Float {
            val d = target - cur
            if (abs(d) < eps) { if (d != 0f) moving = true; return target }
            moving = true
            return cur + d * k
        }
        dispSpeed = ease(dispSpeed, max(0f, truck.speedKmh), 0.03f)
        dispRpm = ease(dispRpm, max(0f, truck.rpm), 2f)
        dispFuel = ease(dispFuel, truck.fuelFraction, 0.001f)
        dispAdBlue = ease(dispAdBlue, truck.adBlueFraction, 0.001f)
        return moving
    }

    // ------------------------------------------------------------------------------------------ units

    private fun spd(kmh: Float) = if (mph) kmh * 0.621371f else kmh
    private fun dist(km: Float) = if (mph) km * 0.621371f else km
    private val spdUnit get() = if (mph) "mph" else "km/h"
    private val distUnit get() = if (mph) "mi" else "km"

    private fun clock(minutes: Int) = if (minutes < 0) "--:--" else "%02d:%02d".format((minutes / 60) % 24, minutes % 60)
    private fun hoursMinutes(min: Float): String { if (min < 0) return "--:--"; val m = min.roundToInt(); return "%d:%02d".format(m / 60, m % 60) }
    private fun num(v: Float) = v.roundToInt().toString()

    // ------------------------------------------------------------------------------------------ helpers

    private fun p(angleDeg: Float, r: Float): FloatArray {
        val a = angleDeg * PI / 180
        return floatArrayOf((CX + r * cos(a)).toFloat(), (CY - r * sin(a)).toFloat())
    }

    /** align: 0 left, 1 centre, 2 right of x; y is the vertical centre. Returns the text width. */
    private fun text(c: Canvas, t: String, x: Float, y: Float, size: Float, color: Int, align: Int = 1, lightFace: Boolean = false): Float {
        tp.typeface = if (lightFace) light else bold
        tp.textSize = size; tp.color = color
        val w = tp.measureText(t)
        val fm = tp.fontMetrics
        val bx = when (align) { 0 -> x; 1 -> x - w / 2; else -> x - w }
        c.drawText(t, bx, y - (fm.ascent + fm.descent) / 2, tp)
        return w
    }

    private fun line(c: Canvas, x1: Float, y1: Float, x2: Float, y2: Float, color: Int, width: Float, round: Boolean = false) {
        stroke.shader = null; stroke.color = color; stroke.strokeWidth = width; stroke.strokeCap = if (round) Paint.Cap.ROUND else Paint.Cap.BUTT
        c.drawLine(x1, y1, x2, y2, stroke)
    }

    private fun line(c: Canvas, a: FloatArray, b: FloatArray, color: Int, width: Float, round: Boolean = false) = line(c, a[0], a[1], b[0], b[1], color, width, round)

    /** Arc of radius r from math angle a0 to a1 (degrees; counter-clockwise positive, as on a maths graph). */
    private fun arc(c: Canvas, r: Float, a0: Float, a1: Float, color: Int, width: Float) {
        oval.set(CX - r, CY - r, CX + r, CY + r)
        stroke.shader = null; stroke.color = color; stroke.strokeWidth = width; stroke.strokeCap = Paint.Cap.ROUND
        c.drawArc(oval, -a0, a0 - a1, false, stroke)
    }

    private fun roundRect(c: Canvas, l: Float, t: Float, r: Float, b: Float, rad: Float, fillColor: Int, strokeColor: Int, strokeW: Float) {
        box.set(l, t, r, b)
        fill.shader = null; fill.color = fillColor
        c.drawRoundRect(box, rad, rad, fill)
        stroke.shader = null; stroke.color = strokeColor; stroke.strokeWidth = strokeW; stroke.strokeCap = Paint.Cap.BUTT
        c.drawRoundRect(box, rad, rad, stroke)
    }

    // ------------------------------------------------------------------------------------------ drawing

    override fun onDraw(canvas: Canvas) {
        val now = SystemClock.uptimeMillis()
        val dt = min(0.1f, max(0.001f, (now - lastFrame) / 1000f)); lastFrame = now
        val moving = step(dt)

        canvas.drawColor(BG)
        val w = width.toFloat(); val h = height.toFloat()
        if (w < 10 || h < 10) return
        val s = min(w / DW, h / DH)
        val save = canvas.save()
        canvas.translate((w - DW * s) / 2, (h - DH * s) / 2)
        canvas.scale(s, s)
        canvas.clipRect(0f, 0f, DW, DH)
        cluster(canvas)
        canvas.restoreToCount(save)

        if (moving) postInvalidateOnAnimation()
    }

    private fun cluster(c: Canvas) {
        val t = truck
        val max = if (mph) 80f else 120f
        val v = spd(dispSpeed)
        val limit = if (t.speedLimit > 0) spd(t.speedLimit.toFloat()) else 0f
        val over = limit > 0 && v > limit + (if (mph) 2 else 3)
        fun ang(value: Float) = 200f - 220f * (value / max).coerceIn(0f, 1.03f)

        // background glow
        fill.shader = RadialGradient(640f, 446f, 780f, Color.rgb(12, 28, 38), Color.rgb(3, 6, 10), Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, DW, DH, fill)
        fill.shader = null

        // amber glow beside the dial centre: bright while over the speed limit
        val a = if (over) 120 else 22
        for (gx in floatArrayOf(480f, DW - 480f)) {
            fill.shader = RadialGradient(gx, 370f, 120f, Color.argb(a, 255, 150, 30), Color.argb(0, 255, 150, 30), Shader.TileMode.CLAMP)
            c.drawCircle(gx, 370f, 120f, fill)
            fill.shader = null
        }

        // dial ring
        arc(c, R + 16, 204f, -24f, Color.rgb(20, 34, 44), 8f)

        // fuel (left) and AdBlue (right) gauges hugging the dial
        gauge(c, 163.5f, 200f, dispFuel, if (t.fuelWarn) RED else GREEN, true)
        gauge(c, 16.5f, -20f, dispAdBlue, if (t.adBlueWarn) RED else BLUE, false)

        // ticks and numbers
        val labelFrom = if (mph) 40 else 60
        var i = 0
        while (i <= max.toInt()) {
            val major = i % 10 == 0
            val an = ang(i.toFloat())
            val lit = i <= v
            line(c, p(an, R - (if (major) 38f else 20f)), p(an, R), if (lit) WHITE else GREY, if (major) 3.6f else 2f)
            if (major && i >= labelFrom) { val q = p(an, R - 72f); text(c, i.toString(), q[0], q[1], 30f, if (lit) WHITE else DIM) }
            i += 2
        }

        // cruise-control setting and speed limit markers
        if (t.cruiseOn && t.cruiseKmh > 1f) { val ca = ang(spd(t.cruiseKmh)); line(c, p(ca, R - 46f), p(ca, R + 10f), GREEN, 6f, true) }
        if (limit > 0 && limit <= max) {
            val la = ang(limit)
            val tip = p(la, R + 2f); val l1 = p(la - 1.6f, R + 24f); val l2 = p(la + 1.6f, R + 24f)
            path.reset(); path.moveTo(tip[0], tip[1]); path.lineTo(l1[0], l1[1]); path.lineTo(l2[0], l2[1]); path.close()
            fill.shader = null; fill.color = if (over) RED else AMBER
            c.drawPath(path, fill)
        }

        // needle
        val na = ang(v)
        line(c, p(na, R - 150f), p(na, R + 2f), Color.argb(70, 255, 59, 48), 16f, true)
        line(c, p(na, R - 150f), p(na, R + 2f), RED, 6f, true)

        // speed
        val speedText = num(max(0f, v))
        val wNum = text(c, speedText, 640f, 268f, 118f, if (over) AMBER else WHITE, 1)
        text(c, spdUnit, 640f + wNum / 2 + 8f, 308f, 26f, DIM, 0, true)

        // make
        if (showMake && t.make.isNotEmpty()) {
            val name = t.make.uppercase().toCharArray().joinToString(" ")
            text(c, "‹", 640f - 170f, 372f, 30f, DIM, 1, true); text(c, name, 640f, 372f, 26f, Color.rgb(176, 184, 192), 1, true); text(c, "›", 640f + 170f, 372f, 30f, DIM, 1, true)
        }

        // gear
        text(c, gearText(t), 640f, 430f, 46f, if (t.gear < 0) AMBER else WHITE, 1)

        revs(c)
        panels(c)
        lamps(c)

        if (updateText.isNotEmpty()) {
            tp.typeface = bold; tp.textSize = 21f
            val tw = tp.measureText(updateText)
            roundRect(c, 640f - tw / 2 - 18f, 556f, 640f + tw / 2 + 18f, 588f, 8f, Color.rgb(30, 22, 8), AMBER, 1.6f)
            text(c, updateText, 640f, 572f, 21f, AMBER, 1)
        }

        if (statusText.isNotEmpty()) {
            fill.shader = null; fill.color = Color.argb(150, 3, 6, 10)
            c.drawRect(0f, 0f, DW, DH, fill)
            text(c, statusText, 640f, 330f, if (statusText.length > 40) 34f else 44f, DIM, 1, true)
        }
    }

    private fun gearText(t: Truck): String {
        val prefix = if (t.shifter == "automatic" || t.shifter == "arcade") "A " else ""
        return when {
            t.gear > 0 -> prefix + t.gear
            t.gear < 0 -> "R" + (if (t.gear < -1) abs(t.gear).toString() else "")
            else -> prefix + "N"
        }
    }

    private fun gauge(c: Canvas, a0: Float, a1: Float, fraction: Float, color: Int, left: Boolean) {
        val r = R + 56f
        arc(c, r, a0, a1, Color.rgb(24, 36, 46), 12f)
        val fa = a1 + (a0 - a1) * fraction.coerceIn(0f, 1f)   // fills from the bottom end (a1) upwards
        if (fraction > 0.004f) arc(c, r, a1, fa, color, 12f)
        val top = p(a0, r + 40f); val mid = p((a0 + a1) / 2, r + 36f); val bot = p(a1, r + 34f)
        text(c, if (left) "DIESEL" else "ADBLUE", top[0], top[1] - 40f, 16f, DIM, 1, true)
        text(c, num(fraction * 100f) + "%", top[0], top[1] - 14f, 28f, color, 1)
        text(c, "1/2", mid[0], mid[1], 18f, DIM, 1, true)
        text(c, "E", bot[0], bot[1], 18f, DIM, 1, true)
    }

    private fun revs(c: Canvas) {
        val rmax = if (truck.rpmMax > 0) max(2500f, ceil(truck.rpmMax / 500f) * 500f) else 2500f
        val x0 = 440f; val x1 = 840f; val y = 514f
        fun xr(rpm: Float) = x0 + (x1 - x0) * (rpm / rmax).coerceIn(0f, 1f)
        line(c, x0, y, x1, y, GREY, 2f)
        var rpm = 0
        while (rpm <= rmax) {
            val major = rpm % 500 == 0 && rpm > 0
            line(c, xr(rpm.toFloat()), y, xr(rpm.toFloat()), y - (if (major) 16f else 8f), if (major) WHITE else GREY, if (major) 2.6f else 1.6f)
            if (major) text(c, (rpm / 100).toString(), xr(rpm.toFloat()), y - 34f, 24f, DIM)
            rpm += 100
        }
        val high = truck.rpmMax > 0 && dispRpm > truck.rpmMax * 0.92f
        val mx = xr(dispRpm)
        fill.shader = null; fill.color = Color.argb(70, 255, 59, 48)
        c.drawRect(mx - 6f, y - 24f, mx + 6f, y + 14f, fill)
        fill.color = if (high) AMBER else RED
        c.drawRect(mx - 2.5f, y - 22f, mx + 2.5f, y + 12f, fill)
        text(c, "rpm x100", 640f, y + 26f, 16f, DIM, 1, true)
    }

    private fun panels(c: Canvas) {
        val t = truck
        // top corners: coolant and oil temperature
        text(c, "COOLANT", 50f, 40f, 17f, DIM, 0, true)
        text(c, if (t.waterC > 0) num(t.waterC) + "°C" else "--", 50f, 72f, 34f, if (t.waterWarn) RED else WHITE, 0)
        text(c, "OIL", DW - 50f, 40f, 17f, DIM, 2, true)
        text(c, if (t.oilC > 0) num(t.oilC) + "°C" else "--", DW - 50f, 72f, 34f, if (t.oilWarn) RED else WHITE, 2)

        // left: consumption, trip consumption, range
        row(c, 50f, 190f, "AVERAGE CONSUMPTION", if (t.fuelAvgLPerKm > 0) "%.1f".format(t.fuelAvgLPerKm * 100) else "--", "l/100km", WHITE, false)
        row(c, 50f, 300f, "THIS TRIP", if (tripLPer100 > 0) "%.1f".format(tripLPer100) else "--", "l/100km", WHITE, false)
        row(c, 50f, 410f, "RANGE", if (t.rangeKm > 0) num(dist(t.rangeKm)) else "--", distUnit, if (t.fuelWarn) AMBER else WHITE, false)

        // right: cruise, ETA, speed limit
        val cc = t.cruiseOn && t.cruiseKmh > 1f
        row(c, DW - 50f, 190f, "CRUISE CONTROL", if (cc) num(spd(t.cruiseKmh)) else "--", spdUnit, if (cc) GREEN else DIM, true)
        val eta = if (t.etaMin >= 0 && t.gameClockMin >= 0) clock((t.gameClockMin + t.etaMin.roundToInt()) % 1440) else "--:--"
        row(c, DW - 50f, 300f, "ARRIVAL (ETA)", eta, "", WHITE, true)
        row(c, DW - 50f, 410f, "SPEED LIMIT", if (t.speedLimit > 0) num(spd(t.speedLimit.toFloat())) else "--", spdUnit,
            if (t.speedLimit > 0 && spd(dispSpeed) > spd(t.speedLimit.toFloat()) + 3) AMBER else WHITE, true)

        // bottom row: game clock and rest, local time, trip and odometer
        line(c, 40f, 596f, DW - 40f, 596f, Color.rgb(24, 36, 46), 2f)
        text(c, "GAME TIME", 50f, 622f, 16f, DIM, 0, true); text(c, clock(t.gameClockMin), 50f, 656f, 34f, WHITE, 0)
        text(c, "REST IN", 250f, 622f, 16f, DIM, 0, true); text(c, hoursMinutes(t.restMin), 250f, 656f, 34f, if (t.restMin in 0f..30f) AMBER else WHITE, 0)
        // the device's local time, large, in the middle of the bottom row
        val cal = Calendar.getInstance()
        text(c, "LOCAL TIME", 640f, 612f, 16f, DIM, 1, true)
        text(c, "%02d:%02d".format(cal.get(Calendar.HOUR_OF_DAY), cal.get(Calendar.MINUTE)), 640f, 660f, 62f, WHITE, 1)
        text(c, "TRIP", DW - 330f, 622f, 16f, DIM, 2, true); text(c, num(dist(tripKm.toFloat())) + " " + distUnit, DW - 330f, 656f, 34f, WHITE, 2)
        text(c, "ODOMETER", DW - 50f, 622f, 16f, DIM, 2, true); text(c, num(dist(t.odoKm)) + " " + distUnit, DW - 50f, 656f, 34f, WHITE, 2)
    }

    private fun row(c: Canvas, x: Float, y: Float, caption: String, value: String, unit: String, color: Int, right: Boolean) {
        text(c, caption, x, y, 16f, DIM, if (right) 2 else 0, true)
        tp.typeface = bold; tp.textSize = 46f
        val vw = tp.measureText(value)
        tp.typeface = light; tp.textSize = 20f
        val uw = if (unit.isNotEmpty()) tp.measureText(unit) else 0f
        val total = vw + (if (unit.isNotEmpty()) uw + 8f else 0f)
        val sx = if (right) x - total else x
        text(c, value, sx, y + 30f, 46f, color, 0)
        if (unit.isNotEmpty()) text(c, unit, sx + vw + 8f, y + 40f, 20f, DIM, 0, true)
    }

    // ------------------------------------------------------------------------------------------ lamps

    private fun lamps(c: Canvas) {
        val t = truck
        val y = 50f; val x = 640f
        arrow(c, x - 96f, y, true, if (t.blinkerL) GREEN else Color.rgb(26, 40, 32))
        arrow(c, x + 96f, y, false, if (t.blinkerR) GREEN else Color.rgb(26, 40, 32))
        beam(c, x - 48f, y, false, if (t.lowBeam) GREEN else Color.rgb(26, 40, 32))
        beam(c, x + 48f, y, true, if (t.highBeam) BLUE else Color.rgb(24, 34, 46))
        val tags = ArrayList<Pair<String, Int>>()
        if (t.parkBrake) tags.add("P BRAKE" to RED)
        if (t.motorBrake) tags.add("ENGINE BRAKE" to AMBER)
        if (t.retarder > 0) tags.add("RETARDER ${t.retarder}" to AMBER)
        if (t.wipers) tags.add("WIPERS" to DIM)
        if (t.beacon) tags.add("BEACON" to AMBER)
        if (t.airWarn) tags.add("AIR" to RED)
        if (t.oilWarn) tags.add("OIL" to RED)
        if (t.waterWarn) tags.add("COOLANT" to RED)
        if (t.batteryWarn) tags.add("BATTERY" to RED)
        if (t.fuelWarn) tags.add("FUEL" to AMBER)
        if (t.adBlueWarn) tags.add("ADBLUE" to AMBER)
        if (tags.isEmpty()) return
        tp.typeface = bold; tp.textSize = 18f
        val widths = tags.map { tp.measureText(it.first) + 26f }
        val total = widths.sum() + 8f * (tags.size - 1)
        var tx = 640f - total / 2
        for ((idx, tag) in tags.withIndex()) {
            roundRect(c, tx, 88f, tx + widths[idx], 118f, 6f, Color.rgb(14, 22, 30), tag.second, 1.6f)
            text(c, tag.first, tx + widths[idx] / 2, 103f, 18f, tag.second, 1)
            tx += widths[idx] + 8f
        }
    }

    private fun arrow(c: Canvas, x: Float, y: Float, left: Boolean, color: Int) {
        val d = if (left) -1f else 1f
        path.reset()
        path.moveTo(x + d * 18, y)
        path.lineTo(x - d * 2, y - 16); path.lineTo(x - d * 2, y - 7)
        path.lineTo(x - d * 18, y - 7); path.lineTo(x - d * 18, y + 7)
        path.lineTo(x - d * 2, y + 7); path.lineTo(x - d * 2, y + 16)
        path.close()
        fill.shader = null; fill.color = color
        c.drawPath(path, fill)
    }

    private fun beam(c: Canvas, x: Float, y: Float, high: Boolean, color: Int) {
        // a "(" shape, then the light rays
        oval.set(x - 8 - 14, y - 14, x - 8 + 14, y + 14)
        stroke.shader = null; stroke.color = color; stroke.strokeWidth = 3.4f; stroke.strokeCap = Paint.Cap.ROUND
        c.drawArc(oval, -90f, -180f, false, stroke)
        for (i in -2..2) {
            val dy = if (high) i * 6f else i * 5f
            line(c, x + 2, y + dy, x + 22, y + dy + (if (high) 0f else 3f), color, 3f, true)
        }
    }
}
