// MLOmega V19 — E53 (Viki mode aide) — atom 11/12
// ZoomInset: a small magnified inset of the step's zone (a fiddly connector, a fine
// alignment mark), reusing the same GlassPanel + RawImage mechanic as E25's
// LensWindow. The renderer assigns the cropped texture (resolved from a render
// texture id, like LensWindow) and an optional caption; the inset is docked beside
// the object when a track is present, otherwise centre-right. It never invents
// detail — it only shows the texture it is given (§14.5 honesty).
using UnityEngine;
using UnityEngine.UI;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class ZoomInset : TaskAtom
    {
        [SerializeField] private Vector2 _size = new Vector2(0.26f, 0.26f);
        [SerializeField] private Vector3 _offset = new Vector3(0.30f, 0.02f, 1.0f);

        private GlassPanel _panel;
        private RawImage _content;
        private TaskAnchorMath _anchor; // optional
        private string _trackId;
        private readonly Vector3[] _corners = new Vector3[4];

        protected override void Build()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true, withBody: false, withTruthChip: false);
            if (_panel.Title != null) _panel.Title.text = "Zoom";

            var go = new GameObject("ZoomContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_panel.Root, false);
            rt.anchorMin = new Vector2(0.08f, 0.08f);
            rt.anchorMax = new Vector2(0.92f, 0.78f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _content = go.GetComponent<RawImage>();
            _content.raycastTarget = false;
            _content.color = new Color(1, 1, 1, 0);
        }

        public void SetZoom(TaskAnchorMath anchor, string trackId, Texture texture, string caption)
        {
            _anchor = anchor;
            _trackId = trackId;
            if (_content != null)
            {
                _content.texture = texture;
                _content.color = texture != null ? Color.white : new Color(1, 1, 1, 0);
            }
            if (_panel?.Title != null && !string.IsNullOrEmpty(caption)) _panel.Title.text = caption;
        }

        public override void Tick(float now, float dt)
        {
            if (_panel == null) return;
            Camera cam = Cam;
            Vector3 pos;
            if (_anchor != null && _anchor.Resolve(_trackId, _corners))
            {
                pos = _anchor.LocalToWorld(new Vector2(1.6f, 0.5f)); // beside the object
            }
            else
            {
                pos = cam != null ? cam.transform.TransformPoint(_offset) : Vector3.zero;
            }
            if (cam != null)
            {
                transform.SetPositionAndRotation(pos,
                    Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
            }
            _panel.SetAlpha(Alpha);
            if (_content != null && _content.texture != null)
            {
                Color c = _content.color; c.a = Alpha; _content.color = c;
            }
        }
    }
}
