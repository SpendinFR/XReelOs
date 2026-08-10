// MLOmega V19 â€” E25
// OffscreenArrow (Â§13.1, Â§14.6): a head-edge arrow pointing toward an off-screen
// object/place by bearing. CRITICAL truth rule (Â§17.2 / Â§14.6): a precise arrow is
// only drawn when SceneCache.SpatialHotEntry map_quality clears the configured
// threshold â€” "Jamais de flÃ¨che sans qualitÃ© de carte". Below the threshold the
// component draws NOTHING (it stays invisible) rather than pointing confidently in
// a possibly-wrong direction; the runtime/last-seen card carries the fallback. The
// bearing is read from spatial_hot for the intent's entity, or from the intent's
// ui_hint as a fallback.
using MLOmega.XR.Scene;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    public sealed class OffscreenArrow : UIComponentBase
    {
        [SerializeField] private float _planeDistance = 1.2f;
        [Tooltip("Arrow ring radius as a fraction of the vertical FOV half-extent.")]
        [SerializeField] private float _edgeRadius = 0.42f;
        [SerializeField] private float _arrowSize = 0.06f;

        private LineRenderer _arrow;
        private TextMeshPro _label;
        private Color _accent = Color.white;
        private bool _qualified;

        public override string ComponentKey => "offscreen_arrow";

        protected override void OnConfigured()
        {
            _arrow = gameObject.AddComponent<LineRenderer>();
            _arrow.useWorldSpace = true;
            _arrow.loop = false;
            _arrow.positionCount = 3; // simple chevron
            _arrow.widthMultiplier = 0.006f;
            _arrow.numCornerVertices = 2;
            _arrow.material = new Material(Shader.Find("MLOmega/XREAL Runtime Unlit"));

            var lblGo = new GameObject("ArrowLabel", typeof(RectTransform));
            lblGo.transform.SetParent(transform, false);
            _label = lblGo.AddComponent<TextMeshPro>();
            _label.fontSize = 0.035f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = Theme != null ? Theme.MutedTextColor : Color.white;
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            if (_label != null) _label.text = IntentRead.Content(intent, "label", "");
        }

        protected override void OnTruth(TruthDescriptor truth) => _accent = truth.Accent;

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle) return;
            UpdateArrow();
        }

        private void UpdateArrow()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null || _arrow == null) return;

            // Â§17.2: gate on map quality. No qualified map -> draw nothing.
            _qualified = IsBearingQualified(out float bearingDeg);
            float alpha = _qualified ? CurrentAlpha : 0f;
            _arrow.enabled = _qualified;
            if (_label != null) _label.enabled = _qualified;
            if (!_qualified) return;

            // Place the chevron on a ring at the screen edge in the bearing direction.
            float rad = bearingDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)); // 0deg = up/forward
            Vector2 vp = new Vector2(0.5f + dir.x * _edgeRadius, 0.5f + dir.y * _edgeRadius);
            Ray ray = cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            Vector3 center = ray.GetPoint(_planeDistance);

            Vector3 up = cam.transform.up;
            Vector3 right = cam.transform.right;
            Vector3 pointDir = (right * dir.x + up * dir.y).normalized;
            Vector3 side = Vector3.Cross(pointDir, cam.transform.forward).normalized;

            Vector3 tip = center + pointDir * _arrowSize;
            Vector3 a = center - pointDir * _arrowSize * 0.3f + side * _arrowSize * 0.6f;
            Vector3 b = center - pointDir * _arrowSize * 0.3f - side * _arrowSize * 0.6f;
            _arrow.SetPosition(0, a);
            _arrow.SetPosition(1, tip);
            _arrow.SetPosition(2, b);

            Color col = _accent; col.a = alpha;
            _arrow.startColor = col; _arrow.endColor = col;

            if (_label != null && !string.IsNullOrEmpty(_label.text))
            {
                _label.transform.position = center - pointDir * _arrowSize * 1.4f;
                _label.transform.rotation = Quaternion.LookRotation(
                    _label.transform.position - cam.transform.position, Vector3.up);
                Color lc = _label.color; lc.a = alpha; _label.color = lc;
            }
        }

        private bool IsBearingQualified(out float bearingDeg)
        {
            bearingDeg = (float)IntentRead.Num(Intent?.UiHint, "bearing_deg", float.NaN);
            SceneCache sc = Context != null ? Context.SceneCache : null;
            if (sc == null) return !float.IsNaN(bearingDeg); // no cache: trust the hint

            float threshold = sc.Config != null ? sc.Config.MapQualityArrowThreshold : 0.55f;
            bool mapOk = sc.SpatialHot.ArrowAllowed(threshold);

            if (!string.IsNullOrEmpty(Intent?.EntityId) &&
                sc.SpatialHot.TryGet(Intent.EntityId, out SceneCache.SpatialHotEntry spatial) &&
                spatial.HasBearing)
            {
                bearingDeg = (float)spatial.BearingDeg;
            }
            return mapOk && !float.IsNaN(bearingDeg);
        }
    }
}
