package com.mlomega.xr.reflexvision

/**
 * MLOmega V19 — E47-B.
 *
 * Pure, device-free frame-rate gate for the gesture pipeline. The Unity capture
 * loop (E23 `EyeCaptureSource`) publishes the eye/phone texture at the WebRTC
 * cadence (up to 30 fps at full resolution). Gestures do **not** need that: a
 * pinch/palm/swipe resolves fine at a downscaled 10–15 fps, and running the
 * MediaPipe graph at capture rate would burn battery for no benefit
 * (GUIDE_V19_REFERENCE §9.4 — never a detector hotter than it must be).
 *
 * This gate is the authoritative throttle: [GesturePipeline.pushFrame] consults
 * it, so the drop policy is identical whether frames arrive from the Unity bridge
 * or a JVM test harness, and it is exhaustively unit-testable off-device
 * ([FrameThrottleTest]).
 *
 * The policy is a simple minimum-interval gate on the monotonic frame timestamp:
 * the first frame is always accepted, then a frame is accepted only once at least
 * [minIntervalMs] has elapsed since the last accepted frame. Out-of-order or
 * duplicate timestamps (dt <= 0) are dropped — MediaPipe LIVE_STREAM rejects
 * non-monotonic timestamps anyway, so dropping them here is both correct and
 * cheaper.
 *
 * @property minIntervalMs Minimum gap (ms) between accepted frames. Derived from a
 *   target fps via [forTargetFps]; defaults to ~12 fps (83 ms).
 */
class FrameThrottle(private val minIntervalMs: Long = DEFAULT_MIN_INTERVAL_MS) {

    private var lastAcceptedMs = Long.MIN_VALUE

    /**
     * @return true if a frame stamped [timestampMs] should be processed, false if
     *   it should be dropped (arrived too soon after the last accepted frame, or
     *   is not strictly newer than it).
     */
    fun accept(timestampMs: Long): Boolean {
        if (lastAcceptedMs == Long.MIN_VALUE) {
            lastAcceptedMs = timestampMs
            return true
        }
        val dt = timestampMs - lastAcceptedMs
        if (dt <= 0L) return false // out-of-order / duplicate — drop.
        if (dt < minIntervalMs) return false // too soon — throttled.
        lastAcceptedMs = timestampMs
        return true
    }

    /** Reset the gate so the next frame is accepted unconditionally (on start/stop). */
    fun reset() {
        lastAcceptedMs = Long.MIN_VALUE
    }

    companion object {
        /** ~12 fps — comfortably inside the 10–15 fps gesture band. */
        const val DEFAULT_MIN_INTERVAL_MS = 83L

        /**
         * Build a throttle for a target frame rate. Clamped to the 10–15 fps
         * gesture band (GUIDE_V19_REFERENCE §9.4): faster wastes battery, slower
         * makes swipes feel laggy. fps <= 0 falls back to the default.
         */
        @JvmStatic
        fun forTargetFps(fps: Float): FrameThrottle {
            return forTargetFps(fps, 15f)
        }

        /**
         * Scoped higher ceiling for short-lived, device-proven tools such as the
         * World Atelier. Product callers keep using [forTargetFps] and therefore
         * remain capped at 15 fps.
         */
        @JvmStatic
        fun forTargetFps(fps: Float, maxFps: Float): FrameThrottle {
            if (fps <= 0f) return FrameThrottle()
            val ceiling = maxFps.coerceIn(10f, 30f)
            val clamped = fps.coerceIn(10f, ceiling)
            return FrameThrottle((1000f / clamped).toLong())
        }
    }
}
