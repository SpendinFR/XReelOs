package com.mlomega.xr.reflexvision

import kotlin.math.abs
import kotlin.math.hypot

/**
 * Pure, device-free gesture recogniser. Consumes per-frame hand observations
 * (landmarks already extracted by MediaPipe) plus the recognizer's top gesture
 * category, and emits discrete [GestureKind] events with hysteresis + minimum
 * hold debouncing (handoff §7 anti-false-positive). Kept free of any Android /
 * MediaPipe type so it is exhaustively unit-testable on the JVM
 * ([GestureStateMachineTest]).
 *
 * All thresholds come from the injected [GestureConfig]; there are no magic
 * numbers in the logic.
 */
class GestureStateMachine(private val config: GestureConfig) {

    /** A single emitted gesture event (mirrors the [GestureCallbacks.onGesture] shape). */
    data class Event(
        val kind: GestureKind,
        val zoomFactor: Float,
        val screenX: Float,
        val screenY: Float,
        val timestampMs: Long,
    )

    /**
     * One frame of hand observation. [normPinchDistance] is thumb-tip↔index-tip
     * distance divided by hand span (scale-invariant); [wristX]/[wristY] are the
     * normalised wrist position (for swipe travel); [anchorX]/[anchorY] are the
     * pinch/palm centre for UI anchoring; [topGesture] is the MediaPipe category
     * name ("Open_Palm", "Closed_Fist", ...) with [topGestureScore]. When no hand
     * is present, pass [handPresent] = false.
     */
    data class HandFrame(
        val handPresent: Boolean,
        val normPinchDistance: Float,
        val wristX: Float,
        val wristY: Float,
        val anchorX: Float,
        val anchorY: Float,
        val topGesture: String,
        val topGestureScore: Float,
        val timestampMs: Long,
    )

    // --- pinch state ---
    private var pinching = false
    private var pinchCandidateSinceMs = -1L
    private var lastEmittedZoom = Float.NaN

    // --- palm state ---
    private var palmSinceMs = -1L
    private var palmFired = false

    // --- swipe state ---
    private var swipeAnchorX = Float.NaN
    private var swipeAnchorSinceMs = -1L
    private var lastSwipeMs = -1L

    /**
     * Feed one frame. Returns 0..N events (usually 0 or 1). Deterministic: the
     * same frame sequence always yields the same events, which is what makes the
     * JVM test meaningful.
     */
    fun onFrame(f: HandFrame): List<Event> {
        if (!f.handPresent) {
            return handleHandLost(f.timestampMs)
        }
        val events = ArrayList<Event>(2)
        evaluatePinch(f, events)
        evaluatePalm(f, events)
        evaluateSwipe(f, events)
        return events
    }

    private fun handleHandLost(nowMs: Long): List<Event> {
        val events = ArrayList<Event>(1)
        if (pinching) {
            pinching = false
            pinchCandidateSinceMs = -1L
            lastEmittedZoom = Float.NaN
            events.add(Event(GestureKind.PINCH_END, 1.0f, -1f, -1f, nowMs))
        }
        palmSinceMs = -1L
        palmFired = false
        swipeAnchorX = Float.NaN
        swipeAnchorSinceMs = -1L
        return events
    }

    // ----------------------------------------------------------------------
    //  Pinch → continuous zoom (begin / update / end) with hysteresis
    // ----------------------------------------------------------------------

    private fun evaluatePinch(f: HandFrame, out: MutableList<Event>) {
        val p = config.pinch
        val d = f.normPinchDistance
        if (!pinching) {
            if (d <= p.enterNormalizedDistance) {
                if (pinchCandidateSinceMs < 0L) pinchCandidateSinceMs = f.timestampMs
                if (f.timestampMs - pinchCandidateSinceMs >= p.minHoldMs) {
                    pinching = true
                    val zoom = zoomForDistance(d)
                    lastEmittedZoom = zoom
                    out.add(Event(GestureKind.PINCH_BEGIN, zoom, f.anchorX, f.anchorY, f.timestampMs))
                }
            } else {
                pinchCandidateSinceMs = -1L
            }
        } else {
            // Only release once the distance relaxes past the (larger) exit
            // threshold — hysteresis prevents flicker around the boundary.
            if (d >= p.exitNormalizedDistance) {
                pinching = false
                pinchCandidateSinceMs = -1L
                lastEmittedZoom = Float.NaN
                out.add(Event(GestureKind.PINCH_END, 1.0f, f.anchorX, f.anchorY, f.timestampMs))
            } else {
                val zoom = zoomForDistance(d)
                // Only emit an UPDATE when the zoom changed meaningfully — never
                // one event per frame (aggregation rule).
                if (java.lang.Float.isNaN(lastEmittedZoom) ||
                    abs(zoom - lastEmittedZoom) >= ZOOM_UPDATE_EPSILON
                ) {
                    lastEmittedZoom = zoom
                    out.add(Event(GestureKind.PINCH_UPDATE, zoom, f.anchorX, f.anchorY, f.timestampMs))
                }
            }
        }
    }

