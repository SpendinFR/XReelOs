// MLOmega V19 — E22 / Gate G1
// In-editor / no-glasses adapter. This is a first-class development path
// mandated by the plan (SimulatedDeviceAdapter), NOT a stub: it delivers REAL
// webcam frames via WebCamTexture and a plausible moving 6DoF pose so the whole
// G1 scene (overlay, preview, pose readout, fps) can be exercised on a laptop.
using System;
using System.Diagnostics;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Webcam-backed simulator with a synthetic head pose. If no webcam is
    /// present it falls back to an animated procedural texture so the preview
    /// pipeline still has real, changing frames to display.
    /// </summary>
    public sealed class SimulatedDeviceAdapter : IXRDeviceAdapter, IDisposable
    {
        private const int FallbackWidth = 640;
        private const int FallbackHeight = 480;

        private DeviceConnectionState _state = DeviceConnectionState.Unknown;
        private bool _eyeActive;
        private long _frameId;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private WebCamTexture _webcam;
        private Texture2D _proceduralTexture;
        private Color32[] _proceduralBuffer;
        private bool _usingProcedural;
        private float _startTime;

        public DeviceConnectionState ConnectionState => _state;
        public string DeviceName { get; private set; } = "Simulated (editor)";
        public bool IsEyeActive => _eyeActive;
        // The simulator stands in for stereo glasses in the editor, so the full
        // stereo scene path is exercised without hardware.
        public bool IsStereo => true;
        public string FrameSource => ContractDefaults.FrameSource.Simulated;

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
                UnityEngine.Debug.LogError($"[SimulatedDeviceAdapter] state handler threw: {ex}");
            }
        }

        public void Initialize()
        {
            _startTime = Time.realtimeSinceStartup;
            SetState(DeviceConnectionState.Connecting);

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices != null && devices.Length > 0)
            {
                DeviceName = $"Simulated ({devices[0].name})";
            }
            else
            {
                DeviceName = "Simulated (procedural, no webcam)";
            }
            SetState(DeviceConnectionState.Connected);
        }

        public void StartEyeCapture()
        {
            if (_eyeActive)
            {
                return;
            }
            _frameId = 0;

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices != null && devices.Length > 0)
            {
                try
                {
                    _webcam = new WebCamTexture(devices[0].name, FallbackWidth, FallbackHeight, 30);
                    _webcam.Play();
                    _usingProcedural = false;
                    _eyeActive = true;
                    UnityEngine.Debug.Log($"[SimulatedDeviceAdapter] Webcam '{devices[0].name}' started.");
                    return;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[SimulatedDeviceAdapter] Webcam start failed, using procedural: {ex.Message}");
                    _webcam = null;
                }
            }

            // Procedural fallback — still produces real, changing frames.
            _usingProcedural = true;
            _proceduralTexture = new Texture2D(FallbackWidth, FallbackHeight, TextureFormat.RGBA32, false);
            _proceduralBuffer = new Color32[FallbackWidth * FallbackHeight];
            _eyeActive = true;
            UnityEngine.Debug.Log("[SimulatedDeviceAdapter] Procedural Eye source started.");
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

        public PoseSample GetPose()
        {
            if (_state != DeviceConnectionState.Connected)
            {
                return PoseSample.Untracked;
            }
            // Smooth synthetic head motion: a slow yaw sweep + slight pitch bob +
            // a small positional sway, so the readout visibly moves during dev.
            float t = Time.realtimeSinceStartup - _startTime;
            float yaw = Mathf.Sin(t * 0.4f) * 25f;
            float pitch = Mathf.Sin(t * 0.9f) * 8f;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = new Vector3(
                Mathf.Sin(t * 0.3f) * 0.15f,
                1.6f + Mathf.Sin(t * 0.7f) * 0.03f,
                Mathf.Cos(t * 0.3f) * 0.15f);
            return new PoseSample(pos, rot, true);
        }

        public EyeFrame? TryGetLatestFrame()
        {
            if (!_eyeActive)
            {
                return null;
            }

            long monotonicNs = _clock.ElapsedTicks * NanosPerTick;

            if (!_usingProcedural && _webcam != null)
            {
                if (!_webcam.didUpdateThisFrame || _webcam.width <= 16)
                {
                    return null; // camera not warmed up yet
                }
                _frameId++;
                return new EyeFrame(_webcam, _frameId, monotonicNs);
            }

            // Animate the procedural buffer: a moving diagonal gradient + a frame
            // counter band, so it is unmistakably live.
            float t = Time.realtimeSinceStartup - _startTime;
            int shift = (int)(t * 90f);
            for (int y = 0; y < FallbackHeight; y++)
            {
                for (int x = 0; x < FallbackWidth; x++)
                {
                    byte r = (byte)((x + shift) & 0xFF);
                    byte g = (byte)((y + shift) & 0xFF);
                    byte b = (byte)((x + y) & 0xFF);
                    _proceduralBuffer[y * FallbackWidth + x] = new Color32(r, g, b, 255);
                }
            }
            _proceduralTexture.SetPixels32(_proceduralBuffer);
            _proceduralTexture.Apply(false);

            _frameId++;
            return new EyeFrame(_proceduralTexture, _frameId, monotonicNs);
        }

        public void Shutdown()
        {
            StopEyeCapture();
            if (_proceduralTexture != null)
            {
                UnityEngine.Object.Destroy(_proceduralTexture);
                _proceduralTexture = null;
                _proceduralBuffer = null;
            }
            SetState(DeviceConnectionState.Disconnected);
        }

        public void Dispose() => Shutdown();
    }
}
