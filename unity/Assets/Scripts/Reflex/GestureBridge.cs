// MLOmega V19 — E26
// Unity-side bridge to the native Android gesture pipeline
// (com.mlomega.xr.reflexvision.GesturePipeline, MediaPipe HandLandmarker +
// GestureRecognizer). Owns the AndroidJavaObject, activates/deactivates it on
// demand for the ReflexScheduler (battery — §9.4), feeds the eye/phone texture
// from EyeCaptureSource.OnFrame up the pipeline, and re-emits recognised gestures
// as C# events on the main thread.
//
// Editor / Windows dev has no Android plugin, so a REAL simulated recogniser runs
// instead (keyboard/mouse): so the whole reflex chain (LensWindow zoom, menu,
// hide-UI) can be developed and tested without a device. Same DIRECT_ANDROID /
// editor-sim split as LiveTransportBridge (DECISIONS §E24/§E26).
using System;
using System.Collections.Generic;
using System.IO;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using MLOmega.XR.UI;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    /// <summary>A recognised gesture surfaced to the reflex layer (main thread).</summary>
    public readonly struct GestureEvent
    {
        public readonly GestureKind Kind;
        public readonly float ZoomFactor;
        public readonly Vector2 ScreenPoint; // normalised 0..1; (-1,-1) if n/a
        public readonly long TimestampMs;

        public GestureEvent(GestureKind kind, float zoom, Vector2 point, long tsMs)
        {
            Kind = kind;
            ZoomFactor = zoom;
            ScreenPoint = point;
            TimestampMs = tsMs;
        }
    }

    public sealed class GestureBridge : MonoBehaviour
    {
        [SerializeField] private EyeCaptureSource _capture;

        [Tooltip("Relative path (under getExternalFilesDir()/models) of the MediaPipe " +
                 "gesture .task bundle. Provisioned at first run (E47), not shipped in the APK.")]
        [SerializeField] private string _modelRelativePath = "models/gesture_recognizer.task";

        [Tooltip("Max hands tracked (1 keeps latency lowest).")]
        [Min(1)]
        [SerializeField] private int _numHands = 1;

        [Tooltip("Longest side (px) the capture texture is downscaled to before the " +
                 "native gesture graph. 256 is plenty for hand landmarks and keeps the " +
                 "GPU readback + JNI Bitmap copy cheap. Never full capture resolution.")]
        [Min(64)]
        [SerializeField] private int _maxDimension = 256;

        [Tooltip("Target gesture cadence (fps). Product recognition remains capped at " +
                 "15; the dedicated Atelier Eye pipeline permits 25. The " +
                 "capture texture arrives at up to 30 fps; we only sample this often " +
                 "(battery, §9.4). The native FrameThrottle is authoritative; this gates " +
                 "the GPU readback so we do not even pay for dropped frames.")]
        [Range(10f, 25f)]
        [SerializeField] private float _targetFps = 12f;

        [Tooltip("Atelier hardware gate only: log the Eye->MediaPipe cadence and " +
                 "persist one downscaled diagnostic frame. Disabled in product scenes.")]
        [SerializeField] private bool _deviceDiagnostics;

        [Tooltip("Use the lighter HandLandmarker-only Eye pinch path. Atelier-only; " +
                 "the product GestureRecognizer path remains unchanged.")]
        [SerializeField] private bool _useDedicatedEyePinchPipeline;

        /// <summary>Raised on the main thread for each recognised gesture.</summary>
        public event Action<GestureEvent> GestureRecognized;

        /// <summary>
        /// Optional feature-layer interception for the deliberate two-palm
        /// gesture. This keeps the generic gesture assembly independent from
        /// Lab/browser types while still providing an emergency immersive exit.
        /// </summary>
        public static event Func<bool> TwoPalmOverrideRequested;

        public static bool TryHandleTwoPalmOverride()
        {
            Delegate[] handlers = TwoPalmOverrideRequested?.GetInvocationList();
            if (handlers == null) return false;
            foreach (Delegate handler in handlers)
            {
                try
                {
                    if (((Func<bool>)handler).Invoke()) return true;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[GestureBridge] two-palm override failed: " +
                        exception.Message);
                }
            }
            return false;
        }

        /// <summary>Whether the native/simulated pipeline is currently running.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Full gesture interaction is suspended, but a 10 fps fist sentinel stays
        /// alive so the same physical gesture can restore it without a controller.
        /// </summary>
        public bool IsInteractionStandby { get; private set; }
        public HandLowLightMode LowLightMode { get; private set; }

        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();

        // Client-side readback gate: skip the GPU readback for frames the native
        // throttle would drop anyway, so downscale + Bitmap copy only run 10-15x/s.
        private float _readbackAccum;
        private float _readbackPeriod;
        private const float StandbyFps = 10f;
        private readonly HandLowLightEnhancer _lowLightEnhancer =
            new HandLowLightEnhancer();

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _pipeline;
        private GestureProxy _proxy;
        private Texture2D _readback;      // reused downscaled ARGB readback target
        private AndroidJavaObject _bitmap; // reused native ARGB_8888 Bitmap
        private int[] _argbBuffer;         // reused packed-ARGB scratch for setPixels
        private Color32[] _rgbaBuffer;     // reused top-down pixels; no per-frame GC
        private int[] _sampleX;
        private int[] _sampleUx;
        private int[] _sampleY;
        private int[] _sampleUy;
        private int _sampleSourceW, _sampleSourceH, _sampleUvW, _sampleUvH;
        private int _bitmapW, _bitmapH;
        private int _submittedFrames;
        private bool _savedDiagnosticFrame;
        private bool _loggedNativeI420FastPath;
