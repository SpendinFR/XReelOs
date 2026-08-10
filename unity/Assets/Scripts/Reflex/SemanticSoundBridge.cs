using System;
using System.Collections.Generic;
using System.IO;
using MLOmega.Contracts.V19;
using MLOmega.XR.Transport;
using MLOmega.XR.UI;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    /// <summary>
    /// Opt-in semantic sound overlay fed by the existing WebRTC microphone PCM.
    /// YAMNet runs on the Android device; no second microphone and no fake
    /// direction are introduced. Results render immediately and are also sent to
    /// the PC as bounded evidence for HotContext/BrainLive consumers.
    /// </summary>
    public sealed class SemanticSoundBridge : MonoBehaviour
    {
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private AugmentedRealityFeatureRegistry _features;
        [SerializeField] private LocalIntentSource _intentSource;
        [SerializeField] private string _modelRelativePath = "models/yamnet.tflite";
        [Range(0.3f, 0.9f)]
        [SerializeField] private float _minimumConfidence = 0.45f;
        [Range(2f, 30f)]
        [SerializeField] private float _cooldownSeconds = 8f;

        public bool IsRunning { get; private set; }
        private readonly Queue<Action> _mainThread = new Queue<Action>();
        private readonly object _queueLock = new object();

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _classifier;
        private AndroidJavaObject _pcmSink;
        private SoundProxy _proxy;
#endif

        private void Awake()
        {
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_features == null) _features = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
            if (_intentSource == null) _intentSource = FindAnyObjectByType<LocalIntentSource>();
        }

        private void OnEnable()
        {
            if (_features != null)
            {
                _features.FeatureChanged += OnFeatureChanged;
                _features.ServiceStatusChanged += OnServiceStatusChanged;
            }
            if (_transport != null) _transport.StateChanged += OnTransportStateChanged;
            RefreshState();
        }

        private void OnDisable()
        {
            if (_features != null)
            {
                _features.FeatureChanged -= OnFeatureChanged;
                _features.ServiceStatusChanged -= OnServiceStatusChanged;
            }
            if (_transport != null) _transport.StateChanged -= OnTransportStateChanged;
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
                catch (Exception ex)
                {
                    Debug.LogWarning("[SemanticSound] callback failed: " + ex.Message);
                }
            }
        }

        private void OnFeatureChanged(string _, bool __) => RefreshState();
        private void OnServiceStatusChanged(string _, string __) => RefreshState();
        private void OnTransportStateChanged(LiveTransportState _, string __) => RefreshState();

        private void RefreshState()
        {
            bool shouldRun = _features != null &&
                _features.IsActive(AugmentedRealityFeatureRegistry.SemanticSound) &&
                _transport != null &&
                (_transport.State == LiveTransportState.Connected ||
                 _transport.State == LiveTransportState.Degraded);
            if (shouldRun && !IsRunning) StartNative();
            else if (!shouldRun && IsRunning) StopNative();
        }

        private void StartNative()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var context = activity.Call<AndroidJavaObject>("getApplicationContext");
                using var extDir = context.Call<AndroidJavaObject>(
                    "getExternalFilesDir", (object)null);
                string root = extDir != null
                    ? extDir.Call<string>("getAbsolutePath")
                    : context.Call<AndroidJavaObject>("getFilesDir")
                        .Call<string>("getAbsolutePath");
                string modelPath = Path.Combine(root, _modelRelativePath);
                if (!File.Exists(modelPath))
                    throw new FileNotFoundException("YAMNet non provisionné", modelPath);
                _proxy = new SoundProxy(this);
                _classifier = new AndroidJavaObject(
                    "com.mlomega.xr.reflexvision.SemanticSoundClassifier",
                    context,
                    modelPath,
                    _proxy,
                    _minimumConfidence,
                    (long)(_cooldownSeconds * 1000f));
                _pcmSink = _classifier.Call<AndroidJavaObject>("asPcmSink");
                if (!_transport.AttachPcmFeed(_pcmSink))
                    throw new InvalidOperationException("fan-out PCM indisponible");
                IsRunning = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SemanticSound] unavailable: " + ex.Message);
                StopNative();
            }
#else
            IsRunning = true;
#endif
        }

        private void StopNative()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_pcmSink != null && _transport != null)
                    _transport.DetachPcmFeed(_pcmSink);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SemanticSound] PCM detach failed: " + ex.Message);
            }
            try { _pcmSink?.Dispose(); } catch { }
            _pcmSink = null;
            try { _classifier?.Call("close"); } catch { }
            try { _classifier?.Dispose(); } catch { }
            _classifier = null;
            _proxy = null;
#endif
            IsRunning = false;
        }

        internal void OnNativeSound(string label, float score, long timestampMs)
        {
            string display = DisplayLabel(label);
            _intentSource?.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = "v19.0",
                UiIntentId = $"semantic-sound-{timestampMs}-{label}",
                Producer = "ultralive",
                Component = "context_card",
                Anchor = new Dictionary<string, object>
                {
                    ["type"] = "head_locked",
                    ["side"] = "left",
                },
                Content = new Dictionary<string, object>
                {
                    ["kind"] = "semantic_sound",
                    ["title"] = "SON // " + display,
                    ["text"] = "Événement sonore détecté maintenant.",
                    ["direction"] = "unknown",
                },
                TruthLevel = "observed",
                Confidence = Math.Max(0d, Math.Min(1d, score)),
                Priority = IsSafetySound(label) ? 0.94 : 0.72,
                TtlMs = IsSafetySound(label) ? 7_000 : 4_000,
                EvidenceRefs = new List<string> { $"device_audio:{timestampMs}" },
            });
            if (_transport == null) return;
            string message =
                "{\"type\":\"device_semantic_sound\"," +
                "\"label\":" + Newtonsoft.Json.JsonConvert.ToString(label) + "," +
                "\"confidence\":" + score.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "," +
                "\"captured_at_ms\":" + timestampMs + "," +
                "\"direction\":\"unknown\"}";
            _transport.SendContractMessage(message);
        }

        private void Enqueue(Action action)
        {
            lock (_queueLock) _mainThread.Enqueue(action);
        }

        private static bool IsSafetySound(string label) =>
            label == "glass_breaking" || label == "smoke_alarm" || label == "siren";

        private static string DisplayLabel(string label)
        {
            switch (label)
            {
                case "glass_breaking": return "VERRE BRISÉ";
                case "smoke_alarm": return "ALARME FUMÉE";
                case "siren": return "SIRÈNE";
                case "doorbell": return "SONNETTE";
                case "baby_cry": return "BÉBÉ";
                case "dog_bark": return "CHIEN";
                case "engine": return "MOTEUR";
                case "footsteps": return "PAS";
                default: return (label ?? "INCONNU").ToUpperInvariant();
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class SoundProxy : AndroidJavaProxy
        {
            private readonly SemanticSoundBridge _owner;
            public SoundProxy(SemanticSoundBridge owner)
                : base("com.mlomega.xr.reflexvision.SemanticSoundCallbacks")
            {
                _owner = owner;
            }
            void onSound(string label, float score, long timestampMs) =>
                _owner.Enqueue(() => _owner.OnNativeSound(label, score, timestampMs));
            void onError(string message) =>
                _owner.Enqueue(() => Debug.LogWarning("[SemanticSound] " + message));
        }
#endif
    }
}
