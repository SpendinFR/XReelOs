// MLOmega V19 â€” E24
// Unity-side bridge to the native Android live transport (LiveTransportPlugin,
// GetStream webrtc-android). Owns the AndroidJavaObject, feeds the eye/phone
// texture from EyeCaptureSource.OnFrame (E23) up the WebRTC video track, relays
// contract messages (UIIntent down / UIReceipt up) over the reliable DataChannel,
// and re-emits the native connection state as C# events for the StatusBar (E25).
//
// Platform matrix (documented decision, DECISIONS.md Â§E24):
//   - Android device build: DIRECT_ANDROID â€” the real Kotlin plugin runs.
//   - Editor / Windows dev: DIRECT_PYTHON â€” there is no Android plugin, so the
//     transport is a no-op here; the PC side is exercised by fake_xr_device
//     (SimulatedDeviceAdapter path) talking to the same /webrtc/offer endpoint.
//     This bridge still parses/echoes contract messages so UI wiring can be
//     developed in the editor against a locally injected message stream.
using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using Newtonsoft.Json;
using Unity.Collections;
using UnityEngine;

namespace MLOmega.XR.Transport
{
    /// <summary>Connection state surfaced to Unity; mirrors the Kotlin TransportState.</summary>
    public enum LiveTransportState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Degraded = 3,
        Reconnecting = 4
    }

    /// <summary>
    /// MonoBehaviour wrapper around the native transport. Assign the session
    /// credentials (from <see cref="SessionPairing"/>) and an
    /// <see cref="EyeCaptureSource"/>; call <see cref="StartTransport"/> once the
    /// session token is available.
    /// </summary>
    public sealed class LiveTransportBridge : MonoBehaviour
    {
        [SerializeField] private SessionPairing _pairing;
        [SerializeField] private EyeCaptureSource _capture;

        [Tooltip("Nominal capture width/height/fps advertised to the encoder.")]
        [SerializeField] private int _width = 1280;
        [SerializeField] private int _height = 720;
        [SerializeField] private int _fps = 30;

        [Tooltip("Feed frames as OES textures (zero-copy) vs I420 CPU readback.")]
        [SerializeField] private bool _textureBacked = false;

        private Texture2D _readback;
        private byte[] _i420;

        /// <summary>Raised on the main thread when the transport state changes.</summary>
        public event Action<LiveTransportState, string> StateChanged;

        /// <summary>Raised on the main thread for each UIIntent received downlink.</summary>
        public event Action<UIIntent> UiIntentReceived;

        /// <summary>Raised on the main thread with the raw downlink JSON before typed
        /// parsing â€” lets the DeviceCommandHandler (E33 Â§4) claim `device_command`
        /// messages, which are NOT UIIntents.</summary>
        public event Action<string> MessageReceived;

        /// <summary>Raised on the main thread with a raw stats JSON snapshot.</summary>
        public event Action<string> StatsReceived;

        /// <summary>Latest known state.</summary>
        public LiveTransportState State { get; private set; } = LiveTransportState.Disconnected;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _plugin;
        private AndroidJavaObject _feeder;
        private NativeCallbackProxy _proxy;
#endif

        // Main-thread dispatch: native callbacks arrive on background threads.
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();

        private void Awake()
        {
            if (_pairing == null) _pairing = FindAnyObjectByType<SessionPairing>();
            if (_capture == null) _capture = FindAnyObjectByType<EyeCaptureSource>();
        }

        private void OnEnable()
        {
            if (_capture != null) _capture.OnFrame += HandleFrame;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_pairing != null) _pairing.CredentialsChanged += RefreshCredentials;
#endif
        }

        private void OnDisable()
        {
            if (_capture != null) _capture.OnFrame -= HandleFrame;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_pairing != null) _pairing.CredentialsChanged -= RefreshCredentials;
