using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Studio profile card for an enrolled face or a sourced Web candidate.
    /// A Web candidate is always rendered as probable and never silently names a
    /// SceneCache entity. Public links are bounded and carry their verification
    /// mode; no age/race/emotion inference is displayed.
    /// </summary>
    public sealed class PersonProfileCard : UIComponentBase
    {
        [SerializeField] private Vector2 _size = new Vector2(0.48f, 0.35f);
        [SerializeField] private float _planeDistance = 1.25f;

        private GlassPanel _panel;
        private Rect _bbox;
        private bool _hasBbox;

        public override string ComponentKey => "person_profile_card";

        protected override void OnConfigured()
        {
            _panel = new GlassPanel(
                transform,
                _size,
                Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true,
                withBody: true,
                withTruthChip: true);
            if (_panel.Body != null)
            {
                _panel.Body.fontSize = 0.027f;
                _panel.Body.lineSpacing = 8f;
                _panel.Body.richText = true;
            }
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            _hasBbox = IntentRead.TryRect(intent.Anchor, "bbox", out _bbox);
            Render(intent);
            Place();
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            _panel?.SetAccent(truth.Accent);
            if (_panel?.TruthChip != null)
                _panel.TruthChip.text = ContextCard.TruthChipText(truth);
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle) return;
            Place();
            _panel?.SetAlpha(CurrentAlpha);
        }

        private void Render(Contracts.V19.UIIntent intent)
        {
            if (_panel == null) return;
            string name = IntentRead.Content(intent, "name", "Profil");
            bool confirmation = IntentRead.Flag(
                intent?.Content, "requires_confirmation", false);
            if (_panel.Title != null)
            {
                _panel.Title.text =
                    "<color=#7FE7FF>◈</color> " + Escape(name) +
                    (confirmation
                        ? "  <size=66%><color=#FFD24A>À CONFIRMER</color></size>"
                        : "  <size=66%><color=#67F0C1>CONSENTI</color></size>");
            }
            if (_panel.Body == null) return;
            var text = new StringBuilder(520);
            string summary = IntentRead.Content(intent, "summary", "");
            if (!string.IsNullOrWhiteSpace(summary))
                text.Append(Escape(summary)).Append("\n\n");
            JArray sources = ReadArray(intent, "public_sources");
            int shown = 0;
            foreach (JToken token in sources)
            {
                if (shown++ >= 6 || token.Type != JTokenType.Object) break;
                string provider = token.Value<string>("provider") ?? "web";
                string handle = token.Value<string>("handle") ?? "";
                string verification =
                    token.Value<string>("verification") ??
                    (token.Value<string>("verified_at") == null
                        ? "source"
                        : "vérifié");
                text.Append("<color=#7FE7FF>• ")
                    .Append(Escape(provider))
                    .Append("</color>");
                if (!string.IsNullOrWhiteSpace(handle))
                    text.Append("  ").Append(Escape(handle));
                text.Append(" <size=70%><color=#8FA3B8>")
                    .Append(Escape(verification))
                    .Append("</color></size>\n");
            }
            if (shown == 0)
                text.Append("<color=#8FA3B8>Aucune source publique vérifiée.</color>");
            _panel.Body.text = text.ToString();
        }

        private void Place()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null) return;
            Vector2 viewport = _hasBbox
                ? new Vector2(
                    Mathf.Clamp01(_bbox.xMax + 0.04f),
                    Mathf.Clamp01(1f - _bbox.center.y))
                : new Vector2(0.73f, 0.58f);
            Vector3 position = cam.ViewportPointToRay(viewport)
                .GetPoint(_planeDistance);
            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(
                    position - cam.transform.position,
                    Vector3.up));
        }

        private static JArray ReadArray(
            Contracts.V19.UIIntent intent,
            string key)
        {
            if (intent?.Content == null ||
                !intent.Content.TryGetValue(key, out object value) ||
                value == null)
                return new JArray();
            try { return value as JArray ?? JArray.FromObject(value); }
            catch { return new JArray(); }
        }

        private static string Escape(string value) =>
            (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
    }
}
