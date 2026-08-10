// MLOmega V19 — E22 / Gate G1
// Reads the 6DoF head pose each frame and exposes a formatted readout plus the
// measured pose sampling rate for the overlay.
using System.Globalization;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Samples <see cref="IXRDeviceAdapter.GetPose"/> every frame, tracks the
    /// sampling frequency and produces a compact human-readable string.
    /// </summary>
    public sealed class PoseReadout : MonoBehaviour
    {
        [SerializeField] private XrSessionController _session;

        public bool IsTracking { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        public float SampleRateHz { get; private set; }

        private float _accumTime;
        private int _accumSamples;

        private void Awake()
        {
            if (_session == null)
            {
                _session = FindAnyObjectByType<XrSessionController>();
            }
        }

        private void Update()
        {
            if (_session == null || _session.Adapter == null)
            {
                IsTracking = false;
                return;
            }

            PoseSample sample = _session.Adapter.GetPose();
            IsTracking = sample.IsTracking;
            Position = sample.Position;
            Rotation = sample.Rotation;

            _accumSamples++;
            _accumTime += Time.unscaledDeltaTime;
            if (_accumTime >= 0.5f)
            {
                SampleRateHz = _accumTime > 0f ? _accumSamples / _accumTime : 0f;
                _accumTime = 0f;
                _accumSamples = 0;
            }
        }

        /// <summary>Compact multi-line readout for the overlay.</summary>
        public string Format()
        {
            if (!IsTracking)
            {
                return "pose: <untracked>";
            }
            CultureInfo c = CultureInfo.InvariantCulture;
            Vector3 e = Rotation.eulerAngles;
            return string.Format(c,
                "pos  x{0,7:0.000} y{1,7:0.000} z{2,7:0.000} m\n" +
                "rot  p{3,6:0.0} y{4,6:0.0} r{5,6:0.0} deg   {6:0} Hz",
                Position.x, Position.y, Position.z,
                e.x, e.y, e.z, SampleRateHz);
        }
    }
}
