using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using MLOmega.XR.Transport;
using MLOmega.XR.UI;
using Newtonsoft.Json;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    /// <summary>
    /// Opt-in S24 semantic tier for object cards. A bundled ML Kit model labels a
    /// small 320 px view at a bounded 2 fps and sends the result to the PC. It
    /// never replaces VisionRT localisation and never runs when AR/object menus
    /// are disabled. Precise model/manual identification remains on-demand.
    /// </summary>
    public sealed class InstantImageLabelBridge : MonoBehaviour
    {
        [SerializeField] private EyeCaptureSource _capture;
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private AugmentedRealityFeatureRegistry _features;
        [Range(0.5f, 3f)]
        [SerializeField] private float _targetFps = 2f;
        [Range(128, 480)]
        [SerializeField] private int _maxDimension = 320;
        [Range(0.4f, 0.95f)]
        [SerializeField] private float _minimumConfidence = 0.65f;

        public bool IsRunning { get; private set; }

        private float _accumulated;
        private readonly Queue<Action> _mainThread = new Queue<Action>();
        private readonly object _queueLock = new object();
        private readonly Dictionary<long, string> _frameIds = new Dictionary<long, string>();

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _labeler;
        private LabelProxy _proxy;
        private Texture2D _readback;
        private AndroidJavaObject _bitmap;
        private int[] _argb;
        private int _bitmapW;
        private int _bitmapH;
#endif

        private void Awake()
        {
            if (_capture == null) _capture = FindAnyObjectByType<EyeCaptureSource>();
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_features == null) _features = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
        }

        private void OnEnable()
        {
            if (_capture != null) _capture.OnFrame += HandleFrame;
            if (_features != null)
            {
                _features.FeatureChanged += OnFeatureChanged;
                _features.ServiceStatusChanged += OnServiceStatusChanged;
            }
            RefreshState();
        }

        private void OnDisable()
        {
            if (_capture != null) _capture.OnFrame -= HandleFrame;
            if (_features != null)
            {
                _features.FeatureChanged -= OnFeatureChanged;
                _features.ServiceStatusChanged -= OnServiceStatusChanged;
            }
            StopNative();
        }

        private void Update()
        {
            while (true)
            {
                Action action = null;
                lock (_queueLock)
                {
                    if (_mainThread.Count > 0) action = _mainThread.Dequeue();
                }
                if (action == null) break;
                try { action(); }
                catch (Exception ex) { Debug.LogWarning($"[MLKitLabels] {ex.Message}"); }
            }
        }

        private void OnFeatureChanged(string _, bool __) => RefreshState();
        private void OnServiceStatusChanged(string _, string __) => RefreshState();

        private void RefreshState()
        {
            bool shouldRun = _features != null &&
                _features.IsActive(AugmentedRealityFeatureRegistry.ObjectMenus);
            if (shouldRun && !IsRunning) StartNative();
            else if (!shouldRun && IsRunning) StopNative();
        }

        private void StartNative()
        {
            IsRunning = true;
            _accumulated = 1f;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var context = activity.Call<AndroidJavaObject>("getApplicationContext");
                _proxy = new LabelProxy(this);
                _labeler = new AndroidJavaObject(
                    "com.mlomega.xr.reflexvision.InstantImageLabeler",
                    context, _proxy, _minimumConfidence, 3);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Debug.LogWarning($"[MLKitLabels] unavailable: {ex.Message}");
            }
#endif
        }

        private void StopNative()
        {
            IsRunning = false;
            _frameIds.Clear();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_labeler != null)
            {
                try { _labeler.Call("close"); } catch { /* shutdown best effort */ }
                _labeler.Dispose();
                _labeler = null;
            }
            _proxy = null;
            ReleaseBitmap();
