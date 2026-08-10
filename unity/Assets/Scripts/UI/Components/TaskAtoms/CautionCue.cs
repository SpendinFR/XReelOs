// MLOmega V19 — E53 (Viki mode aide) — atom 9/12
// CautionCue: a discreet-but-visible warning banner shown when a step carries a
// `caution` ("plaque chaude", "lame", "ne pas serrer trop fort"). It is a small
// amber glass strip with a ⚠ glyph, anchored near the object when a track is
// present, otherwise docked at the top of the task panel. Never a modal, never a
// flashing alarm — steady, readable, out of the way. Uses the probable/amber accent
// so it reads as a caution without hijacking the observed-green channel.
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class CautionCue : TaskAtom
    {
        private GlassPanel _panel;
        private TaskAnchorMath _anchor; // optional
        private string _trackId;
        private string _text;
        private readonly Vector3[] _corners = new Vector3[4];
        private static readonly Color Amber = new Color(0.98f, 0.72f, 0.30f, 1f);

        protected override void Build()
        {
            _panel = new GlassPanel(transform, new Vector2(0.40f, 0.07f), Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: false, withBody: true, withTruthChip: false);
            _panel.SetAccent(Amber);
            if (_panel.Body != null)
            {
                _panel.Body.fontSize = 0.040f;
                _panel.Body.alignment = TextAlignmentOptions.Left;
            }
        }

        public void SetCaution(TaskAnchorMath anchor, string trackId, string text)
        {
            _anchor = anchor;
            _trackId = trackId;
            _text = text;
            if (_panel?.Body != null) _panel.Body.text = $"<color=#FBB84C>⚠</color> {text}";
        }

        public override void Tick(float now, float dt)
        {
            if (_panel == null) return;
            Camera cam = Cam;
            Vector3 pos;
            if (_anchor != null && _anchor.Resolve(_trackId, _corners))
            {
                pos = _anchor.LocalToWorld(new Vector2(0.5f, -0.4f)); // just above the object
            }
            else
            {
                pos = cam != null ? cam.transform.TransformPoint(new Vector3(0f, 0.24f, 1.1f)) : Vector3.zero;
            }
            if (cam != null)
            {
                transform.SetPositionAndRotation(pos,
                    Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
            }
            _panel.SetAlpha(Alpha);
        }
    }
}
