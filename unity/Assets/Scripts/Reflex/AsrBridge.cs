// MLOmega V19 — E26
// Unity-side bridge to the native Android speech pipeline
// (com.mlomega.xr.reflexvision.AsrKwsService, sherpa-onnx VAD + streaming
// zipformer ASR + KeywordSpotter). Owns the AndroidJavaObject, activates on
// demand (§9.4), and re-emits transcripts + wake-word hits as C# events on the
// main thread.
//
// Editor / Windows dev has no Android plugin, so a REAL editor input path runs
// instead: it captures the Windows microphone via Unity's Microphone API and
// runs a basic energy VAD, so the wake-word gate and subtitle skill can be
// developed without a device. When no microphone is present it falls back to a
// keyboard trigger (K = wake word). Same DIRECT_ANDROID / editor split as
// LiveTransportBridge (DECISIONS §E24/§E26).
using System;
using System.Collections.Generic;
using MLOmega.XR.Core;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    /// <summary>A streaming ASR result surfaced to the reflex layer (main thread).</summary>
    public readonly struct TranscriptEvent
    {
        public readonly string Text;
        public readonly bool IsFinal;
        public readonly string Language;
        public readonly long StartMs;
        public readonly long EndMs;

        /// <summary>
        /// E47-A: true only for a final segment that ended inside the wake-word
        /// command window. Capture always continues to the PC (life memory); this
        /// flag tells the PC to ROUTE the segment as a command. Always false for
        /// partials.
        /// </summary>
        public readonly bool IsCommand;

        public TranscriptEvent(string text, bool isFinal, string language, long startMs, long endMs, bool isCommand)
        {
            Text = text;
            IsFinal = isFinal;
            Language = language;
            StartMs = startMs;
            EndMs = endMs;
            IsCommand = isCommand;
        }
    }

    public sealed class AsrBridge : MonoBehaviour
    {
        [SerializeField] private MLOmegaConfig _config;

#if UNITY_EDITOR
        [SerializeField] private bool _editorMicrophoneEnabled = true;
#endif

        [Tooltip("E48-A: relative dir (under getExternalFilesDir()/models/) of the EN " +
                 "streaming ASR model. Matches the provisioned/extracted sherpa dir.")]
        [SerializeField] private string _asrModelDirEn = "models/sherpa-onnx-streaming-zipformer-en-2023-06-26";

        [Tooltip("E48-A: relative dir (under getExternalFilesDir()/models/) of the FR " +
                 "streaming ASR model. Matches the provisioned/extracted sherpa dir.")]
        [SerializeField] private string _asrModelDirFr = "models/sherpa-onnx-streaming-zipformer-fr-2023-04-14";

        [Tooltip("Relative path (under getExternalFilesDir()/models/) of the Silero VAD " +
                 "model. NOT in the device manifest yet — see DECISIONS §E48-A gap note.")]
        [SerializeField] private string _vadRelativePath = "models/silero_vad.onnx";

        [Tooltip("E48-A: relative dir (under getExternalFilesDir()/models/) of the " +
                 "KeywordSpotter model. Matches the provisioned/extracted KWS dir.")]
        [SerializeField] private string _kwsModelRelativeDir = "models/sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01";

        [Tooltip("E47-A: the live transport that owns the single microphone. On " +
                 "device, AsrKwsService consumes its PCM fan-out instead of opening " +
                 "a second AudioRecord. Auto-found if left null.")]
        [SerializeField] private MLOmega.XR.Transport.LiveTransportBridge _transport;

        /// <summary>Raised on the main thread for each partial/final transcript.</summary>
        public event Action<TranscriptEvent> Transcript;

        /// <summary>Raised on the main thread when the configured wake word is spotted.</summary>
        public event Action<string, long> WakeWordSpotted;

        public bool IsRunning { get; private set; }

        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _service;
        private AsrProxy _proxy;
        private AndroidJavaObject _pcmSink;
        private bool _usingOwnMicrophone;
#endif

        // E58: the PC-pushed wake word override (runtime, no rebuild). Stored so it is
        // (re)applied if the ASR service starts after the push arrives.
        private string _pendingWakeWord;
        private MLOmega.XR.UI.DeviceCommandHandler _commands;

        private void Awake()
        {
            if (_config == null) _config = FindAnyObjectByType<SessionPairing>()?.Config;
            if (_transport == null) _transport = FindAnyObjectByType<MLOmega.XR.Transport.LiveTransportBridge>();
            if (_commands == null) _commands = FindAnyObjectByType<MLOmega.XR.UI.DeviceCommandHandler>();
            if (_commands != null) _commands.SetWakeWordRequested += SetWakeWord;
        }

        private void OnDestroy()
        {
            if (_commands != null) _commands.SetWakeWordRequested -= SetWakeWord;
        }

        private void OnEnable()
        {
            if (_transport != null) _transport.StateChanged += OnTransportStateChanged;
        }

        /// <summary>
        /// E58: apply a new wake word at runtime (PC push, no APK rebuild). It is
        /// detected in the on-device ASR transcript (natural pronunciation). Stored so
        /// it also applies if the service starts later. Empty is ignored (never
        /// silently disables the wake word).
        /// </summary>
        public void SetWakeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;
            _pendingWakeWord = word.Trim();
#if UNITY_ANDROID && !UNITY_EDITOR
            _service?.Call("setWakeWord", _pendingWakeWord);
#endif
        }

        private void Update()
        {
            DrainMainThread();
#if UNITY_EDITOR
            if (IsRunning) SimulateFromMicrophone();
#endif
        }

        /// <summary>Activate the speech pipeline (mic + models). Called by scheduler/WakeWordGate. Idempotent.</summary>
        public void Activate()
        {
            if (IsRunning) return;
            IsRunning = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            // E48-A: if the sherpa models are still being provisioned (or absent),
            // native construction throws. Reset IsRunning so the scheduler retries on
            // a later tick / next launch once the models land — honest degraded, no
            // stuck state, no crash. Capture (WebRTC uplink) is unaffected.
            try
            {
                StartAndroid();
            }
            catch (Exception ex)
            {
                StopAndroid();
                IsRunning = false;
                Debug.LogWarning($"[AsrBridge] activation deferred (models not ready?): {ex.Message}");
            }
#else
            if (_editorMicrophoneEnabled) StartEditorMic();
#endif
        }

        /// <summary>Deactivate the pipeline (release mic + models — §9.4). Idempotent.</summary>
        public void Deactivate()
        {
            if (!IsRunning) return;
            IsRunning = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            StopAndroid();
#else
            StopEditorMic();
#endif
        }

        /// <summary>Arm/disarm the wake-word spotter without tearing down ASR.</summary>
        public void SetWakeWordArmed(bool armed)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _service?.Call("setWakeWordArmed", armed);
