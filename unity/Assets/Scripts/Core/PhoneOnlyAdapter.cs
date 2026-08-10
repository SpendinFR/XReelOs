// MLOmega V19 — E23
// Third real IXRDeviceAdapter: the phone's REAR camera via WebCamTexture, no 6DoF
// (pose = identity, not tracking), IsStereo=false so the session renders full-screen
// 2D instead of a stereo rig. This is the first-class "phone-only" target of the
// handoff (§3.5), a real product path, NOT a debug fallback: the phone both captures
// (camera) and displays (2D UI) with the same contracts as the glasses.
using System;
using System.Diagnostics;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Rear-camera adapter for the phone-only profile. Resolution/fps are
    /// configurable; if the rear camera cannot be opened it surfaces an error state
    /// (unlike the simulator, it does not fabricate frames — phone-only is a real
    /// capture path and a dead camera must be visible).
    /// </summary>
    public sealed class PhoneOnlyAdapter : IXRDeviceAdapter, IDisposable
    {
        private readonly int _requestedWidth;
        private readonly int _requestedHeight;
        private readonly int _requestedFps;

        private DeviceConnectionState _state = DeviceConnectionState.Unknown;
        private bool _eyeActive;
        private long _frameId;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private WebCamTexture _webcam;
        private string _cameraName;

        public PhoneOnlyAdapter(int width = 1280, int height = 720, int fps = 30)
        {
            _requestedWidth = Mathf.Max(64, width);
            _requestedHeight = Mathf.Max(64, height);
            _requestedFps = Mathf.Max(1, fps);
        }

        public DeviceConnectionState ConnectionState => _state;
        public string DeviceName { get; private set; } = "Phone (rear camera)";
        public bool IsEyeActive => _eyeActive;
        // Phone-only renders a flat 2D view, never a stereo rig.
        public bool IsStereo => false;
        public string FrameSource => ContractDefaults.FrameSource.PhoneCamera;
        public Texture PreviewTexture => _webcam;
        public int PreviewRotationAngle => _webcam != null ? _webcam.videoRotationAngle : 0;
        public bool PreviewVerticallyMirrored => _webcam != null && _webcam.videoVerticallyMirrored;

        public event Action<DeviceConnectionState> ConnectionStateChanged;

        private static long NanosPerTick => 1_000_000_000L / Stopwatch.Frequency;

        private void SetState(DeviceConnectionState next)
        {
            if (_state == next)
            {
                return;
            }
            _state = next;
            try
            {
                ConnectionStateChanged?.Invoke(next);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PhoneOnlyAdapter] state handler threw: {ex}");
            }
        }

        public void Initialize()
        {
            SetState(DeviceConnectionState.Connecting);
            _cameraName = PickRearCamera();
            if (_cameraName == null)
            {
                DeviceName = "Phone (no camera found)";
                SetState(DeviceConnectionState.Error);
                UnityEngine.Debug.LogError("[PhoneOnlyAdapter] No camera device available.");
                return;
            }
            DeviceName = $"Phone ({_cameraName})";
            SetState(DeviceConnectionState.Connected);
        }

        /// <summary>
        /// Prefer a back-facing camera; fall back to the first available device so a
        /// front-only device still works. Returns null if there is no camera at all.
        /// </summary>
        private static string PickRearCamera()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                return null;
            }
            foreach (WebCamDevice d in devices)
            {
                if (!d.isFrontFacing)
                {
                    return d.name;
                }
            }
            return devices[0].name;
        }

        public void StartEyeCapture()
        {
            if (_eyeActive || _cameraName == null)
            {
                return;
            }
            try
            {
                _webcam = new WebCamTexture(_cameraName, _requestedWidth, _requestedHeight, _requestedFps);
                _webcam.Play();
                _eyeActive = true;
                _frameId = 0;
                UnityEngine.Debug.Log(
                    $"[PhoneOnlyAdapter] Rear camera '{_cameraName}' started ({_requestedWidth}x{_requestedHeight}@{_requestedFps}).");
            }
            catch (Exception ex)
            {
                _eyeActive = false;
                _webcam = null;
                SetState(DeviceConnectionState.Error);
                UnityEngine.Debug.LogError($"[PhoneOnlyAdapter] Camera start failed: {ex.Message}");
            }
        }

        public void StopEyeCapture()
        {
            if (!_eyeActive)
            {
                return;
            }
            _eyeActive = false;
            if (_webcam != null)
            {
                _webcam.Stop();
                UnityEngine.Object.Destroy(_webcam);
                _webcam = null;
            }
        }

        /// <summary>Phone-only has no 6DoF tracking; pose is always identity/untracked.</summary>
        public PoseSample GetPose() => PoseSample.Untracked;

        public EyeFrame? TryGetLatestFrame()
        {
            if (!_eyeActive || _webcam == null)
            {
                return null;
            }
            if (!_webcam.didUpdateThisFrame || _webcam.width <= 16)
            {
                return null; // camera not warmed up yet
            }
            long monotonicNs = _clock.ElapsedTicks * NanosPerTick;
            _frameId++;
            return new EyeFrame(_webcam, _frameId, monotonicNs);
        }

        public void Shutdown()
        {
            StopEyeCapture();
            SetState(DeviceConnectionState.Disconnected);
        }

        public void Dispose() => Shutdown();
    }
}
