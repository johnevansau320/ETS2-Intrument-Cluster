package com.ets2cluster

import org.junit.Assert.*
import org.junit.Test

class TelemetryTest {
    @Test fun gameDateMinutes() {
        assertEquals(-1f, TelemetryParser.minutes("0001-01-01T00:00:00Z"), 0f)
        assertEquals(-1f, TelemetryParser.minutes(""), 0f)
        assertEquals(6 * 60f + 50f, TelemetryParser.minutes("0001-01-01T06:50:00Z"), 0f)
        assertEquals(24 * 60f + 60f, TelemetryParser.minutes("0001-01-02T01:00:00Z"), 0f)
    }

    @Test fun parsesTelemetry() {
        val json = """{"game":{"connected":true,"time":"0001-01-03T06:50:00Z","nextRestStopTime":"0001-01-01T08:12:00Z"},
            "truck":{"make":"Volvo","speed":87.5,"engineRpm":1180,"engineRpmMax":2200,"displayedGear":12,"cruiseControlOn":true,"cruiseControlSpeed":90,
            "fuel":1110,"fuelCapacity":1500,"adblue":30,"adblueCapacity":60,"blinkerRightOn":true},
            "navigation":{"speedLimit":90,"estimatedTime":"0001-01-01T01:12:00Z"}}"""
        val t = TelemetryParser.parse(json)
        assertTrue(t.connected); assertEquals("Volvo", t.make); assertEquals(87.5f, t.speedKmh, 0f); assertEquals(90, t.speedLimit)
        assertEquals(12, t.gear); assertTrue(t.cruiseOn); assertTrue(t.blinkerR); assertFalse(t.blinkerL)
        assertEquals(6 * 60 + 50, t.gameClockMin); assertEquals(72f, t.etaMin, 0f); assertEquals(492f, t.restMin, 0f)
        assertEquals(0.74f, t.fuelFraction, 0.001f); assertEquals(0.5f, t.adBlueFraction, 0.001f)
    }

    @Test fun emptyJsonIsDisconnected() {
        val t = TelemetryParser.parse("{}")
        assertFalse(t.connected); assertEquals(-1, t.gameClockMin); assertEquals(-1f, t.etaMin, 0f)
    }

    @Test fun signatureChangesWithSpeed() {
        val a = TelemetryParser.demo(); val b = TelemetryParser.demo("over")
        assertNotEquals(a.signature(), b.signature())
    }
}
