// MLOmega V19 — E53 (Viki mode aide) — atom 8/12
// InstructionCard: the rich text of the CURRENT step ("Verse 200 g de farine dans
// le bol en remuant"), a larger, more readable variant of the E25 ContextCard/
// TaskCard glass surface. It carries an optional step counter ("étape 3/7") and an
// optional sub-hint line. Content-driven; head-locked centre-low so it reads like a
// prompt without covering the object. Reuses GlassPanel exactly like ContextCard so
// the glass look is identical to the design system.
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class InstructionCard : TaskAtom
    {
        [SerializeField] private Vector2 _size = new Vector2(0.50f, 0.16f);
        [SerializeField] private Vector3 _offset = new Vector3(0f, -0.20f, 1.05f);

        private GlassPanel _panel;

        protected override void Build()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true, withBody: true, withTruthChip: false);
            if (_panel.Body != null) _panel.Body.fontSize = 0.046f;
        }

        /// <summary>Set the step text, optional counter and optional sub-hint.</summary>
        public void SetInstruction(string text, int stepIndex, int stepCount, string subHint)
        {
            if (_panel.Title != null)
            {
                _panel.Title.text = stepCount > 0
                    ? $"Étape {stepIndex}/{stepCount}"
                    : "Étape";
            }
            if (_panel.Body != null)
            {
                _panel.Body.text = string.IsNullOrEmpty(subHint)
                    ? text
                    : $"{text}\n<size=70%><color=#9FB3C8>{subHint}</color></size>";
            }
        }

        public override void Tick(float now, float dt)
        {
            Camera cam = Cam;
            if (cam != null)
            {
                Vector3 pos = cam.transform.TransformPoint(_offset);
                transform.SetPositionAndRotation(pos,
                    Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
            }
            _panel?.SetAlpha(Alpha);
        }
    }
}