#endif
            StopTransport();
        }

        private void Update()
        {
            // Drain native callbacks onto the Unity main thread.
            while (true)
            {
                Action work = null;
                lock (_queueLock)
                {
                    if (_mainThreadQueue.Count > 0) work = _mainThreadQueue.Dequeue();
                }
                if (work == null) break;
                try { work(); } catch (Exception ex) { Debug.LogError($"[LiveTransport] {ex}"); }
            }
        }

        /// <summary>
        /// Start the native transport. Requires a paired session (session id +
        /// token). No-op in editor/Windows (DIRECT_PYTHON): the PC-side loop is
        /// driven by fake_xr_device against the same signaling endpoint.
        /// </summary>
        public void StartTransport()
        {
            if (_pairing == null || string.IsNullOrEmpty(_pairing.SessionId))
            {
                Debug.LogWarning("[LiveTransport] no paired session; cannot start.");
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            StartAndroid();
#else
            Debug.Log("[LiveTransport] editor/Windows: DIRECT_PYTHON mode, native transport skipped. " +
                      "Drive the PC side with simulators/fake_xr_device against /webrtc/offer.");
            SetState(LiveTransportState.Disconnected, "editor-noop");
#endif
        }

        /// <summary>Stop and release the native transport.</summary>
        public void StopTransport()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _plugin?.Call("dispose");
            _plugin?.Dispose();
            _feeder?.Dispose();
            _plugin = null;
            _feeder = null;
            _proxy = null;
#endif
            SetState(LiveTransportState.Disconnected, "stopped");
        }

        /// <summary>
        /// Send a UIReceipt back up the reliable DataChannel (the device's ack of
        /// a UIIntent). No-op if the channel is not open.
        /// </summary>
        public bool SendReceipt(UIReceipt receipt)
        {
            string json = ContractJson.Serialize(receipt);
            return SendContractMessage(json);
        }

        /// <summary>Send any bounded contracts message over the reliable channel.</summary>
        public bool SendContractMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
#if UNITY_ANDROID && !UNITY_EDITOR
            return _plugin != null && _plugin.Call<bool>("sendContractMessage", json);
#else
            Debug.Log($"[LiveTransport] (editor) would send contract message: {json}");
            return false;
#endif
        }

        // --- E47-A: single-microphone fan-out + command-tagged transcripts -------

        /// <summary>
        /// E47-A. Attach the on-device speech pipeline's PCM sink (a Kotlin
        /// <c>com.mlomega.xr.livetransport.PcmFeed</c>) so sherpa consumes the SAME
        /// microphone PCM WebRTC captures — no second AudioRecord. The
        /// AndroidJavaObject is the sink returned by
        /// <c>AsrKwsService.asPcmSink()</c>. No-op in editor / before StartTransport.
        /// </summary>
#if UNITY_ANDROID && !UNITY_EDITOR
        public bool AttachPcmFeed(AndroidJavaObject feed)
        {
            if (_plugin == null || feed == null)
            {
                Debug.LogWarning("[LiveTransport] AttachPcmFeed: transport not started; feed dropped.");
                return false;
            }
            _plugin.Call("attachPcmFeed", feed);
            return true;
        }

        /// <summary>Detach a previously attached PCM feed.</summary>
        public void DetachPcmFeed(AndroidJavaObject feed)
        {
            if (_plugin == null || feed == null) return;
            _plugin.Call("detachPcmFeed", feed);
        }
#else
        public bool AttachPcmFeed(object feed)
        {
            Debug.Log("[LiveTransport] (editor) AttachPcmFeed no-op (DIRECT_PYTHON).");
            return false;
        }

        public void DetachPcmFeed(object feed) { }
#endif

        /// <summary>
        /// E47-A. Send a final device ASR segment up the reliable DataChannel with
        /// the additive <c>is_command</c> flag. Capture already reached the PC over
        /// WebRTC (life memory / hot context); this metadata tells the PC whether to
        /// ROUTE the segment as a command (wake word was active) or keep it as plain
        /// memory. Only finals are sent; partials render locally via SubtitleSkill.
        /// </summary>
        public bool SendTranscriptSegment(string text, string language, long startMs, long endMs, bool isCommand)
        {
            // Flat JSON matching the DataChannel convention; keyed distinctly from
            // ui_intent_id so OnNativeMessage's UIIntent path never claims it.
            string json =
                "{\"type\":\"device_transcript\"," +
                "\"segment_id\":" + JsonConvert.ToString($"device:{startMs}:{endMs}") + "," +
                "\"text\":" + JsonConvert.ToString(text ?? string.Empty) + "," +
                "\"language\":" + JsonConvert.ToString(language ?? string.Empty) + "," +
                "\"start_ms\":" + startMs + "," +
                "\"end_ms\":" + endMs + "," +
                "\"is_final\":true," +
                "\"is_command\":" + (isCommand ? "true" : "false") + "}";
#if UNITY_ANDROID && !UNITY_EDITOR
            return _plugin != null && _plugin.Call<bool>("sendContractMessage", json);
#else
            Debug.Log($"[LiveTransport] (editor) would send transcript: {json}");
            return false;
#endif
        }

        // --- frame feeding --------------------------------------------------------

        private void HandleFrame(Texture texture, FrameEnvelope envelope)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_feeder == null || texture == null) return;
            if (_plugin != null && envelope != null)
                _plugin.Call<bool>("sendContractMessage", ContractJson.Serialize(envelope));
            long tsNs = envelope != null ? envelope.CaptureMonotonicNs : 0L;
            long rotation = envelope != null ? envelope.Rotation : 0L;
            if (_capture != null &&
                _capture.TryGetCurrentNativeI420(
                    out Texture2D planeY,
                    out Texture2D planeU,
                    out Texture2D planeV) &&
                TryPackNativeI420(planeY, planeU, planeV, ref _i420))
            {
                PushPackedI420(_i420, planeY.width, planeY.height, (int)rotation, tsNs);
                return;
            }
            if (_textureBacked)
            {
                // GetNativeTexturePtr() -> GL texture name for the OES feeder path.
                int texId = (int)texture.GetNativeTexturePtr();
                // Identity transform; capture-only rotation is carried separately.
                float[] identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
                _feeder.Call("pushTextureFrame", texId, identity,
                    texture.width, texture.height, (int)rotation, tsNs);
            }
            // I420 readback path is wired by the capture pipeline when
            // _textureBacked is false; omitted here to avoid a per-frame GPU sync
            // on the hot path (see DECISIONS Â§E24).
            if (!_textureBacked) PushCpuI420(texture, (int)rotation, tsNs);
