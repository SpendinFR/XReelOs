// MLOmega V19 — E29 §3b
// OrientationGuard: detects device orientation from the gravity vector and stamps
// the matching rotation (0/90/180/270) on every FrameEnvelope via EyeCaptureSource.
// When the glasses/phone are hung vertically (capture-only), frames arrive rotated
// and the PC un-rotates them before vision (see live_pipeline.deorient_frame). The
// StatusBar shows a "capture-only" badge whenever the rotation is non-zero.
//
// Detection: the gravity vector points "down" in device space. In landscape-up the
// accelerometer reads roughly (0,-1,0); rotating the device 90° rotates that vector
// into (+/-1,0,0). We take the in-plane (x,y) gravity direction, quantise its angle
// to the nearest 90° bucket, and apply hysteresis so small tilts near a boundary do
// not flip the stamp every frame.
//
// The pure decision (gravity → rotation bucket) is a static function so EditMode
// tests can assert it deterministically without a device.
//
// Unity compilation is deferred like the rest of apps/xr-mobile (no editor here);
// this is production C# validated at the first Unity open + on-device gate.
using UnityEngine;

namespace MLOmega.XR.Core
{
    public sealed class OrientationGuard : MonoBehaviour
    {
        [SerializeField] private EyeCaptureSource _capture;
        [SerializeField] private PosePublisher _pose;
        [SerializeField] private XrSessionController _session;

        [Tooltip("Optional StatusBar to flag capture-only (rotation != 0). Wired by name to avoid a UI asmdef dependency.")]
        [SerializeField] private MonoBehaviour _statusBar; // duck-typed: has a bool CaptureOnly

        [Tooltip("Re-evaluate orientation at most this often (seconds).")]
        [Min(0.05f)]
        [SerializeField] private float _evalInterval = 0.3f;

        [Tooltip("Degrees of tilt past a 90° boundary required before switching buckets (hysteresis).")]
        [Range(0f, 30f)]
        [SerializeField] private float _hysteresisDeg = 12f;

        [Tooltip("Below this gravity magnitude the reading is treated as free-fall/noise and ignored.")]
        [SerializeField] private float _minGravity = 0.35f;

        private float _nextEval;
        private int _currentBucket; // 0..3 → 0/90/180/270
        private System.Reflection.PropertyInfo _captureOnlyProp;

        private void Awake()
        {
            if (_capture == null) _capture = FindAnyObjectByType<EyeCaptureSource>();
            if (_pose == null) _pose = FindAnyObjectByType<PosePublisher>();
            if (_session == null) _session = FindAnyObjectByType<XrSessionController>();
            if (_statusBar != null)
            {
                _captureOnlyProp = _statusBar.GetType().GetProperty("CaptureOnly");
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextEval) return;
            _nextEval = Time.unscaledTime + _evalInterval;

            Vector3 gravity = ReadGravity();
            int bucket = DecideBucket(gravity, _currentBucket, _hysteresisDeg, _minGravity);
            if (bucket == _currentBucket) return;

            _currentBucket = bucket;
            int degrees = bucket * 90;
            if (_capture != null)
            {
                _capture.SetRotation(degrees);
            }
            if (_captureOnlyProp != null)
            {
                _captureOnlyProp.SetValue(_statusBar, degrees != 0);
            }
        }

        private bool TryReadTrackedPoseGravity(out Vector3 gravity)
        {
            gravity = Vector3.zero;
            if (_pose == null) return false;
            StampedPose sp = _pose.SampleNow();
            if (!sp.IsTracking) return false;
            gravity = Quaternion.Inverse(sp.Rotation) * Vector3.down;
            return true;
        }

        /// <summary>
        /// Read the gravity direction in device space. Prefers the hardware
        /// accelerometer; falls back to the adapter pose's world-down transformed
        /// into device space when the accelerometer is unavailable.
        /// </summary>
        private Vector3 ReadGravity()
        {
            // The Eye camera rotates with the glasses, not with the host phone.
            // Prefer the HMD pose only for XREAL; PhoneOnly retains the host
            // accelerometer behavior below.
            if (_session != null &&
                _session.Adapter is XrealDeviceAdapter xreal &&
                xreal.TryGetTrackedRotation(out Quaternion xrealRotation))
            {
                return Quaternion.Inverse(xrealRotation) * Vector3.down;
            }

            Vector3 a = Input.acceleration;
            if (a.sqrMagnitude >= _minGravity * _minGravity)
            {
                return a;
            }
            if (TryReadTrackedPoseGravity(out Vector3 poseGravity))
            {
                return poseGravity;
            }
            return a; // may be ~zero → DecideBucket keeps the current bucket
        }

        /// <summary>
        /// Pure decision: map an in-device gravity vector to a rotation bucket
        /// (0..3 → 0/90/180/270), keeping <paramref name="current"/> unless the tilt
        /// has moved a full quadrant past its boundary by <paramref name="hysteresisDeg"/>.
        /// Returns <paramref name="current"/> for degenerate (free-fall) gravity.
        /// </summary>
        public static int DecideBucket(Vector3 gravity, int current, float hysteresisDeg, float minGravity)
        {
            Vector2 g = new Vector2(gravity.x, gravity.y);
            if (g.magnitude < minGravity)
            {
                return ((current % 4) + 4) % 4; // ignore noise / free fall
            }
            // Angle of the "down" direction, 0° = down (landscape-up, no rotation).
            // atan2(x, -y): when gravity = (0,-1) → 0°, (−1,0) → +90°, (0,1) → 180°.
            float angle = Mathf.Atan2(g.x, -g.y) * Mathf.Rad2Deg; // -180..180
            if (angle < 0f) angle += 360f;                        // 0..360

            int raw = Mathf.RoundToInt(angle / 90f) % 4;          // nearest quadrant
            if (raw == current) return current;

            // Hysteresis: only switch once we are hysteresisDeg past the midpoint.
            float boundary = ((current * 90f) + 45f);
            float delta = Mathf.DeltaAngle(current * 90f, angle);
            if (Mathf.Abs(delta) < 45f + hysteresisDeg)
            {
                return current;
            }
            return ((raw % 4) + 4) % 4;
        }
    }
}