#endif

        private void Awake()
        {
            if (_capture == null) _capture = FindAnyObjectByType<EyeCaptureSource>();
            UpdateReadbackPeriod();
        }

        private void OnEnable()
        {
            if (_capture != null) _capture.OnFrame += HandleFrame;
        }

        private void OnDisable()
        {
            if (_capture != null) _capture.OnFrame -= HandleFrame;
            Deactivate();
        }

        private void Update()
        {
            DrainMainThread();
#if UNITY_EDITOR
            if (IsRunning) SimulateFromInput();
#endif
        }

        /// <summary>
        /// Feed one capture frame to the native gesture graph. Only runs while the
        /// recogniser is active (ReflexScheduler on-demand — §9.4); throttled to the
        /// gesture cadence and downscaled so we never read back at full res/30 fps.
        /// No-op in the editor (the simulator drives gestures from input instead).
        /// </summary>
        private void HandleFrame(Texture texture, FrameEnvelope envelope)
        {
            if (!IsRunning || texture == null) return;

            // Client-side cadence gate: avoid the GPU readback for frames the native
            // FrameThrottle would drop. period == 0 means feed every frame.
            _readbackAccum += Time.unscaledDeltaTime;
            if (_readbackPeriod > 0f && _readbackAccum < _readbackPeriod) return;
            // Preserve the fractional time budget. Resetting to zero turns a
            // requested 20 fps cadence into 15 fps on a 30 fps Eye source.
            _readbackAccum = _readbackPeriod > 0f
                ? Mathf.Max(0f, _readbackAccum - _readbackPeriod)
                : 0f;

#if UNITY_ANDROID && !UNITY_EDITOR
            long tsMs = envelope != null ? envelope.CaptureMonotonicNs / 1_000_000L : 0L;
            // XREAL Eye already exposes CPU-readable Y/U/V planes. Feeding those
            // directly avoids the synchronous GPU ReadPixels fence that used to
            // interrupt the 60 Hz XR render loop up to 25 times per second.
            if (_useDedicatedEyePinchPipeline &&
                _capture != null &&
                _capture.TryGetCurrentNativeI420(
                    out Texture2D planeY,
                    out Texture2D planeU,
                    out Texture2D planeV) &&
                TryPushNativeI420(planeY, planeU, planeV, tsMs))
                return;
            PushDownscaledFrame(texture, tsMs);
#endif
        }

        /// <summary>
        /// Activate the recogniser. Called by the ReflexScheduler when a
        /// gesture-relevant signal is active. Idempotent.
        /// </summary>
        public void Activate()
        {
            if (IsRunning) return;
            IsRunning = true;
            _readbackAccum = _readbackPeriod; // feed the first frame immediately
#if UNITY_ANDROID && !UNITY_EDITOR
            // E48-A: the MediaPipe .task bundle may still be provisioning (or absent);
            // native construction then throws. Reset IsRunning so the scheduler retries
            // later / next launch once the model lands — honest degraded, no crash.
            try
            {
                StartAndroid();
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Debug.LogWarning($"[GestureBridge] activation deferred (model not ready?): {ex.Message}");
            }
#else
            Debug.Log("[GestureBridge] editor: simulated gestures (mouse wheel = pinch zoom, " +
                      "M = menu, H = hide).");
#endif
        }

        /// <summary>Deactivate the recogniser (tears down the native graph — §9.4). Idempotent.</summary>
        public void Deactivate()
        {
            if (!IsRunning) return;
            IsRunning = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _pipeline?.Call("stop");
            }
            finally
            {
                _pipeline?.Dispose();
                _pipeline = null;
                _proxy = null;
            }
            ReleaseBitmap();
