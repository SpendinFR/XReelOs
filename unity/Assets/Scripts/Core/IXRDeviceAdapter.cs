// MLOmega V19 — E22 / Gate G1
// Device abstraction shared by the XREAL implementation and the in-editor
// simulator. Everything above this interface (session controller, overlays,
// preview) is device-agnostic, so the exact same G1 scene runs on hardware and
// on a developer machine without glasses.
using System;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Connection state of the underlying XR device / glasses.
    /// </summary>
    public enum DeviceConnectionState
    {
        Unknown = 0,
        Disconnected = 1,
        Connecting = 2,
        Connected = 3,
        Error = 4
    }

    /// <summary>
    /// A 6DoF head pose sample, plus whether it is currently trackable.
    /// Position is in meters, world space; rotation is a unit quaternion.
    /// </summary>
    public readonly struct PoseSample
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly bool IsTracking;

        public PoseSample(Vector3 position, Quaternion rotation, bool isTracking)
        {
            Position = position;
            Rotation = rotation;
            IsTracking = isTracking;
        }

        public static PoseSample Untracked =>
            new PoseSample(Vector3.zero, Quaternion.identity, false);
    }

    /// <summary>
    /// One RGB frame acquired from the device Eye camera (or the simulator).
    /// <see cref="Texture"/> is owned by the adapter and reused across frames —
    /// consumers must not dispose it.
    /// </summary>
    public readonly struct EyeFrame
    {
        public readonly Texture Texture;
        /// <summary>
        /// Optional native XREAL Eye Y/U/V planes. They are owned and reused by
        /// the SDK. Phone/simulator frames leave these null.
        /// </summary>
        public readonly Texture2D PlaneY;
        public readonly Texture2D PlaneU;
        public readonly Texture2D PlaneV;
        /// <summary>Monotonically increasing per-session frame counter.</summary>
        public readonly long FrameId;
        /// <summary>Monotonic capture timestamp in nanoseconds.</summary>
        public readonly long CaptureMonotonicNs;

        public EyeFrame(
            Texture texture,
            long frameId,
            long captureMonotonicNs,
            Texture2D planeY = null,
            Texture2D planeU = null,
            Texture2D planeV = null)
        {
            Texture = texture;
            FrameId = frameId;
            CaptureMonotonicNs = captureMonotonicNs;
            PlaneY = planeY;
            PlaneU = planeU;
            PlaneV = planeV;
        }

        public bool HasNativeI420 =>
            PlaneY != null && PlaneU != null && PlaneV != null;
    }

    /// <summary>
    /// Hardware-facing contract for the G1 gate. Implementations:
    ///   - <c>XrealDeviceAdapter</c>   : real XREAL SDK 3.1.0 (glasses + Eye).
    ///   - <c>SimulatedDeviceAdapter</c>: WebCamTexture + synthetic pose (editor).
    /// Kept deliberately small: G1 only needs connection lifecycle, pose and an
    /// RGB frame. Transport (E24) and richer scene data come in later steps.
    /// </summary>
    public interface IXRDeviceAdapter
    {
        DeviceConnectionState ConnectionState { get; }

        /// <summary>Human-readable device name for the status overlay.</summary>
        string DeviceName { get; }

        /// <summary>True once the Eye camera capture path is live.</summary>
        bool IsEyeActive { get; }

        /// <summary>
        /// True when the device renders a stereo rig (two eyes, e.g. XREAL glasses);
        /// false for flat 2D targets (phone-only). <c>XrSessionController</c> and the
        /// overlay respect this to choose stereo vs full-screen 2D rendering.
        /// </summary>
        bool IsStereo { get; }

        /// <summary>
        /// The value written to <c>FrameEnvelope.source</c> for frames from this
        /// adapter (e.g. <c>xreal_eye</c>, <c>simulated</c>, <c>phone_camera</c>).
        /// </summary>
        string FrameSource { get; }

        /// <summary>Raised whenever <see cref="ConnectionState"/> changes.</summary>
        event Action<DeviceConnectionState> ConnectionStateChanged;

        /// <summary>
        /// Initialize the underlying device stack. Idempotent; safe to call
        /// again after a disconnect to attempt resumption. Throws on
        /// unrecoverable initialization failure.
        /// </summary>
        void Initialize();

        /// <summary>Begin RGB Eye capture. No-op if already active.</summary>
        void StartEyeCapture();

        /// <summary>Stop RGB Eye capture and release its resources.</summary>
        void StopEyeCapture();

        /// <summary>
        /// Latest head pose. Returns <see cref="PoseSample.Untracked"/> when the
        /// device is not tracking.
        /// </summary>
        PoseSample GetPose();

        /// <summary>
        /// Newest Eye frame, or <c>null</c> if none is available yet this update.
        /// </summary>
        EyeFrame? TryGetLatestFrame();

        /// <summary>Release all native resources. Safe to call multiple times.</summary>
        void Shutdown();
    }
}
