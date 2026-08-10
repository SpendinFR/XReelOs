package com.mlomega.xr.reflexvision

import android.content.Context
import android.graphics.Bitmap
import android.util.Log
import com.google.mediapipe.framework.image.BitmapImageBuilder
import com.google.mediapipe.framework.image.MPImage
import com.google.mediapipe.tasks.core.BaseOptions
import com.google.mediapipe.tasks.core.Delegate
import com.google.mediapipe.tasks.vision.core.RunningMode
import com.google.mediapipe.tasks.vision.handlandmarker.HandLandmarker
import com.google.mediapipe.tasks.vision.handlandmarker.HandLandmarkerResult
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.sqrt

/**
 * Pinch-only Eye path for XREAL One Pro + Eye.
 *
 * Deliberately separate from [GesturePipeline]: the product gesture recognizer
 * remains unchanged, while the Atelier uses the lighter HandLandmarker path
 * proven on this exact glasses/camera family by Xreal-tools. Detection follows
 * the same robust geometry: 3D distance(thumb tip,index tip) divided by
 * distance(wrist,index MCP), EMA 0.5, hysteresis 0.28/0.38 and 2/2-frame
 * asymmetric debounce. A held, fully-open palm also emits the existing
 * OPEN_PALM_MENU contract without loading the heavier GestureRecognizer.
 * Palm recognition requires a visibly spread thumb and a short post-pinch
 * cooldown, so releasing a click cannot accidentally recenter a window.
 * Apache-2.0 reference:
 * https://github.com/nudou350/Xreal-tools
 */
