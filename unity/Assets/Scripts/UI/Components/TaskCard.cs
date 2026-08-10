// MLOmega V19 — E25
// TaskCard (§13.1): the single next action of the current task, plus the tool it
// uses. Lateral panel, one step shown at a time (SceneCache.task_hot is single).
// Distinguishes manual / observed / hypothesis via the truth chip (§14.7). Offers
// an explicit "done" affordance that raises an `acted` receipt (§13.3) — the
// receipt is exposure/confirmation, never an automatic outcome/causality claim.
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    public sealed class TaskCard : UIComponentBase
    {
        [SerializeField] private Vector2 _size = new Vector2(0.44f, 0.22f);
        [SerializeField] private Vector3 _lateralOffset = new Vector3(0.34f, -0.16f, 1.1f);

        private GlassPanel _panel;

        public override string ComponentKey => "task_card";

        protected override void OnConfigured()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true, withBody: true, withTruthChip: true);
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            string step = IntentRead.Content(intent, "step",
                IntentRead.Content(intent, "action", "Next step"));
            string tool = IntentRead.Content(intent, "tool", null);
            string goal = IntentRead.Content(intent, "goal", null);

            if (_panel.Title != null) _panel.Title.text = string.IsNullOrEmpty(goal) ? "Task" : goal;
            if (_panel.Body != null)
            {
                string toolLine = string.IsNullOrEmpty(tool)
                    ? ""
                    : $"\n<size=80%><color=#8FD3B0>tool: {tool}</color></size>";
                _panel.Body.text = $"→ {step}{toolLine}";
            }
            PlaceLateral();
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            if (_panel == null) return;
            _panel.SetAccent(truth.Accent);
            if (_panel.TruthChip != null)
            {
                _panel.TruthChip.text = ContextCard.TruthChipText(truth);
            }
        }

        /// <summary>Called by a gesture/voice confirmation that the step was done.</summary>
        public void ConfirmDone()
        {
            RaiseActed(new System.Collections.Generic.Dictionary<string, object>
            {
                { "kind", "task_step_done" },
                { "step", IntentRead.Content(Intent, "step", null) }
            });
            BeginFadeOut(false);
        }

        private void PlaceLateral()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null) return;
            transform.SetPositionAndRotation(
                cam.transform.TransformPoint(_lateralOffset),
                Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up));
        }

        protected override void Update()
        {
            base.Update();
            if (Phase != UIComponentPhase.Idle)
            {
                PlaceLateral();
                _panel?.SetAlpha(CurrentAlpha);
            }
        }
    }
}
