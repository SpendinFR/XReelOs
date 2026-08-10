// MLOmega V19 — E53 (Viki mode aide)
// TaskPanelComponent: the admitted UIComponentBase behind the `task_panel` intent.
// It plugs straight into the E25 dispatch (UIComponentRegistry → UIRuntime pooling/
// receipts/TTL), so it inherits displayed/seen/dismissed and the fade lifecycle for
// free — it only owns the COMPOSITION of the plan-family atoms:
//   * TaskPanel        — the step plan (done/current/ghost-next) + docked progress
//   * InstructionCard  — the rich text of the current step
//   * ChecklistCard    — the current step's item list (when steps carry items)
// Content is parsed once on Bind/Refresh (TaskPanelContent). On an N+1 step change
// the PC re-sends the same task_panel intent id → UIRuntime routes it to Refresh →
// the ghost row is promoted to current WITHOUT re-instantiating anything (0-latency
// transition). All atoms are driven by this component's animated alpha + Tick, so
// there is a single receipt owner per the E25 rule.
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class TaskPanelComponent : UIComponentBase
    {
        private TaskPanel _panel;
        private InstructionCard _instruction;
        private ChecklistCard _checklist;

        public override string ComponentKey => "task_panel";

        // Exposed for EditMode composition assertions.
        public TaskPanel PanelAtom => _panel;
        public InstructionCard InstructionAtom => _instruction;
        public ChecklistCard ChecklistAtom => _checklist;

        protected override void OnConfigured()
        {
            _panel = AddAtom<TaskPanel>("TaskPanelAtom");
            _instruction = AddAtom<InstructionCard>("InstructionAtom");
            _checklist = AddAtom<ChecklistCard>("ChecklistAtom");
            _checklist.SetVisible(false); // shown only when the current step carries items
        }

        private T AddAtom<T>(string name) where T : TaskAtom
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var atom = go.AddComponent<T>();
            atom.Init(Context, Theme);
            return atom;
        }

        protected override void Bind(UIIntent intent)
        {
            TaskPanelContent plan = TaskPanelContent.From(intent);
            _panel.SetPlan(plan);

            // Current step drives the instruction card + optional checklist.
            TaskStep current = FindCurrent(plan, out int stepNumber);
            _instruction.SetInstruction(
                current.Text ?? plan.Title,
                stepNumber,
                plan.Steps.Count,
                null);

            List<string> items = ReadItems(intent, current.Index);
            if (items != null && items.Count > 0)
            {
                _checklist.SetItems("À réunir", items);
                _checklist.SetVisible(true);
            }
            else
            {
                _checklist.SetVisible(false);
            }
        }

        private static TaskStep FindCurrent(TaskPanelContent plan, out int stepNumber)
        {
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                if (plan.Steps[i].Status == StepStatus.Current)
                {
                    stepNumber = i + 1;
                    return plan.Steps[i];
                }
            }
            stepNumber = plan.Steps.Count > 0 ? 1 : 0;
            return plan.Steps.Count > 0 ? plan.Steps[0] : new TaskStep(0, plan.Title, StepStatus.Current);
        }

        // Optional per-step item list: content.step_items = { "<index>": [ ... ] }.
        private static List<string> ReadItems(UIIntent intent, int stepIndex)
        {
            Dictionary<string, object> content = intent?.Content;
            if (content == null) return null;
            if (!content.TryGetValue("step_items", out object raw) ||
                !(raw is Dictionary<string, object> map)) return null;
            if (!map.TryGetValue(stepIndex.ToString(), out object listObj) ||
                !(listObj is IList<object> list)) return null;
            var result = new List<string>(list.Count);
            foreach (object o in list) if (o != null) result.Add(o.ToString());
            return result;
        }

        protected override void ApplyVisual() { } // atoms are placed in Tick, not scaled

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle) return;
            TickAtoms(Time.unscaledTime, Time.unscaledDeltaTime);
        }

        /// <summary>Deterministic atom step (test-drivable; mirrors SceneCache.Tick).</summary>
        public void TickAtoms(float now, float dt)
        {
            if (Phase == UIComponentPhase.Idle) return;
            _panel.SetAlpha(CurrentAlpha); _panel.Tick(now, dt);
            _instruction.SetAlpha(CurrentAlpha); _instruction.Tick(now, dt);
            if (_checklist.gameObject.activeSelf)
            {
                _checklist.SetAlpha(CurrentAlpha); _checklist.Tick(now, dt);
            }
        }
    }
}