#endif
        }

        /// <summary>
        /// Rebuild the native MediaPipe graph after Android temporarily moved the
        /// XREAL panel to a 2D system display. The XREAL RGB camera may have been
        /// stopped and started while this component stayed enabled, so OnEnable
        /// alone cannot recover the native recogniser. Idempotent and deliberately
        /// preserves the current low-light/standby configuration.
        /// </summary>
        public void RestartAfterExternalCameraResume()
        {
            bool shouldRun = IsRunning;
            if (shouldRun) Deactivate();
            if (shouldRun) Activate();
            Debug.Log(
                "[GestureBridge] external display return: MediaPipe graph " +
                (IsRunning ? "restarted" : "inactive"));
        }

        private void UpdateReadbackPeriod()
        {
            float fps = IsInteractionStandby ? StandbyFps : _targetFps;
            _readbackPeriod = fps > 0f ? 1f / fps : 0f;
            _readbackAccum = _readbackPeriod;
        }

        public void SetInteractionStandby(bool standby)
        {
            if (IsInteractionStandby == standby) return;
            IsInteractionStandby = standby;
            UpdateReadbackPeriod();
            Debug.Log(
                "[GestureBridge] physical gestures " +
                (IsInteractionStandby
                    ? "standby (10 fps fist sentinel)"
                    : $"active ({_targetFps:F0} fps)"));
        }

        public void SetLowLightMode(HandLowLightMode mode)
        {
            HandLowLightMode safe = mode switch
            {
                HandLowLightMode.Light => HandLowLightMode.Light,
                HandLowLightMode.Strong => HandLowLightMode.Strong,
                _ => HandLowLightMode.Off,
            };
            if (LowLightMode == safe) return;
            LowLightMode = safe;
            _lowLightEnhancer.Reset();
            Debug.Log("[GestureBridge] hand low-light=" + safe);
        }

        /// <summary>
        /// Lab-only thermal budget for the inference copy. Eye capture,
        /// recordings and displayed textures remain at native resolution.
        /// </summary>
        public void SetInferenceLongEdge(int pixels)
        {
            _maxDimension = Mathf.Clamp(pixels, 320, 768);
            Debug.Log("[GestureBridge] inference long edge=" + _maxDimension);
        }

        private void ToggleInteractionStandby() =>
            SetInteractionStandby(!IsInteractionStandby);

        // --- native plumbing ------------------------------------------------------

