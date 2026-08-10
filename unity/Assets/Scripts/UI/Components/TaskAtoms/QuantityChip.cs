// MLOmega V19 — E53 (Viki mode aide) — atom 4/12
// QuantityChip: a small measure/quantity pill ("200 g", "3 tours", "2 cm") pinned
// just off the anchored object. It follows the object (via TaskAnchorMath) and
// checks itself off — a discreet ✓ prefix and a dim — when the owning step
// advances past it, so a glance tells you whether that measure is already done.
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class QuantityChip : TaskAtom
    {
        private TextMeshPro _label;
        private TaskAnchorMath _anchor;
        private string _trackId;
        private string _text;
        private bool _checked;
        private readonly Vector3[] _corners = new Vector3[4];

        protected override void Build()
        {
            var go = new GameObject("QuantityText", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _label = go.AddComponent<TextMeshPro>();
            _label.fontSize = 0.05f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontStyle = FontStyles.Bold;
            _label.color = Theme != null ? Theme.TextColor : Color.white;
        }

        public void SetQuantity(TaskAnchorMath anchor, string trackId, string text)
        {
            _anchor = anchor;
            _trackId = trackId;
            _text = text;
        }

        /// <summary>Mark the measure as satisfied (step advanced past it).</summary>
        public void SetChecked(bool done) => _checked = done;

        public override void Tick(float now, float dt)
        {
            if (_anchor == null || _label == null) return;
            _anchor.Resolve(_trackId, _corners);
            // Pin above-right of the anchored region.
            _label.transform.position = _anchor.LocalToWorld(new Vector2(1.05f, -0.15f));
            Billboard(_label.transform);

            _label.text = _checked ? $"<color=#7FE0A8>✓</color> {_text}" : _text;
            Color c = _checked
                ? (Theme != null ? Theme.MutedTextColor : Color.gray)
                : (Theme != null ? Theme.TextColor : Color.white);
            _label.color = WithAlpha(c, _checked ? 0.7f : 1f);
        }
    }
}
