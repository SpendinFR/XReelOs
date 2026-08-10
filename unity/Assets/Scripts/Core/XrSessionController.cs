// MLOmega V19 — E22 / Gate G1
// Session lifecycle: owns exactly one IXRDeviceAdapter, assigns a fresh
// timestamped session_id (uuid) per session, tracks plugged/unplugged state and
// performs automatic resumption when the device comes back.
using System;
using UnityEngine;

namespace MLOmega.XR.Core
{
    public enum XrSessionState
    {
        Idle = 0,
        Starting = 1,
        Running = 2,
        Suspended = 3, // device disconnected mid-session; waiting to resume
        Stopped = 4,
        Faulted = 5
    }

    /// <summary>
    /// Drives one XR session. In the editor it defaults to the simulated adapter;
    /// on device it uses the XREAL adapter. Chosen via <see cref="UseSimulator"/>.
    /// </summary>
    public sealed class XrSessionController : MonoBehaviour
    {
        [Tooltip("Force the simulated (webcam) adapter even in a player build. " +
                 "Leave off on device to use the real XREAL adapter. Ignored when a " +
                 "config with an explicit adapter kind is assigned below.")]
        [SerializeField] private bool _useSimulatorOverride;

        [Tooltip("Optional runtime config. When set, its Adapter field (xreal | " +
                 "simulated | phone_only | auto, mirroring configs/user_profile.yaml) " +
                 "selects the device adapter via AdapterSelector.")]
        [SerializeField] private MLOmegaConfig _config;
        [SerializeField] private PermissionGate _permissions;

        [Tooltip("Seconds between resume attempts while suspended.")]
        [SerializeField] private float _resumeIntervalSeconds = 1.0f;

        public XrSessionState State { get; private set; } = XrSessionState.Idle;

        /// <summary>Timestamped uuid, unique per session, never reused.</summary>
        public string SessionId { get; private set; }

        public IXRDeviceAdapter Adapter { get; private set; }

        public event Action<XrSessionState> SessionStateChanged;

        private float _lastResumeAttempt;
        private bool _wantEyeCapture = true;

        public bool UseSimulator =>
            _useSimulatorOverride || Application.isEditor;

        /// <summary>
        /// True when the active adapter renders a stereo rig; false for the flat 2D
        /// phone-only path. Consumers (overlay, UI runtime) branch rendering on this.
        /// </summary>
        public bool IsStereo => Adapter == null || Adapter.IsStereo;

        private void SetState(XrSessionState next)
        {
            if (State == next)
            {
                return;
            }
            State = next;
            SessionStateChanged?.Invoke(next);
        }

        private void Awake()
        {
            // Config-driven selection takes precedence (E23): the Adapter field maps
            // to configs/user_profile.yaml display/capture. Without a config we keep
            // the E22 behaviour (simulator in editor, XREAL on device).
            if (_config != null)
            {
                Adapter = AdapterSelector.Create(_config.Adapter);
            }
            else
            {
                Adapter = UseSimulator
                    ? (IXRDeviceAdapter)new SimulatedDeviceAdapter()
                    : new XrealDeviceAdapter();
            }
            Adapter.ConnectionStateChanged += OnConnectionStateChanged;
        }

        private void OnEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Adapter != null)
            {
                if (_permissions == null) _permissions = FindAnyObjectByType<PermissionGate>();
                if (_permissions != null && !_permissions.AllPermissionsGranted)
                {
                    _permissions.AllGranted += OnPermissionsGranted;
                    return;
                }
            }
#endif
            StartSession();
        }

        private void OnPermissionsGranted()
        {
            if (_permissions != null) _permissions.AllGranted -= OnPermissionsGranted;
            StartSession();
        }

        /// <summary>Begin a new session with a fresh session_id.</summary>
        public void StartSession()
        {
            if (State == XrSessionState.Running || State == XrSessionState.Starting)
            {
                return;
            }
            SessionId = NewSessionId();
            SetState(XrSessionState.Starting);
            try
            {
                Adapter.Initialize();
                if (_wantEyeCapture)
                {
                    Adapter.StartEyeCapture();
                }
                SetState(Adapter.ConnectionState == DeviceConnectionState.Connected
                    ? XrSessionState.Running
                    : XrSessionState.Suspended);
                Debug.Log($"[XrSessionController] Session '{SessionId}' started ({State}).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[XrSessionController] StartSession failed: {ex}");
                SetState(XrSessionState.Faulted);
            }
        }

        private void OnConnectionStateChanged(DeviceConnectionState deviceState)
        {
            switch (deviceState)
            {
                case DeviceConnectionState.Connected:
                    if (State == XrSessionState.Suspended || State == XrSessionState.Starting)
                    {
                        SetState(XrSessionState.Running);
                    }
                    break;
                case DeviceConnectionState.Disconnected:
                case DeviceConnectionState.Error:
                    if (State == XrSessionState.Running)
                    {
                        Debug.LogWarning("[XrSessionController] Device dropped; suspending session.");
                        SetState(XrSessionState.Suspended);
                    }
                    break;
            }
        }

        private void Update()
        {
            // Resume loop: while suspended, periodically re-initialize the adapter
            // so unplug -> replug restores the session (G1 exit criterion).
            if (State != XrSessionState.Suspended)
            {
                return;
            }
            if (Time.realtimeSinceStartup - _lastResumeAttempt < _resumeIntervalSeconds)
            {
                return;
            }
            _lastResumeAttempt = Time.realtimeSinceStartup;
            try
            {
                Adapter.Initialize();
                if (Adapter.ConnectionState == DeviceConnectionState.Connected)
                {
                    if (_wantEyeCapture && !Adapter.IsEyeActive)
                    {
                        Adapter.StartEyeCapture();
                    }
                    SetState(XrSessionState.Running);
                    Debug.Log($"[XrSessionController] Session '{SessionId}' resumed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XrSessionController] Resume attempt failed: {ex.Message}");
            }
        }

        public void StopSession()
        {
            if (Adapter != null)
            {
                try
                {
                    Adapter.Shutdown();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[XrSessionController] Shutdown threw: {ex.Message}");
                }
            }
            SetState(XrSessionState.Stopped);
        }

        /// <summary>Pause/resume camera capture without changing the XR/BrainLive
        /// session identity. Privacy pause is deliberately not an end-session.</summary>
        public bool SetEyeCapturePaused(bool paused)
        {
            _wantEyeCapture = !paused;
            if (Adapter == null) return false;
            try
            {
                if (paused)
                {
                    if (Adapter.IsEyeActive) Adapter.StopEyeCapture();
                }
                else if ((State == XrSessionState.Running || State == XrSessionState.Suspended) &&
                         !Adapter.IsEyeActive)
                {
                    Adapter.StartEyeCapture();
                }
                return paused ? !Adapter.IsEyeActive : Adapter.IsEyeActive;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XrSessionController] privacy capture toggle failed: {ex.Message}");
                return false;
            }
        }

        private void OnDisable()
        {
            if (_permissions != null) _permissions.AllGranted -= OnPermissionsGranted;
            StopSession();
        }

        private void OnDestroy()
        {
            if (Adapter != null)
            {
                Adapter.ConnectionStateChanged -= OnConnectionStateChanged;
                Adapter.Shutdown();
            }
        }

        /// <summary>Timestamped uuid: sortable prefix + full guid, never reused.</summary>
        private static string NewSessionId()
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
            return $"xrs-{stamp}-{Guid.NewGuid():N}";
        }
    }
}