#if UNITY_ANDROID && !UNITY_EDITOR
        private void StartAndroid()
        {
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var ctx = activity.Call<AndroidJavaObject>("getApplicationContext");

            // Models are provisioned to getExternalFilesDir()/models at first run
            // (E47), never shipped in the APK. getExternalFilesDir(null) is the
            // app-private external files dir (no permission required).
            using var extDir = ctx.Call<AndroidJavaObject>("getExternalFilesDir", (object)null);
            string filesDir = extDir != null
                ? extDir.Call<string>("getAbsolutePath")
                : ctx.Call<AndroidJavaObject>("getFilesDir").Call<string>("getAbsolutePath");
            string modelPath = filesDir + "/" + _modelRelativePath;

            float detection = .50f;
            float presence = .50f;
            float tracking = .50f;
            if (_useDedicatedEyePinchPipeline)
            {
                // Keep the sensitive Eye profile loaded once. Reconstructing the
                // GPU HandLandmarker synchronously on every UI mode change froze
                // the XR app for ~3.2 s on the S24. Geometry + debounce remain the
                // action gate; low-light modes now change pixels without restart.
                detection = .35f;
                presence = .35f;
                tracking = .42f;
            }

            using var factory = new AndroidJavaClass(
                "com.mlomega.xr.reflexvision.GestureConfigFactory");
            using var cfg = factory.CallStatic<AndroidJavaObject>(
                "forUnityTuned",
                modelPath,
                _numHands,
                _targetFps,
                detection,
                presence,
                tracking);
            _proxy = new GestureProxy(this);
            string pipelineClass = _useDedicatedEyePinchPipeline
                ? "com.mlomega.xr.reflexvision.EyePinchPipeline"
                : "com.mlomega.xr.reflexvision.GesturePipeline";
            _pipeline = new AndroidJavaObject(pipelineClass, ctx, cfg, _proxy);
            _pipeline.Call("start");
        }

        /// <summary>
        /// Downscale the capture texture to at most <see cref="_maxDimension"/> on its
        /// longest side, read it back to CPU, pack it into a reused ARGB_8888 Android
        /// Bitmap over JNI, and hand it to the native <c>GesturePipeline.pushFrame</c>.
        /// The native FrameThrottle drops anything above the gesture cadence, so this
        /// pushes at most ~15 fps of small frames — never full res, never 30 fps.
        /// </summary>
        private void PushDownscaledFrame(Texture source, long timestampMs)
        {
            if (_pipeline == null) return;

            int sw = source.width, sh = source.height;
            if (sw <= 0 || sh <= 0) return;
            int longSide = Mathf.Max(sw, sh);
            float scale = longSide > _maxDimension ? (float)_maxDimension / longSide : 1f;
            int w = Mathf.Max(1, Mathf.RoundToInt(sw * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(sh * scale));

            // Blit into a small temporary RT (GPU downscale), then read that back.
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            if (_readback == null || _readback.width != w || _readback.height != h)
            {
                if (_readback != null) Destroy(_readback);
                _readback = new Texture2D(w, h, TextureFormat.RGBA32, false);
            }
            _readback.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            EnsurePixelBuffers(w, h);
            Unity.Collections.NativeArray<Color32> raw =
                _readback.GetRawTextureData<Color32>();
            for (int y = 0; y < h; y++)
            {
                int srcRow = (h - 1 - y) * w;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                    _rgbaBuffer[dstRow + x] = raw[srcRow + x];
            }
            SubmitTopDownPixels(w, h, timestampMs);

            if (_deviceDiagnostics && !_savedDiagnosticFrame)
            {
                _savedDiagnosticFrame = true;
                string path = Path.Combine(
                    Application.persistentDataPath,
                    "eye-hand-diagnostic.png");
                File.WriteAllBytes(path, _readback.EncodeToPNG());
                Debug.Log(
                    $"[GestureBridge] first Eye frame submitted: {w}x{h}, " +
                    $"ts={timestampMs}, diagnostic={path}");
            }
        }

        private bool TryPushNativeI420(
            Texture2D planeY,
            Texture2D planeU,
            Texture2D planeV,
            long timestampMs)
        {
            if (_pipeline == null || planeY == null || planeU == null || planeV == null)
                return false;
            int sw = planeY.width;
            int sh = planeY.height;
            int uw = planeU.width;
            int uh = planeU.height;
            if (sw <= 0 || sh <= 0 || uw <= 0 || uh <= 0 ||
                planeV.width != uw || planeV.height != uh)
                return false;

            Unity.Collections.NativeArray<byte> yPlane =
                planeY.GetRawTextureData<byte>();
            Unity.Collections.NativeArray<byte> uPlane =
                planeU.GetRawTextureData<byte>();
            Unity.Collections.NativeArray<byte> vPlane =
                planeV.GetRawTextureData<byte>();
            if (yPlane.Length < sw * sh ||
                uPlane.Length < uw * uh ||
                vPlane.Length < uw * uh)
                return false;

            float scale = (float)_maxDimension / Mathf.Max(sw, sh);
            int w = Mathf.Max(1, Mathf.RoundToInt(sw * Mathf.Min(1f, scale)));
            int h = Mathf.Max(1, Mathf.RoundToInt(sh * Mathf.Min(1f, scale)));
            EnsurePixelBuffers(w, h);
            EnsureI420SamplingMaps(sw, sh, uw, uh, w, h);

            // Texture rows are bottom-up; Android Bitmap rows are top-down. Use
            // nearest-neighbour sampling directly from the SDK planes and the
            // same BGR channel order as the validated Eye conversion shader.
            // Source lookups are precomputed: divisions/Mathf.Clamp inside this
            // 25 fps pixel loop were producing visible XR compositor hitches.
            bool enhance = LowLightMode != HandLowLightMode.Off;
            for (int y = 0; y < h; y++)
            {
                int sy = _sampleY[y];
                int uy = _sampleUy[y];
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int sx = _sampleX[x];
                    int ux = _sampleUx[x];
                    int luminance = yPlane[sy * sw + sx];
                    int u = uPlane[uy * uw + ux] - 128;
                    int v = vPlane[uy * uw + ux] - 128;
                    int red = ClampByte(luminance + ((359 * v) >> 8));
                    int green = ClampByte(
                        luminance - ((88 * u + 183 * v) >> 8));
                    int blue = ClampByte(luminance + ((454 * u) >> 8));
                    int destination = dstRow + x;
                    if (enhance)
                    {
                        _rgbaBuffer[destination] = new Color32(
                            (byte)blue,
                            (byte)green,
                            (byte)red,
                            255);
                    }
                    else
                    {
                        // Exact packed equivalent of the validated BGR Color32
                        // path, without its otherwise redundant second pass.
                        _argbBuffer[destination] = unchecked((int)0xFF000000) |
                            (blue << 16) | (green << 8) | red;
                    }
                }
            }
            if (enhance)
                SubmitTopDownPixels(w, h, timestampMs);
            else
                SubmitPackedArgb(w, h, timestampMs);
            if (!_loggedNativeI420FastPath)
            {
                _loggedNativeI420FastPath = true;
                Debug.Log(
                    $"[GestureBridge] native I420 fast path: {sw}x{sh} -> {w}x{h}; " +
                    "GPU ReadPixels bypassed");
            }
            return true;
        }

        private void EnsureI420SamplingMaps(
            int sourceW,
            int sourceH,
            int uvW,
            int uvH,
            int outputW,
            int outputH)
        {
            bool valid =
                _sampleX != null && _sampleX.Length == outputW &&
                _sampleY != null && _sampleY.Length == outputH &&
                _sampleSourceW == sourceW && _sampleSourceH == sourceH &&
                _sampleUvW == uvW && _sampleUvH == uvH;
            if (valid) return;

            _sampleX = new int[outputW];
            _sampleUx = new int[outputW];
            _sampleY = new int[outputH];
            _sampleUy = new int[outputH];
            for (int x = 0; x < outputW; x++)
            {
                int sourceX = Mathf.Min(sourceW - 1, x * sourceW / outputW);
                _sampleX[x] = sourceX;
                _sampleUx[x] = Mathf.Min(uvW - 1, sourceX * uvW / sourceW);
            }
            for (int y = 0; y < outputH; y++)
            {
                int sourceY = sourceH - 1 -
                    Mathf.Min(sourceH - 1, y * sourceH / outputH);
                _sampleY[y] = sourceY;
                _sampleUy[y] = Mathf.Min(uvH - 1, sourceY * uvH / sourceH);
            }
            _sampleSourceW = sourceW;
            _sampleSourceH = sourceH;
            _sampleUvW = uvW;
            _sampleUvH = uvH;
        }

        private static int ClampByte(int value) =>
            value < 0 ? 0 : (value > 255 ? 255 : value);

        private void EnsurePixelBuffers(int w, int h)
        {
            int length = w * h;
            if (_rgbaBuffer == null || _rgbaBuffer.Length != length)
                _rgbaBuffer = new Color32[length];
            if (_argbBuffer == null || _argbBuffer.Length != length)
                _argbBuffer = new int[length];
            EnsureBitmap(w, h);
        }

        private void SubmitTopDownPixels(int w, int h, long timestampMs)
        {
            if (_bitmap == null || _rgbaBuffer == null) return;
            _lowLightEnhancer.Process(_rgbaBuffer, w, h, LowLightMode);
            for (int i = 0; i < _rgbaBuffer.Length; i++)
            {
                Color32 c = _rgbaBuffer[i];
                _argbBuffer[i] =
                    (c.a << 24) | (c.r << 16) | (c.g << 8) | c.b;
            }

            SubmitPackedArgb(w, h, timestampMs);
        }

        private void SubmitPackedArgb(int w, int h, long timestampMs)
        {
            if (_bitmap == null || _argbBuffer == null) return;
            _bitmap.Call("setPixels", _argbBuffer, 0, w, 0, 0, w, h);
            _pipeline.Call("pushFrame", _bitmap, timestampMs);
            _submittedFrames++;
            if (_deviceDiagnostics && _submittedFrames % 60 == 0)
            {
                Debug.Log(
                    $"[GestureBridge] Eye frames submitted={_submittedFrames}, " +
                    $"last={w}x{h}, ts={timestampMs}");
            }
        }

        private void EnsureBitmap(int w, int h)
        {
            if (_bitmap != null && _bitmapW == w && _bitmapH == h) return;
            ReleaseBitmap();
            using var cfg = new AndroidJavaClass("android.graphics.Bitmap$Config")
                .GetStatic<AndroidJavaObject>("ARGB_8888");
            _bitmap = new AndroidJavaClass("android.graphics.Bitmap")
                .CallStatic<AndroidJavaObject>("createBitmap", w, h, cfg);
            _bitmapW = w;
            _bitmapH = h;
        }

        private void ReleaseBitmap()
        {
            if (_bitmap != null)
            {
                try { _bitmap.Call("recycle"); } catch { /* best-effort */ }
                _bitmap.Dispose();
                _bitmap = null;
            }
            _bitmapW = _bitmapH = 0;
            _rgbaBuffer = null;
            _argbBuffer = null;
            _sampleX = null;
            _sampleUx = null;
            _sampleY = null;
            _sampleUy = null;
            _sampleSourceW = _sampleSourceH = _sampleUvW = _sampleUvH = 0;
        }

        internal void EnqueueMainThread(Action a) { lock (_queueLock) { _mainThreadQueue.Enqueue(a); } }
