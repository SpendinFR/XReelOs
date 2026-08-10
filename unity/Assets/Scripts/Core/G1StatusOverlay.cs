// MLOmega V19 — E22 / Gate G1
// The permanent status panel required by the gate: device, pose OK/KO, Eye OK/KO,
// fps, battery (SystemInfo.batteryLevel), permissions. Writes to a TextMeshPro
// label on a world-space canvas.
using System.Text;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Aggregates state from the session, preview, pose readout and permission
    /// gate into one always-on text panel. This is the visible G1 evidence.
    /// </summary>
    public sealed class G1StatusOverlay : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private XrSessionController _session;
        [SerializeField] private EyeCapturePreview _preview;
        [SerializeField] private PoseReadout _pose;
        [SerializeField] private PermissionGate _permissions;

        [Tooltip("Overlay refresh interval in seconds.")]
        [SerializeField] private float _refreshInterval = 0.25f;

        private float _nextRefresh;
        private readonly StringBuilder _sb = new StringBuilder(512);

        private void Awake()
        {
            if (_session == null) _session = FindAnyObjectByType<XrSessionController>();
            if (_preview == null) _preview = FindAnyObjectByType<EyeCapturePreview>();
            if (_pose == null) _pose = FindAnyObjectByType<PoseReadout>();
            if (_permissions == null) _permissions = FindAnyObjectByType<PermissionGate>();
            if (_label == null) _label = GetComponentInChildren<TMP_Text>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh || _label == null)
            {
                return;
            }
            _nextRefresh = Time.unscaledTime + _refreshInterval;
            _label.text = Compose();
        }

        private string Compose()
        {
            _sb.Clear();
            _sb.AppendLine("<b>MLOmega XR — G1 Gate</b>");

            // Device + session
            string device = _session != null && _session.Adapter != null
                ? _session.Adapter.DeviceName
                : "<no adapter>";
            string sessionState = _session != null ? _session.State.ToString() : "?";
            string sessionId = _session != null && !string.IsNullOrEmpty(_session.SessionId)
                ? _session.SessionId
                : "-";
            _sb.Append("device : ").AppendLine(device);
            _sb.Append("session: ").Append(sessionState).Append("  ").AppendLine(Truncate(sessionId, 28));

            // Pose
            bool poseOk = _pose != null && _pose.IsTracking;
            _sb.Append("pose   : ").AppendLine(poseOk ? "OK" : "KO");
            if (_pose != null)
            {
                _sb.AppendLine(_pose.Format());
            }

            // Eye + fps
            bool eyeActive = _session != null && _session.Adapter != null && _session.Adapter.IsEyeActive;
            bool hasFrame = _preview != null && _preview.HasFrame;
            _sb.Append("eye    : ").AppendLine(eyeActive && hasFrame ? "OK" : (eyeActive ? "waiting" : "KO"));
            if (_preview != null)
            {
                _sb.Append("frame  : #").Append(_preview.LastFrameId)
                   .Append("  ").Append(_preview.MeasuredFps.ToString("0.0")).Append(" fps")
                   .Append("  t=").Append(_preview.LastCaptureMonotonicNs).AppendLine(" ns");
            }

            // Battery
            float battery = SystemInfo.batteryLevel; // -1 if unknown
            string batteryStr = battery < 0f ? "n/a" : $"{Mathf.RoundToInt(battery * 100f)}%";
            _sb.Append("battery: ").Append(batteryStr)
               .Append("  (").Append(SystemInfo.batteryStatus).AppendLine(")");

            // Permissions
            _sb.Append("perms  : ")
               .AppendLine(_permissions != null ? _permissions.Format() : "n/a");

            return _sb.ToString();
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}
