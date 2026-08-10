// MLOmega V19 — E53 (Viki mode aide) — atom 7/12
// ChecklistCard: a tickable list of the current step's ingredients / parts / tools.
// Rows come from a string list (content-driven); each row can be checked off, which
// dims and ✓-prefixes it. Rendered as a compact glass panel, head-locked laterally.
// Distinct from TaskPanel (which is the STEP plan) — this is the item list WITHIN a
// step ("bol · farine · œufs" / "vis M4 ×4 · clé Allen").
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class ChecklistCard : TaskAtom
    {
        [SerializeField] private Vector2 _size = new Vector2(0.34f, 0.24f);
        [SerializeField] private Vector3 _lateralOffset = new Vector3(-0.34f, -0.18f, 1.15f);

        private GlassPanel _panel;
        private readonly List<TMP_Text> _rows = new List<TMP_Text>();
        private readonly List<bool> _checked = new List<bool>();
        private readonly List<string> _items = new List<string>();

        protected override void Build()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true, withBody: false, withTruthChip: false);
            if (_panel.Title != null) _panel.Title.text = "À réunir";

            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject($"Item{i}", typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(_panel.Root, false);
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.sizeDelta = new Vector2(-0.06f, 0.036f);
                rt.anchoredPosition = new Vector2(0, -(0.08f + i * 0.034f));
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 0.030f;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.color = Theme != null ? Theme.TextColor : Color.white;
                tmp.raycastTarget = false;
                tmp.gameObject.SetActive(false);
                _rows.Add(tmp);
            }
        }

        public void SetItems(string title, IReadOnlyList<string> items)
        {
            if (_panel?.Title != null && !string.IsNullOrEmpty(title)) _panel.Title.text = title;
            _items.Clear(); _checked.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                bool has = items != null && i < items.Count;
                _rows[i].gameObject.SetActive(has);
                if (has)
                {
                    _items.Add(items[i]);
                    _checked.Add(false);
                }
            }
            RenderRows();
        }

        /// <summary>Tick/untick an item by index (from a voice/gesture confirmation upstream).</summary>
        public void SetChecked(int index, bool done)
        {
            if (index < 0 || index >= _checked.Count) return;
            _checked[index] = done;
            RenderRows();
        }

        private void RenderRows()
        {
            for (int i = 0; i < _items.Count && i < _rows.Count; i++)
            {
                _rows[i].text = _checked[i]
                    ? $"<color=#6FBF9A>✓</color> <color=#8FA3B8>{_items[i]}</color>"
                    : $"<color=#C9D6E4>◻</color> {_items[i]}";
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
        }
    }
}