#endif

        internal void OnNativeGesture(string kindName, float zoom, float x, float y, long tsMs)
        {
            if (_deviceDiagnostics)
                Debug.Log(
                    $"[GestureBridge] native {kindName}: zoom={zoom:F2}, " +
                    $"anchor=({x:F3},{y:F3}), ts={tsMs}");
            GestureKind kind = MapKind(kindName);
            Enqueue(() =>
            {
                if (kind == GestureKind.FistToggle)
                {
                    ToggleInteractionStandby();
                    GestureRecognized?.Invoke(
                        new GestureEvent(
                            kind,
                            zoom,
                            new Vector2(x, y),
                            tsMs));
                    return;
                }
                if (IsInteractionStandby) return;
                GestureRecognized?.Invoke(
                    new GestureEvent(
                        kind,
                        zoom,
                        new Vector2(x, y),
                        tsMs));
            });
        }

        internal void OnNativeError(string message) =>
            Debug.LogWarning($"[GestureBridge] native error: {message}");

        private static GestureKind MapKind(string name) => name switch
        {
            "PINCH_BEGIN" => GestureKind.PinchBegin,
            "PINCH_UPDATE" => GestureKind.PinchUpdate,
            "PINCH_END" => GestureKind.PinchEnd,
            "OPEN_PALM_MENU" => GestureKind.OpenPalmMenu,
            "TWO_PALM_MENU" => GestureKind.TwoPalmMenu,
            "SWIPE_HIDE" => GestureKind.SwipeHide,
            "FIST_TOGGLE" => GestureKind.FistToggle,
            "INDEX_SCROLL_BEGIN" => GestureKind.IndexScrollBegin,
            "INDEX_SCROLL_UPDATE" => GestureKind.IndexScrollUpdate,
            "INDEX_SCROLL_END" => GestureKind.IndexScrollEnd,
            "TWO_FINGER_KEYBOARD" => GestureKind.TwoFingerKeyboard,
            "THUMB_UP_QUICK_MENU" => GestureKind.ThumbUpQuickMenu,
            _ => GestureKind.PinchUpdate
        };

        private void Enqueue(Action a) { lock (_queueLock) { _mainThreadQueue.Enqueue(a); } }

        private void DrainMainThread()
        {
            while (true)
            {
                Action work = null;
                lock (_queueLock) { if (_mainThreadQueue.Count > 0) work = _mainThreadQueue.Dequeue(); }
                if (work == null) break;
                try { work(); } catch (Exception ex) { Debug.LogError($"[GestureBridge] {ex}"); }
            }
        }

        // --- editor simulation (real input, not a stub) ---------------------------

