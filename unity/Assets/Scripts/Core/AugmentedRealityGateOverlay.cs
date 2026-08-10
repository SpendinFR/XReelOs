using System.Text;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>Visible, device-side status for the disposable provider gate.</summary>
    public sealed class AugmentedRealityGateOverlay : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private AugmentedRealityRuntimeGate _gate;
        [SerializeField] private float _refreshSeconds = 0.25f;

        private readonly StringBuilder _text = new StringBuilder(1024);
        private float _nextRefresh;

        private void Awake()
        {
            if (_gate == null)
                _gate = FindAnyObjectByType<AugmentedRealityRuntimeGate>();
            if (_label == null)
                _label = GetComponentInChildren<TMP_Text>();
        }

        private void Update()
        {
            if (_label == null || _gate == null ||
                Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + _refreshSeconds;
            _label.text = Compose();
        }

        private string Compose()
        {
            _text.Clear();
            _text.AppendLine("<b>AR PROVIDER GATE</b>");
            _text.Append("state: ").AppendLine(_gate.CurrentStatus);
            AugmentedRealityRuntimeGate.GateReport report = _gate.LastReport;
            if (report == null) return _text.ToString();

            _text.Append("provider: ")
                .AppendLine(report.provider?.ProviderBoundary ?? "probing");
            _text.Append("render: ")
                .Append(report.average_render_fps.ToString("0.0"))
                .Append(" fps (min ")
                .Append(report.minimum_render_fps.ToString("0.0"))
                .AppendLine(")");
            _text.Append("Eye: ")
                .Append(report.eye_fps.ToString("0.0"))
                .AppendLine(" fps");
            _text.Append("pose: ")
                .Append((report.pose_tracking_ratio * 100f).ToString("0"))
                .AppendLine("%");
            _text.Append("WebRTC: ")
                .Append((report.transport_connected_ratio * 100f).ToString("0"))
                .AppendLine("%");
            _text.Append("AR session: ")
                .Append((report.ar_session_running_ratio * 100f).ToString("0"))
                .AppendLine("%");
            _text.Append("thermal: ")
                .AppendLine(report.maximum_thermal_status.ToString());

            if (report.failures != null)
            {
                foreach (string failure in report.failures)
                    _text.Append("FAIL: ").AppendLine(failure);
            }
            if (!string.IsNullOrEmpty(report.report_path))
                _text.Append("report: ").AppendLine(report.report_path);
            return _text.ToString();
        }
    }
}
