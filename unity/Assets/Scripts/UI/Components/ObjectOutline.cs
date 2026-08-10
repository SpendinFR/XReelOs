// MLOmega V19 — E25
// ObjectOutline (§13.1): a contour that follows a SceneCache track (screen_track
// anchor). Each frame it reads the track's bbox from SceneCache.Tracks and draws a
// rounded rectangle at that screen region, projected onto a world-space plane in
// front of the camera (so it lives in the XR world but tracks the 2D detection).
// When the track leaves the cache the broker fades and removes the intent (§13.2
// "le renderer ne laisse jamais une boîte vieille collée à un autre objet") — this
// component never re-binds to another track. Truth accent tints the outline; a
// "probable" object shows a discreet chip label.
using System.Collections.Generic;
using MLOmega.XR.Scene;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    public sealed class ObjectOutline : UIComponentBase
    {
        [Tooltip("Distance in front of the camera the outline plane sits (m).")]
        [SerializeField] private float _planeDistance = 1.4f;
        [SerializeField] private float _lineWidth = 0.004f;

        private LineRenderer _line;
        private TextMeshPro _chip;
        private Color _accent = Color.white;
        private string _trackId;
        private Rect _directBbox;
        private bool _hasDirectBbox;

        public override string ComponentKey => "object_outline";

        protected override void OnConfigured()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.loop = true;
            _line.positionCount = 4;
            _line.widthMultiplier = _lineWidth;
            _line.numCornerVertices = 4;
            _line.material = new Material(Shader.Find("MLOmega/XREAL Runtime Unlit"));
            _line.textureMode = LineTextureMode.Stretch;

            var chipGo = new GameObject("OutlineChip", typeof(RectTransform));
            chipGo.transform.SetParent(transform, false);
            _chip = chipGo.AddComponent<TextMeshPro>();
            _chip.fontSize = 0.05f;
            _chip.alignment = TextAlignmentOptions.BottomLeft;
            _chip.color = Theme != null ? Theme.TextColor : Color.white;
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            _trackId = intent.TargetTrackId;
            _hasDirectBbox = IntentRead.TryRect(intent.Anchor, "bbox", out _directBbox);
            if (_chip != null) _chip.text = IntentRead.Content(intent, "label", "");
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            _accent = truth.Accent;
            if (_chip != null)
            {
                string extra = ContextCard.TruthChipText(truth);
                string baseLabel = IntentRead.Content(Intent, "label", "");
                _chip.text = string.IsNullOrEmpty(extra) ? baseLabel
                    : $"{baseLabel} <size=70%><color=#9FB3C8>{extra}</color></size>";
            }
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle) return;
            UpdateOutline();
        }

        private void UpdateOutline()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null || _line == null) return;

            // Prefer the live track. Open-vocabulary VLM hits do not have a COCO
            // track, so they carry one validated direct screen_bbox instead.
            // Never invent a decorative centre box when neither exists.
            Rect bbox = default;
            bool hasBbox = false;
            if (Context != null && Context.SceneCache != null &&
                !string.IsNullOrEmpty(_trackId) &&
                Context.SceneCache.Tracks.TryGet(_trackId, out SceneCache.TrackEntry entry))
            {
                bbox = BboxToRect(entry.Track.BboxOrMask, default);
                hasBbox = bbox.width > 0f && bbox.height > 0f;
            }
            else if (_hasDirectBbox)
            {
                bbox = _directBbox;
                hasBbox = true;
            }

            if (!hasBbox)
            {
                _line.enabled = false;
                if (_chip != null) _chip.enabled = false;
                return;
            }
            _line.enabled = true;
            if (_chip != null) _chip.enabled = true;

            // Project the 4 corners onto a plane _planeDistance in front of the cam.
            Vector3 c0 = ViewportToPlane(cam, new Vector2(bbox.xMin, bbox.yMin));
            Vector3 c1 = ViewportToPlane(cam, new Vector2(bbox.xMax, bbox.yMin));
            Vector3 c2 = ViewportToPlane(cam, new Vector2(bbox.xMax, bbox.yMax));
            Vector3 c3 = ViewportToPlane(cam, new Vector2(bbox.xMin, bbox.yMax));
            _line.SetPosition(0, c0);
            _line.SetPosition(1, c1);
            _line.SetPosition(2, c2);
            _line.SetPosition(3, c3);

            Color col = _accent; col.a = CurrentAlpha;
            _line.startColor = col; _line.endColor = col;

            if (_chip != null)
            {
                _chip.transform.position = c3 + (c0 - c3) * 0f;
                _chip.transform.rotation = Quaternion.LookRotation(
                    _chip.transform.position - cam.transform.position, Vector3.up);
                Color cc = _chip.color; cc.a = CurrentAlpha; _chip.color = cc;
            }
        }

        private Vector3 ViewportToPlane(Camera cam, Vector2 viewport)
        {
            // Flip Y: bbox y is top-down (image), viewport is bottom-up.
            Ray ray = cam.ViewportPointToRay(new Vector3(viewport.x, 1f - viewport.y, 0f));
            return ray.GetPoint(_planeDistance);
        }

        private static Rect BboxToRect(Dictionary<string, object> bbox, Rect fallback)
        {
            if (bbox == null) return fallback;
            float x = (float)IntentRead.Num(bbox, "x", fallback.x);
            float y = (float)IntentRead.Num(bbox, "y", fallback.y);
            float w = (float)IntentRead.Num(bbox, "w", (float)IntentRead.Num(bbox, "width", fallback.width));
            float h = (float)IntentRead.Num(bbox, "h", (float)IntentRead.Num(bbox, "height", fallback.height));
            return new Rect(x, y, w, h);
        }
    }
}