#else
            _editorKwsArmed = armed;
#endif
        }

        private void OnDisable()
        {
            if (_transport != null) _transport.StateChanged -= OnTransportStateChanged;
            Deactivate();
        }

        private void OnTransportStateChanged(MLOmega.XR.Transport.LiveTransportState state, string _)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsRunning) return;
            bool shouldOwn = state != MLOmega.XR.Transport.LiveTransportState.Connected &&
                             state != MLOmega.XR.Transport.LiveTransportState.Degraded;
            if (shouldOwn == _usingOwnMicrophone) return;
            Deactivate();
            Activate();
#endif
        }

        // --- native plumbing ------------------------------------------------------

#if UNITY_ANDROID && !UNITY_EDITOR
        private void StartAndroid()
        {
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var ctx = activity.Call<AndroidJavaObject>("getApplicationContext");
            // E48-A: models are provisioned to getExternalFilesDir()/models/ (same
            // dir as gestures, mirroring the repo models/device/ layout). Fall back
            // to getFilesDir() if the external dir is unavailable.
            using var extDir = ctx.Call<AndroidJavaObject>("getExternalFilesDir", (object)null);
            string filesDir = extDir != null
                ? extDir.Call<string>("getAbsolutePath")
                : ctx.Call<AndroidJavaObject>("getFilesDir").Call<string>("getAbsolutePath");

            bool isFr = _config != null && _config.AsrLanguage == ReflexAsrLanguage.Fr;
            string lang = isFr ? "FR" : "EN";
            string asrDir = isFr ? _asrModelDirFr : _asrModelDirEn;
            string wake = _config != null ? _config.WakeWord : "omega";
            long commandWindowMs = (long)((_config != null ? _config.CommandWindowSeconds : 6f) * 1000f);
            bool ownMicrophone = _transport == null ||
                (_transport.State != MLOmega.XR.Transport.LiveTransportState.Connected &&
                 _transport.State != MLOmega.XR.Transport.LiveTransportState.Degraded);
            using var langEnum = new AndroidJavaClass("com.mlomega.xr.reflexvision.AsrLanguage")
                .CallStatic<AndroidJavaObject>("valueOf", lang);

            // E47-A: build the config via the JNI factory (Kotlin default args are
            // not reachable through the JNI bridge). ownMicrophone=false → the
            // service consumes the transport's PCM fan-out, no second AudioRecord.
            using var cfg = new AndroidJavaClass("com.mlomega.xr.reflexvision.AsrKwsConfigFactory")
                .CallStatic<AndroidJavaObject>("forUnity",
                    langEnum,
                    filesDir + "/" + asrDir,
                    filesDir + "/" + _vadRelativePath,
                    filesDir + "/" + _kwsModelRelativeDir,
                    wake,
                    commandWindowMs,
                    ownMicrophone);

            _proxy = new AsrProxy(this);
            _service = new AndroidJavaObject(
                "com.mlomega.xr.reflexvision.AsrKwsService", ctx, cfg, _proxy);
            _service.Call("start");
            _usingOwnMicrophone = ownMicrophone;
            // E58: apply a wake word the PC pushed before the service was up.
            if (!string.IsNullOrEmpty(_pendingWakeWord)) _service.Call("setWakeWord", _pendingWakeWord);

            // E47-A single-mic arbitration: hand the transport's WebRTC-captured
            // PCM to sherpa. asPcmSink() returns a livetransport-shaped PcmFeed the
            // plugin's JavaAudioDeviceModule fan-out pushes into.
            if (!ownMicrophone)
            {
                _pcmSink = _service.Call<AndroidJavaObject>("asPcmSink");
                if (_transport == null || !_transport.AttachPcmFeed(_pcmSink))
                    throw new InvalidOperationException("transport PCM fan-out unavailable");
            }
        }

        private void StopAndroid()
        {
            try
            {
                if (_pcmSink != null && _transport != null)
                    _transport.DetachPcmFeed(_pcmSink);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AsrBridge] PCM detach failed: " + ex.Message);
            }
            try { _pcmSink?.Dispose(); } catch { }
            _pcmSink = null;
            try { _service?.Call("stop"); } catch { }
            try { _service?.Dispose(); } catch { }
            _service = null;
            _proxy = null;
            _usingOwnMicrophone = false;
        }
