package com.mlomega.xr.reflexvision

import android.content.Context
import android.graphics.Bitmap
import com.google.mediapipe.framework.image.BitmapImageBuilder
import com.google.mediapipe.framework.image.MPImage
import com.google.mediapipe.tasks.core.BaseOptions
import com.google.mediapipe.tasks.core.Delegate
import com.google.mediapipe.tasks.vision.core.RunningMode
import com.google.mediapipe.tasks.vision.gesturerecognizer.GestureRecognizer
import com.google.mediapipe.tasks.vision.gesturerecognizer.GestureRecognizerResult
import java.util.concurrent.atomic.AtomicBoolean

/**
 * MLOmega V19 on-device gesture pipeline (E26).
 *
 * Runs the MediaPipe Tasks Vision [GestureRecognizer] in
 * [RunningMode.LIVE_STREAM] over the camera frames. The recognizer bundles the
 * HandLandmarker, so a single graph gives us both the hand landmarks (used for
 * the continuous pinch-zoom distance and the swipe travel) and the discrete
 * gesture category ("Open_Palm" etc.). Results are fed into the pure
 * [GestureStateMachine], which applies hysteresis + minimum-hold debouncing and
 * emits the three product gestures:
 *   * pinch → continuous zoom factor (begin/update/end),
 *   * open palm held → menu,
 *   * lateral swipe → hide UI.
 *
 * **NO LLM / NO VLM (handoff §3.2):** this is a small specialised on-device
 * calculator, well under the 100 ms reflex budget.
 *
 * **On-demand only (GUIDE_V19_REFERENCE §9.4):** the recognizer graph is created
 * on [start] and torn down on [stop]; the Unity `ReflexScheduler` calls these so
 * the detector is never resident when no signal requires gestures (battery). It
 * is safe to [stop]/[start] repeatedly.
 *
 * Threading: [pushFrame] is called from the capture thread; MediaPipe delivers
 * results on its own callback thread, from which [GestureCallbacks] fire. The
 * Unity bridge marshals callbacks onto the main thread.
 *
 * This module cannot be compiled in the authoring environment (no Android SDK);
 * it is written against the pinned MediaPipe API (see build.gradle.kts) and the
 * real compile/run belongs to the S25 validation gate (ADR docs/DECISIONS §E26).
 */