#endif
        }

        // --- Android plumbing -----------------------------------------------------

#if UNITY_ANDROID && !UNITY_EDITOR
        private void StartAndroid()
        {
            if (_plugin != null) return;
            var config = _pairing.Config;
            string offerUrl = !string.IsNullOrEmpty(_pairing.ActiveBaseUrl)
                ? _pairing.ActiveBaseUrl.TrimEnd('/') + "/webrtc/offer"
                : config != null ? config.WebrtcOfferUrl
                : "http://192.0.2.10:8710/webrtc/offer";

            using var context = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");

            // Build the Kotlin LiveTransportConfig (data class) via its constructor.
            var cfg = BuildConfig(offerUrl);
            _feeder = new AndroidJavaObject(
                "com.mlomega.xr.livetransport.UnityPushVideoFeeder",
                _width, _height, _fps, _textureBacked);
            _proxy = new NativeCallbackProxy(this);

            _plugin = new AndroidJavaObject(
                "com.mlomega.xr.livetransport.LiveTransportPlugin",
                context, cfg, _feeder, _proxy);
            _plugin.Call("start");
        }

        private void RefreshCredentials()
        {
            if (_plugin == null || _pairing == null || string.IsNullOrEmpty(_pairing.ActiveBaseUrl)) return;
            _plugin.Call("updateCredentials",
                _pairing.ActiveBaseUrl.TrimEnd('/') + "/webrtc/offer",
                _pairing.SessionId, _pairing.Token);
        }

        private void PushCpuI420(Texture source, int rotation, long timestampNs)
        {
            int width = source.width, height = source.height;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            if (_readback == null || _readback.width != width || _readback.height != height)
                _readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            _readback.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Color32[] rgba = _readback.GetPixels32();
            int cw = (width + 1) / 2, ch = (height + 1) / 2;
            int ySize = width * height, uvSize = cw * ch;
            if (_i420 == null || _i420.Length != ySize + uvSize * 2)
                _i420 = new byte[ySize + uvSize * 2];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color32 p = rgba[y * width + x];
                _i420[y * width + x] = (byte)Mathf.Clamp(((66 * p.r + 129 * p.g + 25 * p.b + 128) >> 8) + 16, 0, 255);
            }
            for (int y = 0; y < height; y += 2)
            for (int x = 0; x < width; x += 2)
            {
                int rs = 0, gs = 0, bs = 0, n = 0;
                for (int dy = 0; dy < 2 && y + dy < height; dy++)
                for (int dx = 0; dx < 2 && x + dx < width; dx++)
                {
                    Color32 p = rgba[(y + dy) * width + x + dx];
                    rs += p.r; gs += p.g; bs += p.b; n++;
                }
                int r = rs / n, g = gs / n, b = bs / n;
                int uv = (y / 2) * cw + x / 2;
                _i420[ySize + uv] = (byte)Mathf.Clamp(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128, 0, 255);
                _i420[ySize + uvSize + uv] = (byte)Mathf.Clamp(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128, 0, 255);
            }
            PushPackedI420(_i420, width, height, rotation, timestampNs);
        }

        private void PushPackedI420(
            byte[] packed,
            int width,
            int height,
            int rotation,
            long timestampNs)
        {
            using var byteBufferClass = new AndroidJavaClass("java.nio.ByteBuffer");
            using var byteBuffer = byteBufferClass.CallStatic<AndroidJavaObject>("wrap", packed);
            _feeder.Call("pushI420Frame", byteBuffer, width, height, rotation, timestampNs);
        }

        private AndroidJavaObject BuildConfig(string offerUrl)
        {
            // LiveTransportConfig has many defaulted fields; use the all-defaults
            // secondary path by constructing the nested defaults explicitly is
            // verbose, so we rely on the primary constructor with the required
            // args and Kotlin defaults filled by a small companion factory added
            // for JNI (LiveTransportConfig.forUnity). See DECISIONS Â§E24.
            return new AndroidJavaClass("com.mlomega.xr.livetransport.LiveTransportConfigFactory")
                .CallStatic<AndroidJavaObject>("forUnity",
                    offerUrl, _pairing.SessionId, _pairing.Token, _width, _height, _fps);
        }

        internal void EnqueueMainThread(Action action)
        {
            lock (_queueLock) { _mainThreadQueue.Enqueue(action); }
        }