#endif

        internal void OnNativeTranscript(string text, bool isFinal, string lang, long startMs, long endMs, bool isCommand) =>
            Enqueue(() =>
            {
                Transcript?.Invoke(new TranscriptEvent(text, isFinal, lang, startMs, endMs, isCommand));
                // E47-A: forward the FINAL segment to the PC with the is_command
                // flag (capture already streamed as audio; this is the routing hint).
                if (isFinal && _transport != null)
                    _transport.SendTranscriptSegment(text, lang, startMs, endMs, isCommand);
            });

        internal void OnNativeWakeWord(string keyword, long tsMs) =>
            Enqueue(() => WakeWordSpotted?.Invoke(keyword, tsMs));

        internal void OnNativeError(string message) =>
            Debug.LogWarning($"[AsrBridge] native error: {message}");

        private void Enqueue(Action a) { lock (_queueLock) { _mainThreadQueue.Enqueue(a); } }

        private void DrainMainThread()
        {
            while (true)
            {
                Action work = null;
                lock (_queueLock) { if (_mainThreadQueue.Count > 0) work = _mainThreadQueue.Dequeue(); }
                if (work == null) break;
                try { work(); } catch (Exception ex) { Debug.LogError($"[AsrBridge] {ex}"); }
            }
        }

        /// <summary>
        /// Inject a transcript directly (EditMode tests / demo driver): proves the
        /// SubtitleSkill path without any device/native pipeline.
        /// </summary>
        public void InjectTranscript(TranscriptEvent ev) => Transcript?.Invoke(ev);

        /// <summary>Inject a wake-word hit directly (EditMode tests / demo driver).</summary>
        public void InjectWakeWord(string keyword, long tsMs) => WakeWordSpotted?.Invoke(keyword, tsMs);

        // --- editor microphone VAD (real, not a stub) -----------------------------

