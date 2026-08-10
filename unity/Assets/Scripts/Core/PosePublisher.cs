// MLOmega V19 â€” E23
// Associates the 6DoF head pose with each captured frame (sampled AT capture, not
// at render) and also emits a standalone pose stream at its own cadence for the
// future DataChannel (E24). Shares the session's monotonic clock so pose and frame
// timestamps live on one timeline.
using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using Pose = MLOmega.Contracts.V19.Pose;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>One 6DoF sample stamped on the shared monotonic clock.</summary>
    public readonly struct StampedPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly bool IsTracking;
        public readonly long MonotonicNs;

        public StampedPose(Vector3 position, Quaternion rotation, bool isTracking, long monotonicNs)
        {
            Position = position;
            Rotation = rotation;
            IsTracking = isTracking;
            MonotonicNs = monotonicNs;
        }

        /// <summary>Contract Pose (position xyz, rotation as quaternion xyzw).</summary>
        public Pose ToContractPose()
        {
            return new Pose
            {
                ContractsVersion = ContractDefaults.Version,
                Position = new List<double> { Position.x, Position.y, Position.z },
                Rotation = new List<double> { Rotation.x, Rotation.y, Rotation.z, Rotation.w }
            };
        }
    }

    /// <summary>
    /// Samples <see cref="IXRDeviceAdapter.GetPose"/> on demand (for frame pairing)
    /// and on a fixed cadence (for the pose stream). The adapter is read from the
    /// <see cref="XrSessionController"/> so it always tracks the active device.
    /// </summary>
    public sealed class PosePublisher : MonoBehaviour
    {
        [SerializeField] private XrSessionController _session;
        [SerializeField] private SessionPairing _pairing;

        [Tooltip("Standalone pose-stream rate (Hz). Overridden by MLOmegaConfig.PosePublishHz when a config is available.")]
        [Min(1f)]
        [SerializeField] private float _posePublishHz = 60f;

        /// <summary>Latest pose sampled by the pose-stream cadence.</summary>
        public StampedPose Latest { get; private set; }

        /// <summary>Fires at the pose-stream cadence with each fresh sample.</summary>
        public event Action<StampedPose> OnPose;

        private IMonotonicClock _clock;
        private float _period;
        private float _accum;

        private void Awake()
        {
            if (_session == null) _session = FindAnyObjectByType<XrSessionController>();
            if (_pairing == null) _pairing = FindAnyObjectByType<SessionPairing>();
        }

        private void OnEnable()
        {
            _clock = _pairing != null && _pairing.MonotonicClock != null
                ? _pairing.MonotonicClock
                : new StopwatchMonotonicClock();

            float hz = _posePublishHz;
            if (_pairing != null && _pairing.Config != null)
            {
                hz = _pairing.Config.PosePublishHz;
            }
            _period = hz > 0f ? 1f / hz : 0f;
            _accum = 0f;
        }

        private void Update()
        {
            if (_session == null || _session.Adapter == null)
            {
                return;
            }

            _accum += Time.unscaledDeltaTime;
            if (_period > 0f && _accum < _period)
            {
                return;
            }
            _accum = 0f;

            StampedPose sample = SampleNow();
            Latest = sample;
            OnPose?.Invoke(sample);
        }

        /// <summary>
        /// Sample the pose right now, stamped on the shared monotonic clock. Called
        /// by <see cref="EyeCaptureSource"/> at the exact capture instant so the
        /// pose paired to a frame is the pose at capture, not at render.
        /// </summary>
        public StampedPose SampleNow()
        {
            IXRDeviceAdapter adapter = _session != null ? _session.Adapter : null;
            long ns = _clock != null ? _clock.NowNs() : 0;
            if (adapter == null)
            {
                return new StampedPose(Vector3.zero, Quaternion.identity, false, ns);
            }
            PoseSample p = adapter.GetPose();
            return new StampedPose(p.Position, p.Rotation, p.IsTracking, ns);
        }
    }
}
