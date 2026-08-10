// MLOmega V19 â€” E23
// Turns raw frames from the IXRDeviceAdapter into FrameEnvelopes that conform to
// the V19 contract, and raises OnFrame(Texture, FrameEnvelope) for the future
// transport (E24) to consume. It is the single point where a captured texture is
// paired with session_id, a monotonically increasing frame_id, the capture
// monotonic timestamp, the pose AT CAPTURE, and the rotation/source fields.
//
// Allocation discipline: at steady state a frame reuses one FrameEnvelope + one
// Pose + their two position/rotation lists, and a small char buffer for the
// "f_<n>" id. Nothing is allocated per frame on the hot path, so the capture loop
// does not create GC pressure on device.
using System;
using System.Collections.Generic;
using System.Globalization;
using MLOmega.Contracts.V19;
using Pose = MLOmega.Contracts.V19.Pose;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Drives frame capture at a configurable cadence and publishes contract-shaped
    /// envelopes. session_id comes from <see cref="SessionPairing"/>; pose from
    /// <see cref="PosePublisher"/>; timestamps from the shared monotonic clock.
    /// </summary>
    public sealed class EyeCaptureSource : MonoBehaviour
    {
        [SerializeField] private XrSessionController _session;
        [SerializeField] private SessionPairing _pairing;
        [SerializeField] private PosePublisher _pose;

        [Tooltip("Target capture rate (fps). 0 = publish every adapter update. " +
                 "Overridden by MLOmegaConfig.CaptureFps when a config is present.")]
        [Min(0f)]
        [SerializeField] private float _captureFps = 30f;

        [Tooltip("Frame rotation to stamp (0/90/180/270). Wired for capture-only " +
                 "(glasses hung vertically); OrientationGuard drives this in E29.")]
        [SerializeField] private FrameRotation _rotation = FrameRotation.Deg0;

        public enum FrameRotation
        {
            Deg0 = 0,
            Deg90 = 90,
            Deg180 = 180,
            Deg270 = 270
        }

        /// <summary>Current rotation stamped on every envelope (0/90/180/270).</summary>
        public FrameRotation Rotation => _rotation;

        /// <summary>
        /// Set the frame rotation stamped on subsequent envelopes. Driven by
        /// <c>OrientationGuard</c> (E29 Â§3b) from the gravity vector so a phone hung
        /// vertically (capture-only) reports its true sensor orientation; the PC
        /// un-rotates before vision (see live_pipeline.deorient_frame). Rounds to the
        /// nearest 90Â° bucket. Returns true when the value changed.
        /// </summary>
        public bool SetRotation(int degrees)
        {
            int q = ((Mathf.RoundToInt(degrees / 90f) % 4) + 4) % 4;
            FrameRotation next = q switch
            {
                1 => FrameRotation.Deg90,
                2 => FrameRotation.Deg180,
                3 => FrameRotation.Deg270,
                _ => FrameRotation.Deg0,
            };
            if (next == _rotation) return false;
            _rotation = next;
            return true;
        }

        /// <summary>
        /// Raised for each published frame. The Texture is owned by the adapter and
        /// reused; consumers must not dispose it. The FrameEnvelope is reused across
        /// frames â€” read/serialize it synchronously in the handler, do not retain it.
        /// </summary>
        public event Action<Texture, FrameEnvelope> OnFrame;

        // Valid only while OnFrame subscribers are being invoked. XREAL Eye
        // supplies these native Alpha8 planes; phone/simulator adapters do not.
        private Texture2D _currentPlaneY;
        private Texture2D _currentPlaneU;
        private Texture2D _currentPlaneV;

        public bool TryGetCurrentNativeI420(
            out Texture2D planeY,
            out Texture2D planeU,
            out Texture2D planeV)
        {
            planeY = _currentPlaneY;
            planeU = _currentPlaneU;
            planeV = _currentPlaneV;
            return planeY != null && planeU != null && planeV != null;
        }

        /// <summary>Total frames published this session.</summary>
        public long PublishedFrameCount { get; private set; }

        /// <summary>Last envelope published (reused instance).</summary>
        public FrameEnvelope LastEnvelope => _envelope;

        private IMonotonicClock _clock;
        private float _period;
        private float _accum;
        private long _nextFrameNumber;

        // Reused hot-path objects (allocation-free steady state).
        private readonly FrameEnvelope _envelope = new FrameEnvelope();
        private readonly Pose _pose6dof = new Pose();
        private readonly List<double> _position = new List<double>(3) { 0, 0, 0 };
        private readonly List<double> _rotationQuat = new List<double>(4) { 0, 0, 0, 0 };

        private void Awake()
        {
            if (_session == null) _session = FindAnyObjectByType<XrSessionController>();
            if (_pairing == null) _pairing = FindAnyObjectByType<SessionPairing>();
            if (_pose == null) _pose = FindAnyObjectByType<PosePublisher>();

            _envelope.ContractsVersion = ContractDefaults.Version;
            _envelope.Pose = _pose6dof;
            _pose6dof.ContractsVersion = ContractDefaults.Version;
            _pose6dof.Position = _position;
            _pose6dof.Rotation = _rotationQuat;
        }

        private void OnEnable()
        {
            _clock = _pairing != null && _pairing.MonotonicClock != null
                ? _pairing.MonotonicClock
                : new StopwatchMonotonicClock();

            float fps = _captureFps;
            if (_pairing != null && _pairing.Config != null)
            {
                fps = _pairing.Config.CaptureFps;
            }
            _period = fps > 0f ? 1f / fps : 0f;
            _accum = 0f;
            _nextFrameNumber = 0;
            PublishedFrameCount = 0;
        }

        private void Update()
        {
            if (_session == null || _session.Adapter == null)
            {
                return;
            }

            // Cadence gate. period == 0 means "as fast as the adapter produces".
            _accum += Time.unscaledDeltaTime;
            if (_period > 0f && _accum < _period)
            {
                return;
            }

            EyeFrame? maybe = _session.Adapter.TryGetLatestFrame();
            if (!maybe.HasValue || maybe.Value.Texture == null)
            {
                return; // nothing new this update; keep the accumulator so we retry
            }
            _accum = 0f;
            EyeFrame frame = maybe.Value;

            // Sample the pose at the capture instant, not at render (deliberate).
            StampedPose pose = _pose != null
                ? _pose.SampleNow()
                : new StampedPose(Vector3.zero, Quaternion.identity, false, _clock.NowNs());

            FrameEnvelope env = BuildEnvelope(frame, pose);
            PublishedFrameCount++;

            _currentPlaneY = frame.PlaneY;
            _currentPlaneU = frame.PlaneU;
            _currentPlaneV = frame.PlaneV;
            try
            {
                OnFrame?.Invoke(frame.Texture, env);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EyeCaptureSource] frame handler threw: {ex}");
            }
            finally
            {
                _currentPlaneY = null;
                _currentPlaneU = null;
                _currentPlaneV = null;
            }
        }

        /// <summary>
        /// Populate the reused envelope for one frame. Public+static-friendly shape
        /// so EditMode tests can assert field correctness deterministically via
        /// <see cref="BuildEnvelopeInto"/>.
        /// </summary>
        private FrameEnvelope BuildEnvelope(EyeFrame frame, StampedPose pose)
        {
            string sessionId = _pairing != null ? _pairing.SessionId : null;
            string source = _session.Adapter != null ? _session.Adapter.FrameSource : null;
            long frameNumber = _nextFrameNumber++;
            BuildEnvelopeInto(_envelope, sessionId, frameNumber,
                frame.CaptureMonotonicNs, DateTime.UtcNow, pose, (long)_rotation, source);
            return _envelope;
        }

        /// <summary>
        /// Deterministic envelope builder (no Unity Update state). Writes into the
        /// supplied reused <paramref name="env"/>. frame_id is formatted "f_&lt;n&gt;".
        /// captured_at_utc is ISO-8601 UTC ("o" round-trip format).
        /// </summary>
        public static void BuildEnvelopeInto(
            FrameEnvelope env,
            string sessionId,
            long frameNumber,
            long captureMonotonicNs,
            DateTime capturedAtUtc,
            StampedPose pose,
            long rotation,
            string source)
        {
            env.ContractsVersion = ContractDefaults.Version;
            env.SessionId = sessionId;
            env.FrameId = FormatFrameId(frameNumber);
            env.CaptureMonotonicNs = captureMonotonicNs;
            env.CapturedAtUtc = capturedAtUtc.ToUniversalTime()
                .ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
            env.PoseValid = pose.IsTracking;
            env.Rotation = rotation;
            env.Source = source;

            // Fill the pose in place, reusing lists when the envelope already owns them.
            Pose p = env.Pose ?? (env.Pose = new Pose());
            p.ContractsVersion = ContractDefaults.Version;
            List<double> pos = p.Position ?? (p.Position = new List<double>(3));
            pos.Clear();
            pos.Add(pose.Position.x);
            pos.Add(pose.Position.y);
            pos.Add(pose.Position.z);
            List<double> rot = p.Rotation ?? (p.Rotation = new List<double>(4));
            rot.Clear();
            rot.Add(pose.Rotation.x);
            rot.Add(pose.Rotation.y);
            rot.Add(pose.Rotation.z);
            rot.Add(pose.Rotation.w);
        }

        /// <summary>
        /// Format "f_&lt;n&gt;" without allocating a temporary string for the number.
        /// Falls back to string concat only for negative inputs (never happens for a
        /// monotonic counter).
        /// </summary>
        public static string FormatFrameId(long n)
        {
            if (n < 0)
            {
                return "f_" + n.ToString(CultureInfo.InvariantCulture);
            }
            // Build digits into a stack buffer, then a single string alloc (the id
            // itself must be a string per the contract).
            Span<char> buf = stackalloc char[24];
            int i = buf.Length;
            long v = n;
            if (v == 0)
            {
                buf[--i] = '0';
            }
            else
            {
                while (v > 0)
                {
                    buf[--i] = (char)('0' + (int)(v % 10));
                    v /= 10;
                }
            }
            buf[--i] = '_';
            buf[--i] = 'f';
            return new string(buf.Slice(i));
        }
    }
}