    /** Map a normalised pinch distance to a clamped, linear continuous zoom factor. */
    fun zoomForDistance(normDistance: Float): Float {
        val p = config.pinch
        val lo = p.closedNormalizedDistance
        val hi = p.openNormalizedDistance
        val t = ((normDistance - lo) / (hi - lo)).coerceIn(0f, 1f)
        // t=0 (fully closed) -> zoomAtMinDistance; t=1 (open) -> zoomAtMaxDistance.
        return p.zoomAtMinDistance + t * (p.zoomAtMaxDistance - p.zoomAtMinDistance)
    }

    // ----------------------------------------------------------------------
    //  Open palm held → menu
    // ----------------------------------------------------------------------

    private fun evaluatePalm(f: HandFrame, out: MutableList<Event>) {
        val palm = config.palm
        val isPalm = f.topGesture == OPEN_PALM_CATEGORY && f.topGestureScore >= palm.minScore
        if (isPalm) {
            if (palmSinceMs < 0L) palmSinceMs = f.timestampMs
            if (!palmFired && f.timestampMs - palmSinceMs >= palm.minHoldMs) {
                palmFired = true
                out.add(Event(GestureKind.OPEN_PALM_MENU, 0f, f.anchorX, f.anchorY, f.timestampMs))
            }
        } else {
            palmSinceMs = -1L
            palmFired = false
        }
    }

    // ----------------------------------------------------------------------
    //  Lateral swipe → hide UI
    // ----------------------------------------------------------------------

    private fun evaluateSwipe(f: HandFrame, out: MutableList<Event>) {
        val s = config.swipe
        if (java.lang.Float.isNaN(swipeAnchorX)) {
            swipeAnchorX = f.wristX
            swipeAnchorSinceMs = f.timestampMs
            swipeStartY = f.wristY
            return
        }
        val dx = f.wristX - swipeAnchorX
        val dy = f.wristY - swipeStartY
        val dt = f.timestampMs - swipeAnchorSinceMs

        // Restart the window if it expired without a swipe.
        if (dt > s.maxDurationMs) {
            swipeAnchorX = f.wristX
            swipeAnchorSinceMs = f.timestampMs
            swipeStartY = f.wristY
            return
        }
        val travel = abs(dx)
        val speed = if (dt > 0) travel / (dt / 1000f) else 0f
        val verticalRatio = if (travel > 0f) abs(dy) / travel else Float.MAX_VALUE
        val cooledDown = lastSwipeMs < 0L || f.timestampMs - lastSwipeMs >= s.cooldownMs

        if (travel >= s.minTravelNormalized &&
            speed >= s.minSpeedNormalizedPerSec &&
            verticalRatio <= s.maxVerticalRatio &&
            cooledDown
        ) {
            lastSwipeMs = f.timestampMs
            swipeAnchorX = f.wristX
            swipeAnchorSinceMs = f.timestampMs
            swipeStartY = f.wristY
            out.add(Event(GestureKind.SWIPE_HIDE, 0f, f.wristX, f.wristY, f.timestampMs))
        }
    }

    private var swipeStartY = Float.NaN

    /** Euclidean helper (exposed for the JVM test that builds synthetic landmarks). */
    fun distance(ax: Float, ay: Float, bx: Float, by: Float): Float = hypot(ax - bx, ay - by)

    companion object {
        const val OPEN_PALM_CATEGORY = "Open_Palm"
        /** Minimum change in zoom factor before emitting a PINCH_UPDATE. */
        const val ZOOM_UPDATE_EPSILON = 0.08f
    }
}
