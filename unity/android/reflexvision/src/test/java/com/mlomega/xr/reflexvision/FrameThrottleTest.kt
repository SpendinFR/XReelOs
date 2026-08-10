package com.mlomega.xr.reflexvision

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Device-free tests of the E47-B frame-rate gate. The Unity capture texture
 * arrives at up to 30 fps; the gesture graph only needs 10–15 fps, so
 * [FrameThrottle] drops the surplus. These prove the drop policy independently of
 * Unity so the on-device cadence is trustworthy without a phone.
 */
class FrameThrottleTest {

    @Test
    fun first_frame_is_always_accepted() {
        val t = FrameThrottle(minIntervalMs = 83L)
        assertTrue(t.accept(1_000L))
    }

    @Test
    fun frames_arriving_too_soon_are_dropped() {
        val t = FrameThrottle(minIntervalMs = 83L)
        assertTrue(t.accept(0L))          // accepted
        assertFalse(t.accept(40L))        // +40 ms < 83 -> dropped
        assertFalse(t.accept(82L))        // +82 ms < 83 -> dropped
        assertTrue(t.accept(83L))         // exactly the interval -> accepted
    }

    @Test
    fun drops_do_not_advance_the_reference_frame() {
        // A stream of dropped frames must not push the accept window forward, or a
        // slow trickle just under the interval would starve the pipeline entirely.
        val t = FrameThrottle(minIntervalMs = 100L)
        assertTrue(t.accept(0L))
        assertFalse(t.accept(50L))        // dropped, reference stays at 0
        assertFalse(t.accept(90L))        // dropped, reference still 0
        assertTrue(t.accept(100L))        // 100 - 0 >= 100 -> accepted
    }

    @Test
    fun out_of_order_and_duplicate_timestamps_are_dropped() {
        val t = FrameThrottle(minIntervalMs = 50L)
        assertTrue(t.accept(1_000L))
        assertFalse(t.accept(1_000L))     // duplicate -> dropped
        assertFalse(t.accept(900L))       // earlier -> dropped
        assertTrue(t.accept(1_060L))      // forward past the interval -> accepted
    }

    @Test
    fun reset_makes_the_next_frame_accepted_unconditionally() {
        val t = FrameThrottle(minIntervalMs = 100L)
        assertTrue(t.accept(0L))
        assertFalse(t.accept(10L))        // throttled
        t.reset()                         // e.g. pipeline stop/start
        assertTrue(t.accept(11L))         // first frame after reset -> accepted
    }

    @Test
    fun steady_stream_yields_roughly_the_target_cadence() {
        // Feed a 30 fps stream (33 ms apart) through a 12 fps gate (83 ms) and
        // count how many survive over one second: expect 12–13, never ~30.
        val t = FrameThrottle.forTargetFps(12f)
        var accepted = 0
        var ts = 0L
        while (ts <= 1_000L) {
            if (t.accept(ts)) accepted++
            ts += 33L
        }
        assertTrue("expected ~12 fps, got $accepted", accepted in 11..14)
    }

    @Test
    fun target_fps_is_clamped_to_the_gesture_band() {
        // The derived interval is clamped to the 10–15 fps band regardless of the
        // requested rate: too-high requests never go below the 15 fps interval
        // (66 ms) and too-low requests never exceed the 10 fps interval (100 ms).
        assertTrue(FrameThrottle.forTargetFps(60f).accept(0L))
        assertFalse("60 fps must still be gated at 15 fps (66 ms)",
            FrameThrottle.forTargetFps(60f).also { it.accept(0L) }.accept(65L))

        val slow = FrameThrottle.forTargetFps(1f)
        assertTrue(slow.accept(0L))
        assertFalse("1 fps must clamp up to 10 fps (100 ms) — 90 ms is dropped",
            slow.accept(90L))
        assertTrue("1 fps clamped to 10 fps accepts at 100 ms", slow.accept(100L))
    }

    @Test
    fun atelier_can_raise_its_scoped_ceiling_without_changing_product_cap() {
        val product = FrameThrottle.forTargetFps(20f)
        assertTrue(product.accept(0L))
        assertFalse(product.accept(50L))

        val atelier = FrameThrottle.forTargetFps(20f, 20f)
        assertTrue(atelier.accept(0L))
        assertTrue(atelier.accept(50L))

        val fasterAtelier = FrameThrottle.forTargetFps(25f, 25f)
        assertTrue(fasterAtelier.accept(0L))
        assertTrue(fasterAtelier.accept(40L))
    }

    @Test
    fun non_positive_target_fps_falls_back_to_default() {
        val t = FrameThrottle.forTargetFps(0f)
        assertTrue(t.accept(0L))
        // Default interval is 83 ms — a frame 40 ms later is still dropped.
        assertFalse(t.accept(40L))
        assertTrue(t.accept(FrameThrottle.DEFAULT_MIN_INTERVAL_MS))
    }

    @Test
    fun forTargetFps_interval_matches_the_requested_rate() {
        // 12 fps -> 83 ms; the gate accepts at 83 but not at 82.
        val t = FrameThrottle.forTargetFps(12f)
        assertTrue(t.accept(0L))
        assertFalse(t.accept(82L))
        assertTrue(t.accept(83L))
        assertEquals(83L, 1000L / 12L)    // documents the derivation
    }
}