class GesturePipeline(
    private val appContext: Context,
    private val config: GestureConfig,
    private val callbacks: GestureCallbacks,
) {
    private val running = AtomicBoolean(false)
    private val stateMachine = GestureStateMachine(config)

    // Authoritative frame-rate gate (E47-B): the Unity capture texture arrives at
    // the WebRTC cadence; gestures only need 10–15 fps (battery — §9.4). Dropping
    // here rather than in the Unity bridge keeps the policy testable on the JVM and
    // identical across capture sources.
    private val throttle = FrameThrottle.forTargetFps(config.targetFps)

    @Volatile
    private var recognizer: GestureRecognizer? = null

    /**
     * Create the recognizer graph and begin accepting frames. No-op if already
     * running. Called by the Unity ReflexScheduler when a gesture-relevant signal
     * is active (hand + near object, or an explicit lens/zoom focus).
     */
    fun start() {
        if (!running.compareAndSet(false, true)) return
        throttle.reset()
        try {
            val base = BaseOptions.builder()
                .setModelAssetPath(config.modelAssetPath)
                // GPU delegate keeps the graph off the CPU hot path; falls back to
                // CPU automatically if unavailable on the device.
                .setDelegate(Delegate.GPU)
                .build()

            val options = GestureRecognizer.GestureRecognizerOptions.builder()
                .setBaseOptions(base)
                .setRunningMode(RunningMode.LIVE_STREAM)
                .setNumHands(config.numHands)
                .setMinHandDetectionConfidence(config.minHandDetectionConfidence)
                .setMinHandPresenceConfidence(config.minHandPresenceConfidence)
                .setMinTrackingConfidence(config.minTrackingConfidence)
                .setResultListener { result, _ -> onResult(result) }
                .setErrorListener { e -> callbacks.onError("gesture: ${e.message}") }
                .build()

            recognizer = GestureRecognizer.createFromOptions(appContext, options)
        } catch (t: Throwable) {
            running.set(false)
            callbacks.onError("gesture start failed: ${t.message}")
        }
    }

    /** Tear down the recognizer graph (battery — §9.4). No-op if not running. */
    fun stop() {
        if (!running.compareAndSet(true, false)) return
        try {
            recognizer?.close()
        } catch (t: Throwable) {
            callbacks.onError("gesture stop failed: ${t.message}")
        } finally {
            recognizer = null
        }
    }

    fun isRunning(): Boolean = running.get()

    /**
     * Submit one camera frame for LIVE_STREAM recognition. [timestampMs] must be
     * monotonically increasing (MediaPipe rejects out-of-order timestamps). The
     * [bitmap] is the current camera texture read back to an ARGB_8888 bitmap by
     * the caller (or the Unity capture path). Dropped silently if not running.
     */
    fun pushFrame(bitmap: Bitmap, timestampMs: Long) {
        val r = recognizer ?: return
        // Drop frames above the gesture cadence (10–15 fps) — battery, §9.4.
        if (!throttle.accept(timestampMs)) return
        val mp: MPImage = BitmapImageBuilder(bitmap).build()
        try {
            r.recognizeAsync(mp, timestampMs)
        } catch (t: Throwable) {
            callbacks.onError("gesture frame failed: ${t.message}")
        }
    }

    // ----------------------------------------------------------------------
    //  MediaPipe result → pure state machine → callbacks
    // ----------------------------------------------------------------------

    private fun onResult(result: GestureRecognizerResult) {
        val frame = toHandFrame(result)
        val events = stateMachine.onFrame(frame)
        for (e in events) {
            callbacks.onGesture(e.kind, e.zoomFactor, e.screenX, e.screenY, e.timestampMs)
        }
    }

    /**
     * Convert a MediaPipe result into the pure [GestureStateMachine.HandFrame].
     * Landmark indices follow the MediaPipe hand model:
     *   0 = wrist, 4 = thumb tip, 8 = index tip, 9 = middle-finger MCP.
     * The pinch distance is thumb-tip↔index-tip normalised by the hand span
     * (wrist↔middle-MCP) so it is invariant to the hand's distance from camera.
     */
    private fun toHandFrame(result: GestureRecognizerResult): GestureStateMachine.HandFrame {
        val ts = result.timestampMs()
        val landmarks = result.landmarks()
        if (landmarks.isEmpty() || landmarks[0].size <= MIDDLE_MCP) {
            return GestureStateMachine.HandFrame(
                handPresent = false,
                normPinchDistance = 1f,
                wristX = -1f, wristY = -1f,
                anchorX = -1f, anchorY = -1f,
                topGesture = "", topGestureScore = 0f,
                timestampMs = ts,
            )
        }
        val hand = landmarks[0]
        val wrist = hand[WRIST]
        val thumb = hand[THUMB_TIP]
        val index = hand[INDEX_TIP]
        val middleMcp = hand[MIDDLE_MCP]

        val span = stateMachine.distance(wrist.x(), wrist.y(), middleMcp.x(), middleMcp.y())
        val raw = stateMachine.distance(thumb.x(), thumb.y(), index.x(), index.y())
        val norm = if (span > 1e-4f) raw / span else raw

        val anchorX = (thumb.x() + index.x()) * 0.5f
        val anchorY = (thumb.y() + index.y()) * 0.5f

        var topName = ""
        var topScore = 0f
        val gestures = result.gestures()
        if (gestures.isNotEmpty() && gestures[0].isNotEmpty()) {
            val top = gestures[0][0]
            topName = top.categoryName()
            topScore = top.score()
        }

        return GestureStateMachine.HandFrame(
            handPresent = true,
            normPinchDistance = norm,
            wristX = wrist.x(), wristY = wrist.y(),
            anchorX = anchorX, anchorY = anchorY,
            topGesture = topName, topGestureScore = topScore,
            timestampMs = ts,
        )
    }

    companion object {
        private const val WRIST = 0
        private const val THUMB_TIP = 4
        private const val INDEX_TIP = 8
        private const val MIDDLE_MCP = 9
    }
}
