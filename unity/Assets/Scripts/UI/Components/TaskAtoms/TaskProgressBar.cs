// MLOmega V19 — E53 (Viki mode aide) — atom 12/12
// TaskProgressBar: a thin overall-progress bar (0..1). Usable standalone or docked
// inside the TaskPanel. Two flat UGUI images (track + fill) under a world-space
// RectTransform, tinted from the theme accent; the fill eases toward the target so
// a step advance reads as a smooth nudge, not a jump.
using UnityEngine;
using UnityEngine.UI;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class TaskProgressBar : TaskAtom
    {
        private RectTransform _root;
        private Image _track;
        private Image _fill;
        private RectTransform _fillRt;
        private float _target;
        private float _shown;
        private float _width = 0.36f;

        protected override void Build()
        {
            var go = new GameObject("ProgressBar", typeof(RectTransform), typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _root = go.GetComponent<RectTransform>();
            _root.SetParent(transform, false);
            _root.sizeDelta = new Vector2(_width, 0.012f);

            _track = MakeBar(_root, "Track",
                Theme != null ? Theme.MutedTextColor : new Color(1, 1, 1, 0.25f));
            _fill = MakeBar(_root, "Fill",
                Theme != null ? Theme.RimColor : Color.cyan);
            _fillRt = _fill.rectTransform;
            _fillRt.anchorMin = new Vector2(0, 0);
            _fillRt.anchorMax = new Vector2(0, 1);
            _fillRt.pivot = new Vector2(0, 0.5f);
        }

        private Image MakeBar(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color; img.raycastTarget = false;
            return img;
        }

        /// <summary>Configure width (metres) — used when the bar is docked in a panel.</summary>
        public void SetWidth(float width)
        {
            _width = width;
            if (_root != null) _root.sizeDelta = new Vector2(width, _root.sizeDelta.y);
        }

        public void SetProgress(float progress01) => _target = Mathf.Clamp01(progress01);

        /// <summary>Local-space placement helper for docking into a parent panel.</summary>
        public void SetLocal(Vector3 localPos)
        {
            if (_root != null) _root.localPosition = localPos;
        }

        public override void Tick(float now, float dt)
        {
            _shown = Mathf.MoveTowards(_shown, _target, dt * 1.5f);
            if (_fillRt != null)
            {
                _fillRt.sizeDelta = new Vector2(0, 0);
                _fillRt.anchorMax = new Vector2(_shown, 1);
            }
            if (_track != null) _track.color = WithAlpha(Theme != null ? Theme.MutedTextColor : new Color(1, 1, 1, 0.25f));
            if (_fill != null) _fill.color = WithAlpha(Theme != null ? Theme.RimColor : Color.cyan);
        }
    }
}
