// MLOmega V19 — E53 (Viki mode aide) — atom 1/12
// TaskPanel: the floating plan window. It lists the step plan compactly —
//   done     → dim, ✓
//   current  → bright, an animated accent bar that breathes so the eye lands there
//   next     → shown as a pre-computed GHOST (faint) so the N+1 transition is 0-latency
//   pending  → hidden or collapsed to keep it compact
// plus the domain/title and a docked TaskProgressBar. Content-driven only: every
// row comes from task_panel.steps; nothing is hard-coded. Head-locked lateral
// panel, like the E25 ContextCard/TaskCard, so it stays legible as the head moves.
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class TaskPanel : TaskAtom
    {
        [SerializeField] private Vector2 _size = new Vector2(0.46f, 0.30f);
        [SerializeField] private Vector3 _lateralOffset = new Vector3(-0.34f, 0.06f, 1.15f);
        [Tooltip("How many upcoming/pending steps to reveal beyond the current one.")]
        [SerializeField] private int _lookahead = 2;

        private GlassPanel _panel;
        private readonly List<TMP_Text> _rows = new List<TMP_Text>();
        private TaskProgressBar _bar;
        private int _currentRowIndex = -1;
        private float _pulse;

        protected override void Build()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true, withBody: false, withTruthChip: false);

            // Rows are TMP labels stacked under the title.
            for (int i = 0; i < 6; i++)
            {
                var go = new GameObject($"Step{i}", typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(_panel.Root, false);
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.sizeDelta = new Vector2(-0.06f, 0.042f);
                rt.anchoredPosition = new Vector2(0, -(0.09f + i * 0.040f));
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 0.032f;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.color = Theme != null ? Theme.TextColor : Color.white;
                tmp.raycastTarget = false;
                _rows.Add(tmp);
            }

            var barGo = new GameObject("PanelProgress");
            barGo.transform.SetParent(_panel.Root, false);
            _bar = barGo.AddComponent<TaskProgressBar>();
            _bar.Init(Context, Theme);
            _bar.SetWidth(_size.x - 0.06f);
            _bar.SetLocal(new Vector3(-(_size.x - 0.06f) * 0.5f, -_size.y * 0.5f + 0.02f, 0f));
        }

        /// <summary>Fill the panel from parsed content.</summary>
        public void SetPlan(TaskPanelContent plan)
        {
            if (_panel?.Title != null)
            {
                _panel.Title.text = string.IsNullOrEmpty(plan.Domain)
                    ? plan.Title
                    : $"{plan.Title}  <size=70%><color=#9FB3C8>{plan.Domain}</color></size>";
            }
            _bar?.SetProgress(plan.Progress);
            _currentRowIndex = -1;

            // Choose a compact window: all done steps collapse to the last one + current + lookahead.
            int visibleRow = 0;
            for (int i = 0; i < plan.Steps.Count && visibleRow < _rows.Count; i++)
            {
                TaskStep step = plan.Steps[i];
                bool ghost = step.Status == StepStatus.Next && plan.GhostNext;

                // Keep it compact: skip far-future pending steps beyond the lookahead.
                if (step.Status == StepStatus.Pending && !WithinLookahead(plan, i)) continue;

                TMP_Text row = _rows[visibleRow];
                row.gameObject.SetActive(true);
                row.text = Format(step, ghost);
                if (step.Status == StepStatus.Current) _currentRowIndex = visibleRow;
                visibleRow++;
            }
            for (int r = visibleRow; r < _rows.Count; r++) _rows[r].gameObject.SetActive(false);
        }

        private bool WithinLookahead(TaskPanelContent plan, int i)
        {
            // Reveal pending steps only within _lookahead of the current step.
            int cur = -1;
            for (int k = 0; k < plan.Steps.Count; k++)
                if (plan.Steps[k].Status == StepStatus.Current) { cur = k; break; }
            if (cur < 0) return i < _lookahead;
            return i <= cur + _lookahead;
        }

        private static string Format(TaskStep s, bool ghost)
        {
            switch (s.Status)
            {
                case StepStatus.Done:
                    return $"<color=#6FBF9A>✓</color> <color=#8FA3B8><s>{s.Text}</s></color>";
                case StepStatus.Current:
                    return $"<b>▶ {s.Text}</b>";
                case StepStatus.Next:
                    return ghost
                        ? $"<alpha=#80><color=#9FB3C8>… {s.Text}</color>"
                        : $"<color=#C9D6E4>· {s.Text}</color>";
                default:
                    return $"<alpha=#55><color=#8FA3B8>· {s.Text}</color>";
            }
        }

        public override void Tick(float now, float dt)
        {
            Camera cam = Cam;
            if (cam != null)
            {
                Vector3 pos = cam.transform.TransformPoint(_lateralOffset);
                transform.SetPositionAndRotation(pos,
                    Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
            }
            _panel?.SetAlpha(Alpha);
            _bar?.Tick(now, dt);

            // Breathing accent on the current row so the eye lands on it.
            if (_currentRowIndex >= 0 && _currentRowIndex < _rows.Count)
            {
                _pulse += dt * 2.2f;
                float b = 0.75f + 0.25f * (0.5f + 0.5f * Mathf.Sin(_pulse));
                Color accent = Theme != null ? Theme.AccentFor("observed") : Color.green;
                _rows[_currentRowIndex].color = WithAlpha(accent, b);
            }
            // Push panel alpha into every visible row.
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i == _currentRowIndex) continue;
                if (!_rows[i].gameObject.activeSelf) continue;
                Color c = _rows[i].color; c.a = Alpha; _rows[i].color = c;
            }
        }
    }
}
