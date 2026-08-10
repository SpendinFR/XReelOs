package com.mlomega.xr.reflexvision

import android.content.Context
import android.graphics.Bitmap
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.mockito.Mockito.mock

/**
 * Device-free tests of the E47-B on-demand contract: the Unity ReflexScheduler
 * activates/deactivates the pipeline (battery — §9.4), and frames must only be
 * consumed while it is active. These never create a real MediaPipe recognizer
 * (that needs a device); they exercise the lifecycle gate and the frame-drop
 * behaviour when inactive. `isReturnDefaultValues` (build.gradle.kts) stubs the
 * android.* references to their type defaults.
 */
class GesturePipelineActivationTest {

    private class RecordingCallbacks : GestureCallbacks {
        val errors = ArrayList<String>()
        var gestures = 0
        override fun onGesture(
            kind: GestureKind, zoomFactor: Float, screenX: Float, screenY: Float, timestampMs: Long,
        ) {
            gestures++
        }
        override fun onError(message: String) { errors.add(message) }
    }

    private fun pipeline(cb: GestureCallbacks): GesturePipeline =
        // A stub Context is never dereferenced on the paths under test (no start()).
        GesturePipeline(mock(Context::class.java), GestureConfig(modelAssetPath = "unused"), cb)

    private fun stubBitmap(): Bitmap = mock(Bitmap::class.java)

    @Test
    fun starts_inactive() {
        assertFalse(pipeline(RecordingCallbacks()).isRunning())
    }

    @Test
    fun pushFrame_is_a_no_op_while_inactive() {
        val cb = RecordingCallbacks()
        val p = pipeline(cb)
        // Not started: recognizer is null, so the frame is dropped before it is ever
        // wrapped — no crash, no callback, no error. This is the on-demand guarantee:
        // frames pushed by the Unity capture path do nothing until the scheduler
        // activates the pipeline.
        p.pushFrame(stubBitmap(), 0L)
        p.pushFrame(stubBitmap(), 100L)
        assertFalse(p.isRunning())
        assertTrue(cb.errors.isEmpty())
        assertTrue(cb.gestures == 0)
    }

    @Test
    fun stop_while_inactive_is_a_no_op() {
        val cb = RecordingCallbacks()
        val p = pipeline(cb)
        p.stop() // deactivate before ever activating — idempotent, no error.
        assertFalse(p.isRunning())
        assertTrue(cb.errors.isEmpty())
    }
}