#endif
        }

        private void HandleFrame(Texture texture, FrameEnvelope envelope)
        {
            if (!IsRunning || texture == null || _transport == null) return;
            _accumulated += Time.unscaledDeltaTime;
            float period = 1f / Mathf.Max(0.5f, _targetFps);
            if (_accumulated < period) return;
            _accumulated = 0f;
#if UNITY_ANDROID && !UNITY_EDITOR
            long timestampMs = envelope != null
                ? envelope.CaptureMonotonicNs / 1_000_000L
                : (long)(Time.unscaledTimeAsDouble * 1000.0);
            _frameIds[timestampMs] = envelope?.FrameId ?? string.Empty;
            if (_frameIds.Count > 4)
            {
                long oldest = long.MaxValue;
                foreach (long key in _frameIds.Keys) if (key < oldest) oldest = key;
                if (oldest != long.MaxValue) _frameIds.Remove(oldest);
            }
            PushDownscaled(texture, (int)(envelope?.Rotation ?? 0L), timestampMs);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void PushDownscaled(Texture source, int rotation, long timestampMs)
        {
            if (_labeler == null) return;
            int sw = source.width, sh = source.height;
            if (sw <= 0 || sh <= 0) return;
            float scale = Mathf.Min(1f, (float)_maxDimension / Mathf.Max(sw, sh));
            int w = Mathf.Max(1, Mathf.RoundToInt(sw * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(sh * scale));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            if (_readback == null || _readback.width != w || _readback.height != h)
            {
                if (_readback != null) Destroy(_readback);
                _readback = new Texture2D(w, h, TextureFormat.RGBA32, false);
            }
            _readback.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            _readback.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            EnsureBitmap(w, h);
            Color32[] pixels = _readback.GetPixels32();
            if (_argb == null || _argb.Length != w * h) _argb = new int[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w;
                int dst = y * w;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = pixels[src + x];
                    _argb[dst + x] = (c.a << 24) | (c.r << 16) | (c.g << 8) | c.b;
                }
            }
            _bitmap.Call("setPixels", _argb, 0, w, 0, 0, w, h);
            _labeler.Call<bool>("pushFrame", _bitmap, rotation, timestampMs);
        }

        private void EnsureBitmap(int w, int h)
        {
            if (_bitmap != null && _bitmapW == w && _bitmapH == h) return;
            ReleaseBitmap();
            using var config = new AndroidJavaClass("android.graphics.Bitmap$Config")
                .GetStatic<AndroidJavaObject>("ARGB_8888");
            _bitmap = new AndroidJavaClass("android.graphics.Bitmap")
                .CallStatic<AndroidJavaObject>("createBitmap", w, h, config);
            _bitmapW = w;
            _bitmapH = h;
        }

        private void ReleaseBitmap()
        {
            if (_bitmap != null)
            {
                try { _bitmap.Call("recycle"); } catch { }
                _bitmap.Dispose();
                _bitmap = null;
            }
            _bitmapW = _bitmapH = 0;
            if (_readback != null)
            {
                Destroy(_readback);
                _readback = null;
            }
            _argb = null;
        }

        private sealed class LabelProxy : AndroidJavaProxy
        {
            private readonly InstantImageLabelBridge _owner;
            public LabelProxy(InstantImageLabelBridge owner)
                : base("com.mlomega.xr.reflexvision.InstantImageLabelCallbacks")
            {
                _owner = owner;
            }
            void onLabels(string labelsJson, long timestampMs) =>
                _owner.Enqueue(() => _owner.ForwardLabels(labelsJson, timestampMs));
            void onError(string message) =>
                _owner.Enqueue(() => Debug.LogWarning($"[MLKitLabels] {message}"));
        }
#endif

        private void Enqueue(Action action)
        {
            lock (_queueLock) _mainThread.Enqueue(action);
        }

        private void ForwardLabels(string labelsJson, long timestampMs)
        {
            _frameIds.TryGetValue(timestampMs, out string frameId);
            _frameIds.Remove(timestampMs);
            string message =
                "{\"type\":\"device_object_labels\"," +
                "\"source\":\"mlkit_bundled\"," +
                "\"source_frame_id\":" + JsonConvert.ToString(frameId ?? string.Empty) + "," +
                "\"captured_at_ms\":" + timestampMs + "," +
                "\"labels\":" + (string.IsNullOrWhiteSpace(labelsJson) ? "[]" : labelsJson) + "}";
            _transport.SendContractMessage(message);
        }
    }
}
