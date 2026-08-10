// MLOmega V19 — E53 (Viki mode aide) — atom 6/12
// TaskDirectionalArrow: when the required object has NO live track (off-screen, or
// never matched by VisionRT), a discreet peripheral chevron points toward its last
// known bearing (spatial_hot for the entity), plus a "cherche le X" label. It obeys
// the same §17.2 honesty gate as E25's OffscreenArrow: a precise arrow is drawn
// only when map_quality clears the threshold; below it, the chevron is hidden and
// only the "cherche le X" text remains — never a confident arrow in a wrong
// direction. The renderer swaps to this atom automatically when the ObjectAnchorRing
// loses its track for good.
using MLOmega.XR.Scene;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class TaskDirectionalArrow : TaskAtom
    {
        private LineRenderer _chevron;
        private TextMeshPro _label;
        private string _entityId;
        private string _name;
        private Color _accent = Color.white;

        [SerializeField] private float _planeDistance = 1.2f;
        [SerializeField] private float _edgeRadius = 0.40f;
        [SerializeField] private float _arrowSize = 0.055f;

        protected override void Build()
        {
            _chevron = gameObject.AddComponent<LineRenderer>();
            _chevron.useWorldSpace = true;
            _chevron.loop = false;
            _chevron.positionCount = 3;
            _chevron.widthMultiplier = 0.006f;
            _chevron.numCornerVertices = 2;
            _chevron.material = new Material(Shader.Find("MLOmega/XREAL Runtime Unlit"));

            var go = new GameObject("SearchLabel", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _label = go.AddComponent<TextMeshPro>();
            _label.fontSize = 0.035f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = Theme != null ? Theme.MutedTextColor : Color.white;
        }

        public void SetTarget(string entityId, string name, Color accent)
        {
            _entityId = entityId;
            _name = name;
            _accent = accent;
            if (_label != null) _label.text = string.IsNullOrEmpty(name) ? "cherche l'objet" : $"cherche {name}";
        }

        public override void Tick(float now, float dt)
        {
            Camera cam = Cam;
            if (cam == null || _chevron == null) return;

            bool qualified = ResolveBearing(out float bearingDeg);
            _chevron.enabled = qualified;
            if (!qualified)
            {
                // No trustworthy bearing: keep only the "cherche X" text, centred low.
                if (_label != null)
                {
                    _label.enabled = true;
                    _label.transform.position = cam.transform.TransformPoint(new Vector3(0f, -0.16f, 1.1f));
                    Billboard(_label.transform);
                    _label.color = WithAlpha(Theme != null ? Theme.MutedTextColor : Color.white);
                }
                return;
            }

            float rad = bearingDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 vp = new Vector2(0.5f + dir.x * _edgeRadius, 0.5f + dir.y * _edgeRadius);
            Ray ray = cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            Vector3 center = ray.GetPoint(_planeDistance);

            Vector3 up = cam.transform.up, right = cam.transform.right;
            Vector3 pointDir = (right * dir.x + up * dir.y).normalized;
            Vector3 side = Vector3.Cross(pointDir, cam.transform.forward).normalized;
            _chevron.SetPosition(0, center - pointDir * _arrowSize * 0.3f + side * _arrowSize * 0.6f);
            _chevron.SetPosition(1, center + pointDir * _arrowSize);
            _chevron.SetPosition(2, center - pointDir * _arrowSize * 0.3f - side * _arrowSize * 0.6f);
            Color col = WithAlpha(_accent);
            _chevron.startColor = col; _chevron.endColor = col;

            if (_label != null)
            {
                _label.enabled = true;
                _label.transform.position = center - pointDir * _arrowSize * 1.5f;
                Billboard(_label.transform);
                _label.color = WithAlpha(Theme != null ? Theme.MutedTextColor : Color.white);
            }
        }

        private bool ResolveBearing(out float bearingDeg)
        {
            bearingDeg = float.NaN;
            SceneCache sc = Context != null ? Context.SceneCache : null;
            if (sc == null) return false;
            float threshold = sc.Config != null ? sc.Config.MapQualityArrowThreshold : 0.55f;
            if (!sc.SpatialHot.ArrowAllowed(threshold)) return false;
            if (!string.IsNullOrEmpty(_entityId) &&
                sc.SpatialHot.TryGet(_entityId, out SceneCache.SpatialHotEntry sp) && sp.HasBearing)
            {
                bearingDeg = (float)sp.BearingDeg;
                return true;
            }
            return false;
        }
    }
}