class EyePinchPipeline(
    private val appContext: Context,
    private val config: GestureConfig,
    private val callbacks: GestureCallbacks,
) {
    private val running = AtomicBoolean(false)
    private val throttle = FrameThrottle.forTargetFps(
        config.targetFps,
        MAX_ATELIER_FPS,
    )

    @Volatile
    private var landmarker: HandLandmarker? = null
    private var ema = Float.NaN
    private var pinched = false
    private var candidate: Boolean? = null
    private var candidateFrames = 0
    private var missingFrames = 0
    private var resultCount = 0L
    private var lastDiagnosticMs = Long.MIN_VALUE
    private var lastPinchActivityMs = Long.MIN_VALUE
    private var palmSinceMs = -1L
    private var palmFired = false
    private var twoPalmSinceMs = -1L
    private var twoPalmFired = false
    private var fistSinceMs = -1L
    private var fistFired = false
    private var indexScrollSinceMs = -1L
    private var indexScrollActive = false
    private var indexScrollOriginX = Float.NaN
    private var indexScrollOriginY = Float.NaN
    private var indexScrollFired = false
    private var lastIndexScrollPoseMs = -1L
    private var twoFingerSinceMs = -1L
    private var twoFingerFired = false
    private var thumbUpSinceMs = -1L
    private var thumbUpFired = false

    fun start() {
        if (!running.compareAndSet(false, true)) return
        throttle.reset()
        lastPinchActivityMs = Long.MIN_VALUE
        resetPinch()
        resetFist()
        resetTwoPalm()
        resetDirectGestures(false, 0L)
        try {
            val base = BaseOptions.builder()
                .setModelAssetPath(config.modelAssetPath)
                .setDelegate(Delegate.GPU)
                .build()
            val options = HandLandmarker.HandLandmarkerOptions.builder()
                .setBaseOptions(base)
                .setRunningMode(RunningMode.LIVE_STREAM)
                .setNumHands(config.numHands)
                .setMinHandDetectionConfidence(config.minHandDetectionConfidence)
                .setMinHandPresenceConfidence(config.minHandPresenceConfidence)
                .setMinTrackingConfidence(config.minTrackingConfidence)
                .setResultListener(::onResult)
                .setErrorListener { e -> callbacks.onError("eye pinch: ${e.message}") }
                .build()
            landmarker = HandLandmarker.createFromOptions(appContext, options)
            Log.i(
                TAG,
                "HandLandmarker ready (GPU/LIVE_STREAM, ${config.targetFps} fps, " +
                    "det=${config.minHandDetectionConfidence}, " +
                    "presence=${config.minHandPresenceConfidence}, " +
                    "tracking=${config.minTrackingConfidence})",
            )
        } catch (t: Throwable) {
            running.set(false)
            callbacks.onError("eye pinch start failed: ${t.message}")
        }
    }

    fun stop() {
        if (!running.compareAndSet(true, false)) return
        try {
            landmarker?.close()
        } catch (t: Throwable) {
            callbacks.onError("eye pinch stop failed: ${t.message}")
        } finally {
            landmarker = null
            resetPinch()
            resetFist()
            resetTwoPalm()
            resetDirectGestures(false, 0L)
        }
    }

    fun isRunning(): Boolean = running.get()

    fun pushFrame(bitmap: Bitmap, timestampMs: Long) {
        val tracker = landmarker ?: return
        if (!throttle.accept(timestampMs)) return
        val image: MPImage = BitmapImageBuilder(bitmap).build()
        try {
            tracker.detectAsync(image, timestampMs)
        } catch (t: Throwable) {
            image.close()
            callbacks.onError("eye pinch frame failed: ${t.message}")
        }
    }

    private fun onResult(result: HandLandmarkerResult, image: MPImage) {
        try {
            resultCount++
            val ts = result.timestampMs()
            val hands = result.landmarks()
            if (hands.isEmpty() || hands[0].size <= INDEX_TIP) {
                missingFrames++
                resetPalm()
                resetFist()
                resetTwoPalm()
                // The descending stroke is the hardest monocular view: the
                // folded fingers can briefly occlude one another at the bottom
                // of the Eye image. Preserve an already-armed index stroke for
                // one inference gap; the next valid tip pose still supplies the
                // full net displacement. Pinch is never inferred in this gap.
                if (
                    indexScrollSinceMs >= 0L &&
                    lastIndexScrollPoseMs >= 0L &&
                    ts - lastIndexScrollPoseMs <= INDEX_SCROLL_POSE_GRACE_MS
                ) {
                    logDiagnostic(ts, false, 1f, -1f, -1f)
                    return
                }
                resetDirectGestures(true, ts)
                if (pinched && missingFrames >= RELEASE_FRAMES) {
                    callbacks.onGesture(GestureKind.PINCH_END, 1f, -1f, -1f, ts)
                    resetPinch()
                }
                logDiagnostic(ts, false, 1f, -1f, -1f)
                return
            }
            missingFrames = 0
            val openHands = if (!pinched && candidate != true) {
                hands.filter { candidateHand ->
                    candidateHand.size > PINKY_TIP &&
                        isOpenPalmGeometry(candidateHand)
                }
            } else {
                emptyList()
            }
            val suppressSinglePalm = hands.size >= 2
            if (suppressSinglePalm) {
                // When both hands are visible, do not accidentally fire the
                // one-palm recenter. The dock requires two genuinely open palms.
                resetPalm()
                if (openHands.size >= 2) {
                    val left = openHands[0][WRIST]
                    val right = openHands[1][WRIST]
                    evaluateTwoPalm(
                        true,
                        (left.x() + right.x()) * .5f,
                        (left.y() + right.y()) * .5f,
                        ts,
                    )
                    resetFist()
                    resetDirectGestures(true, ts)
                    logDiagnostic(ts, true, ema, -1f, -1f)
                    return
                }
            }
            resetTwoPalm()
            val hand = hands[0]
            val thumb = hand[THUMB_TIP]
            val index = hand[INDEX_TIP]
            val wrist = hand[WRIST]
            val indexMcp = hand[INDEX_MCP]
            val scale = dist3(wrist.x(), wrist.y(), wrist.z(), indexMcp.x(), indexMcp.y(), indexMcp.z())
            val raw = dist3(thumb.x(), thumb.y(), thumb.z(), index.x(), index.y(), index.z())
            val ratio = if (scale > 1e-4f) raw / scale else raw
            ema = if (ema.isNaN()) ratio else EMA_ALPHA * ratio + (1f - EMA_ALPHA) * ema
            val x = (thumb.x() + index.x()) * .5f
            val y = (thumb.y() + index.y()) * .5f
            // A fist is defined by actual joint flexion plus a tucked thumb,
            // independently of thumb/index distance. This keeps a normal pinch
            // on the proven pinch path while allowing a genuinely tight fist.
            val closedFist = isClosedFist(hand)
            evaluateFist(closedFist, x, y, ts)
            if (closedFist) {
                resetPalm()
                resetDirectGestures(true, ts)
                if (pinched) {
                    callbacks.onGesture(GestureKind.PINCH_END, 1f, x, y, ts)
                    pinched = false
                    candidate = null
                    candidateFrames = 0
                }
                logDiagnostic(ts, true, ema, x, y)
                return
            }

            // Pinch always wins over direct poses. A click therefore cannot
            // become a point-scroll or keyboard request while thumb and index
            // are crossing the proven pinch envelope.
            val pinchLikely =
                pinched || candidate == true || ratio < PALM_PINCH_ACTIVITY_RATIO
            if (!pinchLikely) {
                val thumbUp = isThumbUp(hand)
                val twoFinger = !thumbUp && isTwoFingerKeyboardPose(hand, ratio)
                val indexOnly = !thumbUp && !twoFinger &&
                    isIndexScrollPose(hand, ratio)
                when {
                    thumbUp -> {
                        resetIndexScroll(true, ts)
                        resetTwoFinger()
                        evaluateThumbUp(true, x, y, ts)
                        evaluatePinch(ratio, x, y, ts)
                        resetPalm()
                        logDiagnostic(ts, true, ema, x, y)
                        return
                    }
                    twoFinger -> {
                        resetIndexScroll(true, ts)
                        resetThumbUp()
                        evaluateTwoFinger(true, x, y, ts)
                        evaluatePinch(ratio, x, y, ts)
                        resetPalm()
                        logDiagnostic(ts, true, ema, x, y)
                        return
                    }
                    indexOnly -> {
                        resetTwoFinger()
                        resetThumbUp()
                        lastIndexScrollPoseMs = ts
                        // The index pose only arms the gesture. Measure the
                        // actual stroke on the palm centre: lifting the index
                        // must not consume travel, and folding noise at the tip
                        // must not make one vertical direction easier than the
                        // other. The origin is captured only after the pose has
                        // remained stable for INDEX_SCROLL_HOLD_MS.
                        val wrist = hand[WRIST]
                        val middleMcp = hand[MIDDLE_MCP]
                        evaluateIndexScroll(
                            true,
                            (wrist.x() + middleMcp.x()) * .5f,
                            (wrist.y() + middleMcp.y()) * .5f,
                            ts,
                        )
                        evaluatePinch(ratio, x, y, ts)
                        resetPalm()
                        logDiagnostic(ts, true, ema, index.x(), index.y())
                        return
                    }
                }
                if (
                    indexScrollSinceMs >= 0L &&
                    lastIndexScrollPoseMs >= 0L &&
                    ts - lastIndexScrollPoseMs <= INDEX_SCROLL_POSE_GRACE_MS
                ) {
                    // Do not restart the stroke because one folded-finger test
                    // flickered. Keeping lastY also means the following valid
                    // frame measures the actual tip travel, in either direction.
                    evaluatePinch(ratio, x, y, ts)
                    resetPalm()
                    logDiagnostic(ts, true, ema, index.x(), index.y())
                    return
                }
            }
            resetDirectGestures(true, ts)
            // The EMA keeps ordinary/noisy pinches stable, but a physically very
            // deep raw pinch is already unambiguous and must not wait for the EMA
            // to decay over several inference results.
            val decisionRatio = if (!pinched && ratio <= DEEP_RAW_ENTER_THRESHOLD) {
                ratio
            } else {
                ema
            }
            evaluatePinch(decisionRatio, x, y, ts)
            if (suppressSinglePalm) {
                resetPalm()
            } else {
                evaluatePalm(isOpenPalm(hand, ts), x, y, ts)
            }
            logDiagnostic(ts, true, ema, x, y)
        } finally {
            image.close()
        }
    }

    /**
     * Rotation-independent open-palm check. Each of the four long fingers must
     * be straight at its PIP joint and extend away from the wrist. Requiring all
     * four fingers, a non-pinched hand and a timed hold avoids opening the deck
     * during an ordinary point or click.
     */
    private fun isOpenPalm(
        hand: List<com.google.mediapipe.tasks.components.containers.NormalizedLandmark>,
        ts: Long,
    ): Boolean {
        if (pinched || candidate == true || hand.size <= PINKY_TIP) return false
        if (
            lastPinchActivityMs != Long.MIN_VALUE &&
            ts - lastPinchActivityMs < PALM_AFTER_PINCH_COOLDOWN_MS
        ) return false
        return isOpenPalmGeometry(hand)
    }

    private fun isOpenPalmGeometry(
        hand: List<com.google.mediapipe.tasks.components.containers.NormalizedLandmark>,
    ): Boolean {
        if (hand.size <= PINKY_TIP) return false
        val wrist = hand[WRIST]
        val indexMcp = hand[INDEX_MCP]
        val handScale = dist3(
            wrist.x(), wrist.y(), wrist.z(),
            indexMcp.x(), indexMcp.y(), indexMcp.z(),
        )
        val thumb = hand[THUMB_TIP]
        val index = hand[INDEX_TIP]
        val thumbSpread = if (handScale > 1e-4f) {
            dist3(
                thumb.x(), thumb.y(), thumb.z(),
                index.x(), index.y(), index.z(),
            ) / handScale
        } else {
            0f
        }
        if (thumbSpread < PALM_THUMB_SPREAD_RATIO) return false

        fun extended(mcpIndex: Int, pipIndex: Int, tipIndex: Int): Boolean {
            val mcp = hand[mcpIndex]
            val pip = hand[pipIndex]
            val tip = hand[tipIndex]
            val aX = mcp.x() - pip.x()
            val aY = mcp.y() - pip.y()
            val aZ = mcp.z() - pip.z()
            val bX = tip.x() - pip.x()
            val bY = tip.y() - pip.y()
            val bZ = tip.z() - pip.z()
            val aLen = sqrt(aX * aX + aY * aY + aZ * aZ)
            val bLen = sqrt(bX * bX + bY * bY + bZ * bZ)
            if (aLen < 1e-4f || bLen < 1e-4f) return false
            val straightness = (aX * bX + aY * bY + aZ * bZ) / (aLen * bLen)
            val tipRadius = dist3(
                tip.x(), tip.y(), tip.z(), wrist.x(), wrist.y(), wrist.z(),
            )
            val pipRadius = dist3(
                pip.x(), pip.y(), pip.z(), wrist.x(), wrist.y(), wrist.z(),
            )
            return straightness <= PALM_STRAIGHT_DOT &&
                tipRadius >= pipRadius * PALM_EXTENSION_RATIO
        }

        return extended(INDEX_MCP, INDEX_PIP, INDEX_TIP) &&
            extended(MIDDLE_MCP, MIDDLE_PIP, MIDDLE_TIP) &&
            extended(RING_MCP, RING_PIP, RING_TIP) &&
            extended(PINKY_MCP, PINKY_PIP, PINKY_TIP)
    }

    private fun evaluatePalm(open: Boolean, x: Float, y: Float, ts: Long) {
        if (!open) {
            resetPalm()
            return
        }
        if (palmSinceMs < 0L) palmSinceMs = ts
        if (!palmFired && ts - palmSinceMs >= config.palm.minHoldMs) {
            palmFired = true
            callbacks.onGesture(GestureKind.OPEN_PALM_MENU, 0f, x, y, ts)
        }
    }

    private fun evaluateTwoPalm(open: Boolean, x: Float, y: Float, ts: Long) {
        if (!open) {
            resetTwoPalm()
            return
        }
        if (twoPalmSinceMs < 0L) twoPalmSinceMs = ts
        if (!twoPalmFired && ts - twoPalmSinceMs >= TWO_PALM_HOLD_MS) {
            twoPalmFired = true
            callbacks.onGesture(GestureKind.TWO_PALM_MENU, 0f, x, y, ts)
        }
    }

    /**
     * Orientation-independent fist: every long fingertip folds back toward the
     * palm, every PIP joint is visibly bent, and the thumb is tucked near the
     * palm. A pinch can bring thumb/index together, but its index joint remains
     * extended and/or its thumb stays outside this compact envelope.
     */
    private fun isClosedFist(
        hand: List<com.google.mediapipe.tasks.components.containers.NormalizedLandmark>,
    ): Boolean {
        if (hand.size <= PINKY_TIP) return false
        val wrist = hand[WRIST]
        val middleMcp = hand[MIDDLE_MCP]
        val thumb = hand[THUMB_TIP]
        val palmRadius = dist3(
            middleMcp.x(), middleMcp.y(), middleMcp.z(),
            wrist.x(), wrist.y(), wrist.z(),
        )
        val thumbRadius = dist3(
            thumb.x(), thumb.y(), thumb.z(),
            wrist.x(), wrist.y(), wrist.z(),
        )
        if (palmRadius <= 1e-4f ||
            thumbRadius > palmRadius * FIST_THUMB_TO_PALM_RATIO
        ) return false

        fun folded(mcpIndex: Int, pipIndex: Int, tipIndex: Int): Boolean {
            val mcp = hand[mcpIndex]
            val pip = hand[pipIndex]
            val tip = hand[tipIndex]
            val tipRadius = dist3(
                tip.x(), tip.y(), tip.z(), wrist.x(), wrist.y(), wrist.z(),
            )
            val pipRadius = dist3(
                pip.x(), pip.y(), pip.z(), wrist.x(), wrist.y(), wrist.z(),
            )
            val mcpRadius = dist3(
                mcp.x(), mcp.y(), mcp.z(), wrist.x(), wrist.y(), wrist.z(),
            )
            val proximalX = pip.x() - mcp.x()
            val proximalY = pip.y() - mcp.y()
            val proximalZ = pip.z() - mcp.z()
            val distalX = tip.x() - pip.x()
            val distalY = tip.y() - pip.y()
            val distalZ = tip.z() - pip.z()
            val proximalLength = sqrt(
                proximalX * proximalX + proximalY * proximalY +
                    proximalZ * proximalZ,
            )
            val distalLength = sqrt(
                distalX * distalX + distalY * distalY + distalZ * distalZ,
            )
            if (proximalLength <= 1e-4f || distalLength <= 1e-4f) return false
            val jointCos = (
                proximalX * distalX + proximalY * distalY +
                    proximalZ * distalZ
            ) / (proximalLength * distalLength)
            return tipRadius <= pipRadius * FIST_TIP_TO_PIP_RATIO &&
                tipRadius <= mcpRadius * FIST_TIP_TO_MCP_RATIO &&
                jointCos <= FIST_MAX_JOINT_COS
        }

        return folded(INDEX_MCP, INDEX_PIP, INDEX_TIP) &&
            folded(MIDDLE_MCP, MIDDLE_PIP, MIDDLE_TIP) &&
            folded(RING_MCP, RING_PIP, RING_TIP) &&
            folded(PINKY_MCP, PINKY_PIP, PINKY_TIP)
    }

    private fun evaluateFist(closed: Boolean, x: Float, y: Float, ts: Long) {
        if (!closed) {
            resetFist()
            return
        }
        if (fistSinceMs < 0L) fistSinceMs = ts
        if (!fistFired && ts - fistSinceMs >= FIST_HOLD_MS) {
            fistFired = true
            Log.i(TAG, "fist toggle: joint-flexion")
            callbacks.onGesture(GestureKind.FIST_TOGGLE, 0f, x, y, ts)
        }
    }

    private fun isIndexScrollPose(
        hand: List<com.google.mediapipe.tasks.components.containers.NormalizedLandmark>,
        pinchRatio: Float,
    ): Boolean =
        pinchRatio >= INDEX_POSE_MIN_PINCH_RATIO &&
            isFingerExtended(hand, INDEX_MCP, INDEX_PIP, INDEX_TIP) &&
            isFingerFolded(hand, MIDDLE_MCP, MIDDLE_PIP, MIDDLE_TIP) &&
            isFingerFolded(hand, RING_MCP, RING_PIP, RING_TIP) &&
            isFingerFolded(hand, PINKY_MCP, PINKY_PIP, PINKY_TIP)

    private fun isTwoFingerKeyboardPose(
        hand: List<com.google.mediapipe.tasks.components.containers.NormalizedLandmark>,
        pinchRatio: Float,
    ): Boolean =
        pinchRatio >= INDEX_POSE_MIN_PINCH_RATIO &&
            isFingerExtended(hand, INDEX_MCP, INDEX_PIP, INDEX_TIP) &&
            isFingerExtended(hand, MIDDLE_MCP, MIDDLE_PIP, MIDDLE_TIP) &&
            isFingerFolded(hand, RING_MCP, RING_PIP, RING_TIP) &&
            isFingerFolded(hand, PINKY_MCP, PINKY_PIP, PINKY_TIP)

    private fun isThumbUp(
        hand: List<com.google.mediapipe.tasks.components.containers.NormalizedLandmark>,
    ): Boolean {
        if (hand.size <= PINKY_TIP) return false
        val wrist = hand[WRIST]
        val thumb = hand[THUMB_TIP]
        val middleMcp = hand[MIDDLE_MCP]
        val scale = dist3(
            wrist.x(), wrist.y(), wrist.z(),
            middleMcp.x(), middleMcp.y(), middleMcp.z(),
        )
        if (
            scale <= 1e-4f ||
            thumb.y() > wrist.y() - scale * THUMB_UP_HEIGHT_RATIO
        ) return false
        return isFingerFolded(hand, INDEX_MCP, INDEX_PIP, INDEX_TIP) &&
            isFingerFolded(hand, MIDDLE_MCP, MIDDLE_PIP, MIDDLE_TIP) &&
            isFingerFolded(hand, RING_MCP, RING_PIP, RING_TIP) &&
            isFingerFolded(hand, PINKY_MCP, PINKY_PIP, PINKY_TIP)
    }

    private fun isFingerExtended(
        hand: List<com.google.mediapipe.tasks.components.containers.NormalizedLandmark>,
        mcpIndex: Int,
        pipIndex: Int,
        tipIndex: Int,
    ): Boolean {
        if (hand.size <= tipIndex) return false
        val wrist = hand[WRIST]
        val mcp = hand[mcpIndex]
        val pip = hand[pipIndex]
        val tip = hand[tipIndex]
        val aX = mcp.x() - pip.x()
        val aY = mcp.y() - pip.y()
        val aZ = mcp.z() - pip.z()
        val bX = tip.x() - pip.x()
        val bY = tip.y() - pip.y()
        val bZ = tip.z() - pip.z()
        val aLen = sqrt(aX * aX + aY * aY + aZ * aZ)
        val bLen = sqrt(bX * bX + bY * bY + bZ * bZ)
        if (aLen <= 1e-4f || bLen <= 1e-4f) return false
        val jointCos = (aX * bX + aY * bY + aZ * bZ) / (aLen * bLen)
        val tipRadius = dist3(
            tip.x(), tip.y(), tip.z(), wrist.x(), wrist.y(), wrist.z(),
        )
        val pipRadius = dist3(
            pip.x(), pip.y(), pip.z(), wrist.x(), wrist.y(), wrist.z(),
        )
        return jointCos <= POINT_STRAIGHT_DOT &&
            tipRadius >= pipRadius * POINT_EXTENSION_RATIO
    }

    private fun isFingerFolded(
        hand: List<com.google.mediapipe.tasks.components.containers.NormalizedLandmark>,
        mcpIndex: Int,
        pipIndex: Int,
        tipIndex: Int,
    ): Boolean {
        if (hand.size <= tipIndex) return false
        val wrist = hand[WRIST]
        val mcp = hand[mcpIndex]
        val pip = hand[pipIndex]
        val tip = hand[tipIndex]
        val tipRadius = dist3(
            tip.x(), tip.y(), tip.z(), wrist.x(), wrist.y(), wrist.z(),
        )
        val pipRadius = dist3(
            pip.x(), pip.y(), pip.z(), wrist.x(), wrist.y(), wrist.z(),
        )
        val mcpRadius = dist3(
            mcp.x(), mcp.y(), mcp.z(), wrist.x(), wrist.y(), wrist.z(),
        )
        return tipRadius <= pipRadius * POINT_FOLDED_TIP_PIP_RATIO ||
            tipRadius <= mcpRadius * POINT_FOLDED_TIP_MCP_RATIO
    }

    private fun evaluateIndexScroll(pointing: Boolean, x: Float, y: Float, ts: Long) {
        if (!pointing) {
            resetIndexScroll(true, ts)
            return
        }
        if (indexScrollSinceMs < 0L) {
            indexScrollSinceMs = ts
            // Capture the tip before the short pose-confirmation hold. The old
            // code captured it after 90 ms, i.e. after the user's quick stroke
            // had often already happened. That made one direction appear to
            // work only when the hand happened to pause at the right end.
            indexScrollOriginX = x
            indexScrollOriginY = y
            indexScrollFired = false
            return
        }
        if (!indexScrollActive) {
            if (ts - indexScrollSinceMs < INDEX_SCROLL_HOLD_MS) return
            indexScrollActive = true
            callbacks.onGesture(GestureKind.INDEX_SCROLL_BEGIN, 0f, x, y, ts)
            // Deliberately keep the first valid pose as the origin and evaluate
            // this frame too: fast upward and downward strokes are symmetric.
        }
        if (indexScrollFired) return
        val vertical = y - indexScrollOriginY
        val horizontal = x - indexScrollOriginX
        if (kotlin.math.abs(vertical) < INDEX_SCROLL_STROKE_DISTANCE) return
        if (kotlin.math.abs(vertical) <
            kotlin.math.abs(horizontal) * INDEX_SCROLL_VERTICAL_DOMINANCE) return
        indexScrollFired = true
        Log.i(
            TAG,
            "index stroke vertical=$vertical horizontal=$horizontal " +
                "originY=$indexScrollOriginY endY=$y",
        )
        callbacks.onGesture(
            GestureKind.INDEX_SCROLL_UPDATE,
            vertical,
            x,
            y,
            ts,
        )
    }

    private fun evaluateTwoFinger(open: Boolean, x: Float, y: Float, ts: Long) {
        if (!open) {
            resetTwoFinger()
            return
        }
        if (twoFingerSinceMs < 0L) twoFingerSinceMs = ts
        if (!twoFingerFired && ts - twoFingerSinceMs >= TWO_FINGER_HOLD_MS) {
            twoFingerFired = true
            callbacks.onGesture(GestureKind.TWO_FINGER_KEYBOARD, 0f, x, y, ts)
        }
    }

    private fun evaluateThumbUp(open: Boolean, x: Float, y: Float, ts: Long) {
        if (!open) {
            resetThumbUp()
            return
        }
        if (thumbUpSinceMs < 0L) thumbUpSinceMs = ts
        if (!thumbUpFired && ts - thumbUpSinceMs >= THUMB_UP_HOLD_MS) {
            thumbUpFired = true
            callbacks.onGesture(GestureKind.THUMB_UP_QUICK_MENU, 0f, x, y, ts)
        }
    }

    private fun evaluatePinch(ratio: Float, x: Float, y: Float, ts: Long) {
        if (pinched || candidate == true || ratio < PALM_PINCH_ACTIVITY_RATIO) {
            lastPinchActivityMs = ts
        }
        val want = when {
            !pinched && ratio < ENTER_THRESHOLD -> true
            pinched && ratio > EXIT_THRESHOLD -> false
            else -> {
                candidate = null
                candidateFrames = 0
                if (pinched) callbacks.onGesture(
                    GestureKind.PINCH_UPDATE,
                    zoomFor(ratio), x, y, ts,
                )
                return
            }
        }
        if (candidate != want) {
            candidate = want
            candidateFrames = 0
        }
        candidateFrames++
        // A clearly closed pinch is unambiguous enough to engage on the first
        // inference result. Near the boundary we keep the proven two-frame
        // debounce, so lowering perceived latency does not invite false clicks.
        val required = if (want) {
            if (ratio <= DEEP_ENTER_THRESHOLD) 1 else ENGAGE_FRAMES
        } else RELEASE_FRAMES
        if (candidateFrames < required) return
        pinched = want
        candidate = null
        candidateFrames = 0
        callbacks.onGesture(
            if (want) GestureKind.PINCH_BEGIN else GestureKind.PINCH_END,
            if (want) zoomFor(ratio) else 1f,
            x, y, ts,
        )
    }

    private fun zoomFor(ratio: Float): Float {
        val p = config.pinch
        val t = ((ratio - p.closedNormalizedDistance) /
            (p.openNormalizedDistance - p.closedNormalizedDistance)).coerceIn(0f, 1f)
        return p.zoomAtMinDistance + t * (p.zoomAtMaxDistance - p.zoomAtMinDistance)
    }

    private fun resetPinch() {
        ema = Float.NaN
        pinched = false
        candidate = null
        candidateFrames = 0
        missingFrames = 0
        resetPalm()
    }

    private fun resetPalm() {
        palmSinceMs = -1L
        palmFired = false
    }

    private fun resetTwoPalm() {
        twoPalmSinceMs = -1L
        twoPalmFired = false
    }

    private fun resetFist() {
        fistSinceMs = -1L
        fistFired = false
    }

    private fun resetDirectGestures(emitScrollEnd: Boolean, ts: Long) {
        resetIndexScroll(emitScrollEnd, ts)
        resetTwoFinger()
        resetThumbUp()
    }

    private fun resetIndexScroll(emitEnd: Boolean, ts: Long) {
        if (emitEnd && indexScrollActive) {
            callbacks.onGesture(
                GestureKind.INDEX_SCROLL_END,
                0f,
                -1f,
                -1f,
                ts,
            )
        }
        indexScrollSinceMs = -1L
        indexScrollActive = false
        indexScrollOriginX = Float.NaN
        indexScrollOriginY = Float.NaN
        indexScrollFired = false
        lastIndexScrollPoseMs = -1L
    }

    private fun resetTwoFinger() {
        twoFingerSinceMs = -1L
        twoFingerFired = false
    }

    private fun resetThumbUp() {
        thumbUpSinceMs = -1L
        thumbUpFired = false
    }

    private fun logDiagnostic(ts: Long, hand: Boolean, ratio: Float, x: Float, y: Float) {
        if (lastDiagnosticMs == Long.MIN_VALUE || ts - lastDiagnosticMs >= 1000L) {
            lastDiagnosticMs = ts
            Log.i(TAG, "results=$resultCount hand=$hand ratio=$ratio anchor=($x,$y)")
        }
    }

    private fun dist3(ax: Float, ay: Float, az: Float, bx: Float, by: Float, bz: Float): Float {
        val dx = ax - bx
        val dy = ay - by
        val dz = az - bz
        return sqrt(dx * dx + dy * dy + dz * dz)
    }

    companion object {
        private const val TAG = "MLOmegaEyePinch"
        private const val WRIST = 0
        private const val THUMB_TIP = 4
        private const val INDEX_MCP = 5
        private const val INDEX_PIP = 6
        private const val INDEX_TIP = 8
        private const val MIDDLE_MCP = 9
        private const val MIDDLE_PIP = 10
        private const val MIDDLE_TIP = 12
        private const val RING_MCP = 13
        private const val RING_PIP = 14
        private const val RING_TIP = 16
        private const val PINKY_MCP = 17
        private const val PINKY_PIP = 18
        private const val PINKY_TIP = 20
        private const val ENTER_THRESHOLD = .28f
        private const val DEEP_ENTER_THRESHOLD = .20f
        private const val DEEP_RAW_ENTER_THRESHOLD = .18f
        private const val EXIT_THRESHOLD = .38f
        private const val EMA_ALPHA = .5f
        private const val ENGAGE_FRAMES = 2
        private const val RELEASE_FRAMES = 2
        private const val PALM_STRAIGHT_DOT = -.62f
        private const val PALM_EXTENSION_RATIO = 1.08f
        private const val PALM_THUMB_SPREAD_RATIO = .58f
        private const val PALM_PINCH_ACTIVITY_RATIO = .48f
        private const val PALM_AFTER_PINCH_COOLDOWN_MS = 900L
        private const val FIST_TIP_TO_PIP_RATIO = 1.03f
        private const val FIST_TIP_TO_MCP_RATIO = 1.14f
        private const val FIST_THUMB_TO_PALM_RATIO = 1.38f
        private const val FIST_MAX_JOINT_COS = .62f
        private const val FIST_HOLD_MS = 400L
        private const val TWO_PALM_HOLD_MS = 550L
        private const val INDEX_POSE_MIN_PINCH_RATIO = .44f
        private const val POINT_STRAIGHT_DOT = -.48f
        private const val POINT_EXTENSION_RATIO = 1.05f
        private const val POINT_FOLDED_TIP_PIP_RATIO = 1.08f
        private const val POINT_FOLDED_TIP_MCP_RATIO = 1.18f
        // The Eye stream commonly yields 10-15 analysed frames/s even though
        // capture targets 25 fps. One real inference frame is therefore enough
        // to reject a transient pose without adding a perceptible 120 ms tax.
        private const val INDEX_SCROLL_HOLD_MS = 90L
        private const val INDEX_SCROLL_STROKE_DISTANCE = .045f
        private const val INDEX_SCROLL_VERTICAL_DOMINANCE = 1.15f
        private const val INDEX_SCROLL_POSE_GRACE_MS = 300L
        private const val TWO_FINGER_HOLD_MS = 320L
        private const val THUMB_UP_HOLD_MS = 360L
        private const val THUMB_UP_HEIGHT_RATIO = .35f
        private const val MAX_ATELIER_FPS = 25f
    }
}