#if UNITY_EDITOR
        private bool _simPinching;
        private float _simZoom = 1f;

        private void SimulateFromInput()
        {
            long now = (long)(Time.unscaledTimeAsDouble * 1000.0);
            Vector2 pt = new Vector2(0.5f, 0.5f);

            // Mouse wheel drives a pinch zoom: first scroll begins, subsequent update, release with right-click.
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _simZoom = Mathf.Clamp(_simZoom + wheel * 0.4f, 1f, 6f);
                if (!_simPinching)
                {
                    _simPinching = true;
                    RaiseSim(GestureKind.PinchBegin, _simZoom, pt, now);
                }
                else
                {
                    RaiseSim(GestureKind.PinchUpdate, _simZoom, pt, now);
                }
            }
            if (_simPinching && Input.GetMouseButtonDown(1))
            {
                _simPinching = false;
                _simZoom = 1f;
                RaiseSim(GestureKind.PinchEnd, 1f, pt, now);
            }
            if (Input.GetKeyDown(KeyCode.M)) RaiseSim(GestureKind.OpenPalmMenu, 0f, pt, now);
            if (Input.GetKeyDown(KeyCode.H)) RaiseSim(GestureKind.SwipeHide, 0f, pt, now);
            if (Input.GetKeyDown(KeyCode.F))
            {
                ToggleInteractionStandby();
                RaiseSim(GestureKind.FistToggle, 0f, pt, now);
            }
        }

        private void RaiseSim(GestureKind kind, float zoom, Vector2 pt, long tsMs) =>
            GestureRecognized?.Invoke(new GestureEvent(kind, zoom, pt, tsMs));
#endif

        /// <summary>
        /// Directly inject a gesture (used by EditMode tests and the demo driver to
        /// prove the reflex chain without any device/native pipeline).
        /// </summary>
        public void InjectGesture(GestureEvent ev) => GestureRecognized?.Invoke(ev);

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class GestureProxy : AndroidJavaProxy
        {
            private readonly GestureBridge _bridge;
            public GestureProxy(GestureBridge b)
                : base("com.mlomega.xr.reflexvision.GestureCallbacks") { _bridge = b; }

            void onGesture(AndroidJavaObject kind, float zoom, float x, float y, long tsMs)
            {
                string name = kind != null ? kind.Call<string>("name") : "PINCH_UPDATE";
                _bridge.OnNativeGesture(name, zoom, x, y, tsMs);
            }
            void onError(string message) => _bridge.OnNativeError(message);
        }
#endif
    }
}
