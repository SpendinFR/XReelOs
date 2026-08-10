// MLOmega V19 — E48-A
// Unity-side bridge to the native Android device-model provisioning client
// (com.mlomega.xr.livetransport.ModelProvisioner). After pairing (endpoint +
// session token available) it kicks off a BACKGROUND download of any device
// models missing from getExternalFilesDir()/models/ — the two streaming ASR
// models are ~300-380 MB, so the small ones (KWS + MediaPipe .task) are embedded
// in the APK (StreamingAssets, copied at first launch) and only the ASR is
// fetched. Provisioning NEVER blocks the session: features whose model is still
// absent stay in honest degraded mode (the reflex bridges already gate on model
// presence), never a crash.
//
// Same DIRECT_ANDROID / editor split as LiveTransportBridge/AsrBridge: in the
// editor there is no Android plugin, so this is a no-op (device-only work).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>Coarse progress snapshot for the discreet provisioning card (StatusBar).</summary>
    public readonly struct ProvisioningProgress
    {
        public readonly string ModelName;
        public readonly long ReceivedBytes;
        public readonly long TotalBytes;
        /// <summary>0..1 fraction, or -1 when the total is unknown.</summary>
        public readonly float Fraction;

        public ProvisioningProgress(string name, long received, long total)
        {
            ModelName = name;
            ReceivedBytes = received;
            TotalBytes = total;
            Fraction = total > 0 ? Mathf.Clamp01((float)received / total) : -1f;
        }
    }

    /// <summary>
    /// Drives the native <c>ModelProvisioner</c> once pairing yields an endpoint +
    /// token. Re-emits plan/progress/ready/complete as C# events on the main thread
    /// so the StatusBar can show a glanceable download line. Attach next to
    /// <see cref="SessionPairing"/>.
    /// </summary>
    public sealed class ModelProvisioningBridge : MonoBehaviour
    {
        [SerializeField] private SessionPairing _pairing;

        [Tooltip("E48-A: installs APK-embedded models into files/models at first " +
                 "launch. Provisioning waits for it to finish so the two paths never " +
                 "race on the same file. Auto-found if left null.")]
        [SerializeField] private StreamingAssetsModelInstaller _installer;

        /// <summary>Manifest read: total device models advertised, and how many are missing.</summary>
        public event Action<int, int> PlanReceived;

        /// <summary>Per-model download progress (main thread).</summary>
        public event Action<ProvisioningProgress> Progress;

        /// <summary>One model finished + verified + installed (main thread).</summary>
        public event Action<string> ModelReady;

        /// <summary>Non-fatal provisioning error (name = "__manifest__" for the manifest fetch).</summary>
        public event Action<string, string> ProvisioningError;

        /// <summary>The whole provisioning pass finished (main thread).</summary>
        public event Action Completed;

        /// <summary>True from the first plan until Completed. Drives the StatusBar chip.</summary>
        public bool IsProvisioning { get; private set; }

        /// <summary>Latest coarse progress (for pollers like the StatusBar).</summary>
        public ProvisioningProgress LastProgress { get; private set; }

        /// <summary>Models finished this session (so a re-armed feature knows it can proceed).</summary>
        public IReadOnlyCollection<string> ReadyModels => _ready;

        private readonly HashSet<string> _ready = new HashSet<string>();
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();
        private bool _started;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _provisioner;
        private ProvisioningProxy _proxy;
#endif

        private void Awake()
        {
            if (_pairing == null) _pairing = FindAnyObjectByType<SessionPairing>();
            if (_installer == null) _installer = FindAnyObjectByType<StreamingAssetsModelInstaller>();
        }

        private void OnEnable()
        {
            if (_pairing != null)
            {
                _pairing.StateChanged += OnPairingState;
            }
            if (_installer != null) _installer.Completed += OnInstallerDone;
            // If we were enabled after pairing / install already happened, catch up.
            if (_pairing != null && _pairing.State == PairingState.Paired) TryStart();
        }

        private void OnDisable()
        {
            if (_pairing != null) _pairing.StateChanged -= OnPairingState;
            if (_installer != null) _installer.Completed -= OnInstallerDone;
        }

        private void OnInstallerDone() => TryStart();

        private void Update() => DrainMainThread();

        private void OnPairingState(PairingState state)
        {
            if (state == PairingState.Paired) TryStart();
        }

        /// <summary>
        /// Begin provisioning once (per app run). Requires a paired session and a
        /// resolved base URL. No-op if already started or not yet paired. The
        /// background download must run only once — a re-pair does not re-trigger it
        /// (the client itself re-checks what is missing, so a second run would be a
        /// no-op anyway, but we avoid the churn).
        /// </summary>
        public void TryStart()
        {
            if (_started) return;
            if (_pairing == null) return;
            // Wait for the APK-embedded models to be installed first, so the
            // installer and the downloader never race on the same file. When there
            // is no installer, proceed (nothing was embedded).
            if (_installer != null && !_installer.Done) return;
            if (!_pairing.TryGetActiveSession(out string sessionId, out string token)) return;
            string baseUrl = _pairing.ActiveBaseUrl;
            if (string.IsNullOrEmpty(baseUrl)) return;

            _started = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            StartAndroid(baseUrl, sessionId, token);
#else
            Debug.Log("[ModelProvisioning] editor: device provisioning is a no-op (models are " +
                      "pushed to files/models on device). DIRECT_PYTHON dev path.");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void StartAndroid(string baseUrl, string sessionId, string token)
        {
            try
            {
                using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var ctx = activity.Call<AndroidJavaObject>("getApplicationContext");

                _proxy = new ProvisioningProxy(this);
                // ModelProvisioner(context, callbacks) — models root + timeout default.
                _provisioner = new AndroidJavaObject(
                    "com.mlomega.xr.livetransport.ModelProvisioner", ctx, _proxy);
                _provisioner.Call("start", baseUrl, sessionId, token);
                Debug.Log($"[ModelProvisioning] started against {baseUrl}.");
            }
            catch (Exception ex)
            {
                // Provisioning failing to even start must never break the session.
                Debug.LogWarning($"[ModelProvisioning] could not start: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            try { _provisioner?.Call("stop"); } catch { /* best-effort */ }
            _provisioner?.Dispose();
            _provisioner = null;
            _proxy = null;
        }
#endif

        // --- native callbacks (background thread) → main thread -------------------

        internal void OnNativePlan(int total, int missing) => Enqueue(() =>
        {
            IsProvisioning = missing > 0;
            PlanReceived?.Invoke(total, missing);
        });

        internal void OnNativeProgress(string name, long received, long total) => Enqueue(() =>
        {
            var p = new ProvisioningProgress(name, received, total);
            LastProgress = p;
            Progress?.Invoke(p);
        });

        internal void OnNativeReady(string name) => Enqueue(() =>
        {
            _ready.Add(name);
            ModelReady?.Invoke(name);
        });

        internal void OnNativeError(string name, string message) => Enqueue(() =>
        {
            Debug.LogWarning($"[ModelProvisioning] {name}: {message}");
            ProvisioningError?.Invoke(name, message);
        });

        internal void OnNativeComplete() => Enqueue(() =>
        {
            IsProvisioning = false;
            Completed?.Invoke();
        });

        private void Enqueue(Action a) { lock (_queueLock) { _mainThreadQueue.Enqueue(a); } }

        private void DrainMainThread()
        {
            while (true)
            {
                Action work = null;
                lock (_queueLock) { if (_mainThreadQueue.Count > 0) work = _mainThreadQueue.Dequeue(); }
                if (work == null) break;
                try { work(); } catch (Exception ex) { Debug.LogError($"[ModelProvisioning] {ex}"); }
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class ProvisioningProxy : AndroidJavaProxy
        {
            private readonly ModelProvisioningBridge _bridge;
            public ProvisioningProxy(ModelProvisioningBridge b)
                : base("com.mlomega.xr.livetransport.ModelProvisioningCallbacks") { _bridge = b; }

            void onProvisioningPlan(int total, int missing) => _bridge.OnNativePlan(total, missing);
            void onModelProgress(string name, long received, long total) =>
                _bridge.OnNativeProgress(name, received, total);
            void onModelReady(string name) => _bridge.OnNativeReady(name);
            void onProvisioningError(string name, string message) =>
                _bridge.OnNativeError(name, message);
            void onProvisioningComplete() => _bridge.OnNativeComplete();
        }
#endif
    }
}