#endif

        /// <summary>
        /// Packs the native XREAL Eye Alpha8 Y/U/V textures directly as I420.
        /// This avoids the PhoneOnly fallback's synchronous GPU readback and
        /// per-pixel RGB conversion. It stays platform-neutral for EditMode.
        /// </summary>
        public static bool TryPackNativeI420(
            Texture2D planeY,
            Texture2D planeU,
            Texture2D planeV,
            ref byte[] packed)
        {
            if (planeY == null || planeU == null || planeV == null)
                return false;
            int width = planeY.width, height = planeY.height;
            int chromaWidth = (width + 1) / 2;
            int chromaHeight = (height + 1) / 2;
            if (planeU.width != chromaWidth || planeU.height != chromaHeight ||
                planeV.width != chromaWidth || planeV.height != chromaHeight)
                return false;

            int ySize = width * height;
            int uvSize = chromaWidth * chromaHeight;
            NativeArray<byte> y = planeY.GetRawTextureData<byte>();
            NativeArray<byte> u = planeU.GetRawTextureData<byte>();
            NativeArray<byte> v = planeV.GetRawTextureData<byte>();
            if (y.Length < ySize || u.Length < uvSize || v.Length < uvSize)
                return false;

            int total = ySize + uvSize * 2;
            if (packed == null || packed.Length != total)
                packed = new byte[total];
            NativeArray<byte>.Copy(y, 0, packed, 0, ySize);
            NativeArray<byte>.Copy(u, 0, packed, ySize, uvSize);
            NativeArray<byte>.Copy(v, 0, packed, ySize + uvSize, uvSize);
            return true;
        }

        internal void OnNativeState(string stateName, string detail)
        {
            LiveTransportState mapped = stateName switch
            {
                "CONNECTING" => LiveTransportState.Connecting,
                "CONNECTED" => LiveTransportState.Connected,
                "DEGRADED" => LiveTransportState.Degraded,
                "RECONNECTING" => LiveTransportState.Reconnecting,
                _ => LiveTransportState.Disconnected
            };
            Enqueue(() => SetState(mapped, detail));
        }

        internal void OnNativeMessage(string json)
        {
            Enqueue(() =>
            {
                // Raw hook first: device_command messages (E33 Â§4) are claimed here
                // and must NOT be parsed as UIIntents.
                MessageReceived?.Invoke(json);
                if (json == null || json.IndexOf("\"ui_intent_id\"", StringComparison.Ordinal) < 0)
                {
                    return;
                }
                try
                {
                    var intent = ContractJson.Deserialize<UIIntent>(json);
                    if (intent != null) UiIntentReceived?.Invoke(intent);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LiveTransport] bad downlink json: {ex.Message}");
                }
            });
        }

        internal void OnNativeStats(string json) => Enqueue(() => StatsReceived?.Invoke(json));

        internal void OnNativeError(string message) =>
            Debug.LogWarning($"[LiveTransport] native error: {message}");

        private void Enqueue(Action action)
        {
            lock (_queueLock) { _mainThreadQueue.Enqueue(action); }
        }

        private void SetState(LiveTransportState next, string detail)
        {
            State = next;
            StateChanged?.Invoke(next, detail);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// AndroidJavaProxy implementing the Kotlin LiveTransportCallbacks interface.
        /// Marshals native callbacks back into the bridge (which re-dispatches to
        /// the Unity main thread).
        /// </summary>
        private sealed class NativeCallbackProxy : AndroidJavaProxy
        {
            private readonly LiveTransportBridge _bridge;

            public NativeCallbackProxy(LiveTransportBridge bridge)
                : base("com.mlomega.xr.livetransport.LiveTransportCallbacks")
            {
                _bridge = bridge;
            }

            // enum TransportState arrives as an AndroidJavaObject; read .name().
            void onStateChanged(AndroidJavaObject state, string detail)
            {
                string name = state != null ? state.Call<string>("name") : "DISCONNECTED";
                _bridge.OnNativeState(name, detail);
            }

            void onDataChannelMessage(string json) => _bridge.OnNativeMessage(json);
            void onStats(string json) => _bridge.OnNativeStats(json);
            void onError(string message) => _bridge.OnNativeError(message);
        }
#endif
    }
}