#if UNITY_EDITOR
        private AudioClip _micClip;
        private string _micDevice;
        private int _lastMicPos;
        private bool _editorSpeaking;
        private bool _editorKwsArmed = true;
        private float[] _micBuffer;
        private const int EditorSampleRate = 16000;
        private const float VadEnergyThreshold = 0.0025f;
        // E47-A: editor mirror of the native command window so IsCommand can be
        // exercised without a device (the K key opens it for CommandWindowSeconds).
        private double _editorCommandUntil;

        private void StartEditorMic()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.Log("[AsrBridge] editor: no microphone; press K to simulate the wake word.");
                return;
            }
            _micDevice = Microphone.devices[0];
            _micClip = Microphone.Start(_micDevice, true, 1, EditorSampleRate);
            _micBuffer = new float[EditorSampleRate / 10];
            _lastMicPos = 0;
        }

        private void StopEditorMic()
        {
            if (!string.IsNullOrEmpty(_micDevice)) Microphone.End(_micDevice);
            _micClip = null;
            _micDevice = null;
        }

        private void SimulateFromMicrophone()
        {
            if (Input.GetKeyDown(KeyCode.K) && _editorKwsArmed)
            {
                long now = (long)(Time.unscaledTimeAsDouble * 1000.0);
                float windowSec = _config != null ? _config.CommandWindowSeconds : 6f;
                _editorCommandUntil = Time.unscaledTimeAsDouble + windowSec;
                WakeWordSpotted?.Invoke(_config != null ? _config.WakeWord : "omega", now);
                return;
            }
            if (_micClip == null) return;

            int pos = Microphone.GetPosition(_micDevice);
            if (pos < 0 || pos == _lastMicPos) return;
            int count = pos - _lastMicPos;
            if (count < 0) count += _micClip.samples; // wrapped
            if (count < _micBuffer.Length) return;

            _micClip.GetData(_micBuffer, _lastMicPos % _micClip.samples);
            _lastMicPos = pos;

            // Basic energy VAD: RMS over the last 100 ms window.
            float sum = 0f;
            for (int i = 0; i < _micBuffer.Length; i++) sum += _micBuffer[i] * _micBuffer[i];
            float rms = Mathf.Sqrt(sum / _micBuffer.Length);

            long nowMs = (long)(Time.unscaledTimeAsDouble * 1000.0);
            bool speaking = rms > VadEnergyThreshold;
            if (speaking && !_editorSpeaking)
            {
                _editorSpeaking = true;
                Transcript?.Invoke(new TranscriptEvent("…", false,
                    _config != null && _config.AsrLanguage == ReflexAsrLanguage.Fr ? "fr" : "en",
                    nowMs, nowMs, false));
            }
            else if (!speaking && _editorSpeaking)
            {
                _editorSpeaking = false;
                bool isCommand = Time.unscaledTimeAsDouble <= _editorCommandUntil;
                Transcript?.Invoke(new TranscriptEvent("(speech segment)", true,
                    _config != null && _config.AsrLanguage == ReflexAsrLanguage.Fr ? "fr" : "en",
                    nowMs, nowMs, isCommand));
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AsrProxy : AndroidJavaProxy
        {
            private readonly AsrBridge _bridge;
            public AsrProxy(AsrBridge b)
                : base("com.mlomega.xr.reflexvision.AsrKwsCallbacks") { _bridge = b; }

            void onTranscript(string text, bool isFinal, string language, long startMs, long endMs, bool isCommand) =>
                _bridge.OnNativeTranscript(text, isFinal, language, startMs, endMs, isCommand);
            void onWakeWord(string keyword, long tsMs) => _bridge.OnNativeWakeWord(keyword, tsMs);
            void onError(string message) => _bridge.OnNativeError(message);
        }
#endif
    }
}
