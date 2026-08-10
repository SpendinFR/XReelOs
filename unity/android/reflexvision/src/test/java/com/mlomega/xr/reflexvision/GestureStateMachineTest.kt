package com.mlomega.xr.reflexvision

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Device-free tests of the gesture recogniser: hysteresis, minimum hold, the
 * continuous zoom mapping, palm hold, and swipe detection. This is the JVM half
 * of the E26 "gestures survive with PC cut" guarantee — the state machine runs
 * fully on-device with no network.
 */
class GestureStateMachineTest {

    private fun cfg() = GestureConfig(modelAssetPath = "unused")

    private fun frame(
        present: Boolean = true,
        pinch: Float = 1f,
        wristX: Float = 0.5f,
        wristY: Float = 0.5f,
        gesture: String = "",
        score: Float = 0f,
        t: Long,
    ) = GestureStateMachine.HandFrame(
        handPresent = present,
        normPinchDistance = pinch,
        wristX = wristX, wristY = wristY,
        anchorX = 0.5f, anchorY = 0.5f,
        topGesture = gesture, topGestureScore = score,
        timestampMs = t,
    )

    @Test
    fun pinch_requires_min_hold_then_begins_and_ends_with_hysteresis() {
        val sm = GestureStateMachine(cfg())
        // Cross the enter threshold but not held long enough yet.
        assertTrue(sm.onFrame(frame(pinch = 0.2f, t = 0)).isEmpty())
        // Held past minHoldMs (60) -> BEGIN.
        val begin = sm.onFrame(frame(pinch = 0.2f, t = 100))
        assertEquals(1, begin.size)
        assertEquals(GestureKind.PINCH_BEGIN, begin[0].kind)
        assertTrue(begin[0].zoomFactor > 1f)

        // Relax slightly but stay below the exit threshold (0.40): no END, stays pinched.
        val stillPinched = sm.onFrame(frame(pinch = 0.35f, t = 200))
        assertTrue(stillPinched.all { it.kind != GestureKind.PINCH_END })

        // Relax past exit threshold -> END.
        val end = sm.onFrame(frame(pinch = 0.5f, t = 300))
        assertEquals(1, end.size)
        assertEquals(GestureKind.PINCH_END, end[0].kind)
    }

    @Test
    fun pinch_zoom_is_continuous_and_monotonic() {
        val sm = GestureStateMachine(cfg())
        val tight = sm.zoomForDistance(0.06f) // fully closed -> max zoom
        val wide = sm.zoomForDistance(0.55f)  // open -> min zoom
        val mid = sm.zoomForDistance(0.30f)
        assertTrue(tight > mid)
        assertTrue(mid > wide)
        assertEquals(4.0f, tight, 1e-3f)
        assertEquals(1.0f, wide, 1e-3f)
    }

    @Test
    fun pinch_update_only_on_meaningful_change_not_every_frame() {
        val sm = GestureStateMachine(cfg())
        sm.onFrame(frame(pinch = 0.10f, t = 0))
        sm.onFrame(frame(pinch = 0.10f, t = 100)) // BEGIN
        // Tiny wobble -> no UPDATE spam.
        val noUpdate = sm.onFrame(frame(pinch = 0.105f, t = 150))
        assertTrue(noUpdate.none { it.kind == GestureKind.PINCH_UPDATE })
        // Big change -> exactly one UPDATE.
        // Stay strictly inside the exit threshold; exactly 0.40 is the defined
        // hysteresis release boundary and must emit PINCH_END instead.
        val update = sm.onFrame(frame(pinch = 0.30f, t = 200))
        assertEquals(1, update.count { it.kind == GestureKind.PINCH_UPDATE })
    }

    @Test
    fun open_palm_fires_once_after_hold_and_rearms_after_drop() {
        val sm = GestureStateMachine(cfg())
        assertTrue(sm.onFrame(frame(gesture = "Open_Palm", score = 0.9f, t = 0)).isEmpty())
        val fired = sm.onFrame(frame(gesture = "Open_Palm", score = 0.9f, t = 600)) // >550 hold
        assertEquals(1, fired.count { it.kind == GestureKind.OPEN_PALM_MENU })
        // Still palm: does not fire again.
        val again = sm.onFrame(frame(gesture = "Open_Palm", score = 0.9f, t = 900))
        assertTrue(again.none { it.kind == GestureKind.OPEN_PALM_MENU })
        // Drop, then hold again -> re-arms and fires.
        sm.onFrame(frame(gesture = "None", score = 0.9f, t = 1000))
        sm.onFrame(frame(gesture = "Open_Palm", score = 0.9f, t = 1100))
        val refired = sm.onFrame(frame(gesture = "Open_Palm", score = 0.9f, t = 1700))
        assertEquals(1, refired.count { it.kind == GestureKind.OPEN_PALM_MENU })
    }

    @Test
    fun lateral_swipe_detected_and_debounced() {
        val sm = GestureStateMachine(cfg())
        // Anchor the wrist.
        sm.onFrame(frame(wristX = 0.2f, t = 0))
        // Fast horizontal travel of 0.35 within 200 ms, minimal vertical.
        val swipe = sm.onFrame(frame(wristX = 0.55f, wristY = 0.5f, t = 200))
        assertEquals(1, swipe.count { it.kind == GestureKind.SWIPE_HIDE })
        // Immediate second swipe is swallowed by the cooldown.
        val cooled = sm.onFrame(frame(wristX = 0.9f, t = 250))
        assertTrue(cooled.none { it.kind == GestureKind.SWIPE_HIDE })
    }

    @Test
    fun vertical_gesture_is_not_a_lateral_swipe() {
        val sm = GestureStateMachine(cfg())
        sm.onFrame(frame(wristX = 0.5f, wristY = 0.2f, t = 0))
        // Mostly vertical travel -> rejected.
        val notSwipe = sm.onFrame(frame(wristX = 0.55f, wristY = 0.6f, t = 200))
        assertTrue(notSwipe.none { it.kind == GestureKind.SWIPE_HIDE })
    }

    @Test
    fun hand_lost_ends_an_active_pinch() {
        val sm = GestureStateMachine(cfg())
        sm.onFrame(frame(pinch = 0.1f, t = 0))
        sm.onFrame(frame(pinch = 0.1f, t = 100)) // BEGIN
        val lost = sm.onFrame(frame(present = false, t = 150))
        assertEquals(1, lost.count { it.kind == GestureKind.PINCH_END })
    }
}
