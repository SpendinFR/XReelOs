using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// UltraLive object sheet: immediate detector identity + optional actions.
    /// Expensive identification/manual work is requested only after an explicit
    /// action receipt; the initial card never waits for a VLM/LLM.
    /// </summary>
    public sealed class ObjectProfileCard : UIComponentBase
    {
        public sealed class ActionView
        {
            public string Id;
            public string Label;
            public string Kind;
            public bool RequiresConfirmation;
            public bool StateChange;
        }

        private static readonly List<ObjectProfileCard> Live =
            new List<ObjectProfileCard>();
        public static IReadOnlyList<ObjectProfileCard> ActiveCards => Live;

        [SerializeField] private Vector2 _size = new Vector2(0.46f, 0.34f);
        [SerializeField] private float _planeDistance = 1.15f;
        [SerializeField] private float _confirmSeconds = 4f;

        private readonly List<ActionView> _actions = new List<ActionView>();
        private GlassPanel _panel;
        private Rect _bbox;
        private bool _hasBbox;
        private int _hover = -1;
        private string _confirmAction;
        private float _confirmUntil;
        private string _label;
        private string _entityId;
        private string _manualRef;
        private string _appId;

        public override string ComponentKey => "object_profile_card";
        public IReadOnlyList<ActionView> Actions => _actions;

        protected override void OnConfigured()
        {
            _panel = new GlassPanel(
                transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true, withBody: true, withTruthChip: true);
            if (_panel.Body != null)
            {
                _panel.Body.fontSize = 0.029f;
                _panel.Body.lineSpacing = 8f;
                _panel.Body.richText = true;
            }
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            _label = IntentRead.Content(intent, "title",
                IntentRead.Content(intent, "label", "Objet"));
            _entityId = intent.EntityId ?? string.Empty;
            _manualRef = IntentRead.Content(intent, "manual_ref", "");
            _appId = IntentRead.Content(intent, "app_id", "");
            _hasBbox = IntentRead.TryRect(intent.Anchor, "bbox", out _bbox);
            ParseActions(intent);
            RefreshText();
            PlaceBesideObject();
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
            if (Phase == UIComponentPhase.Idle)
            {
                Live.Remove(this);
                return;
            }
            if (!Live.Contains(this)) Live.Add(this);
            if (_confirmAction != null && Time.unscaledTime > _confirmUntil)
            {
                _confirmAction = null;
                RefreshText();
            }
            PlaceBesideObject();
            _panel?.SetAlpha(CurrentAlpha);
        }

        private void OnDisable() => Live.Remove(this);

        public int ResolveActionAtViewport(Vector2 viewportPoint)
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (Phase == UIComponentPhase.Idle || _panel?.Body == null || cam == null ||
                viewportPoint.x < 0f || viewportPoint.y < 0f || _actions.Count == 0)
                return -1;
            RectTransform body = _panel.Body.rectTransform;
            Ray ray = cam.ViewportPointToRay(
                new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            Plane plane = new Plane(-body.forward, body.position);
            if (!plane.Raycast(ray, out float enter)) return -1;
            Vector3 local3 = body.InverseTransformPoint(ray.GetPoint(enter));
            Rect rect = body.rect;
            Vector2 local = new Vector2(local3.x, local3.y);
            if (!rect.Contains(local)) return -1;
            // The first 55% of the body is the description; actions occupy the rest.
            float actionTop = rect.yMax - rect.height * 0.55f;
            if (local.y > actionTop) return -1;
            float actionHeight = rect.height * 0.45f;
            float fromTop = actionTop - local.y;
            return Mathf.Clamp(
                Mathf.FloorToInt(fromTop / actionHeight * _actions.Count),
                0, _actions.Count - 1);
        }

        public void HoverAtViewport(Vector2 viewportPoint)
        {
            int next = ResolveActionAtViewport(viewportPoint);
            if (next == _hover) return;
            _hover = next;
            RefreshText();
        }

        public bool PinchCommit()
        {
            if (_hover < 0 || _hover >= _actions.Count) return false;
            ActionView action = _actions[_hover];
            bool confirmed = !action.RequiresConfirmation ||
                (_confirmAction == action.Id && Time.unscaledTime <= _confirmUntil);
            if (!confirmed)
            {
                _confirmAction = action.Id;
                _confirmUntil = Time.unscaledTime + _confirmSeconds;
                RefreshText();
                return true;
            }
            _confirmAction = null;
            RaiseActed(new Dictionary<string, object>
            {
                { "kind", "ar_object_action" },
                { "action_id", action.Id },
                { "action_kind", action.Kind },
                { "entity_id", _entityId },
                { "label", _label },
                { "manual_ref", _manualRef },
                { "app_id", _appId },
                { "confirmed", confirmed },
                { "bbox", new Dictionary<string, object>
                    {
                        { "x", _bbox.x }, { "y", _bbox.y },
                        { "w", _bbox.width }, { "h", _bbox.height },
                    }
                },
            });
            RefreshText();
            return true;
        }

        private void ParseActions(Contracts.V19.UIIntent intent)
        {
            _actions.Clear();
            if (intent?.Content == null ||
                !intent.Content.TryGetValue("actions", out object raw) || raw == null)
                return;
            JArray array;
            try { array = raw as JArray ?? JArray.FromObject(raw); }
            catch { return; }
            foreach (JToken token in array)
            {
                if (_actions.Count >= 5 || token.Type != JTokenType.Object) break;
                string id = token.Value<string>("action_id");
                string label = token.Value<string>("label");
                string kind = token.Value<string>("kind");
                if (string.IsNullOrWhiteSpace(id) ||
                    string.IsNullOrWhiteSpace(label) ||
                    string.IsNullOrWhiteSpace(kind))
                    continue;
                _actions.Add(new ActionView
                {
                    Id = id,
                    Label = label,
                    Kind = kind,
                    RequiresConfirmation =
                        token.Value<bool?>("requires_confirmation") == true,
                    StateChange = token.Value<bool?>("state_change") == true,
                });
            }
        }

        private void RefreshText()
        {
            if (_panel == null) return;
            if (_panel.Title != null)
                _panel.Title.text =
                    $"<color=#7FE7FF>◈</color> {_label}  <size=68%><color=#67F0C1>LIVE</color></size>";
            if (_panel.Body == null) return;
            string summary = IntentRead.Content(Intent, "summary", "");
            string category = IntentRead.Content(Intent, "category", "");
            var text = new System.Text.StringBuilder(420);
            if (!string.IsNullOrEmpty(category))
                text.Append("<color=#9FB3C8>").Append(category).Append("</color>\n");
            text.Append(summary);
            if (_actions.Count > 0) text.Append("\n\n");
            for (int i = 0; i < _actions.Count; i++)
            {
                ActionView action = _actions[i];
                bool confirmation = _confirmAction == action.Id &&
                    Time.unscaledTime <= _confirmUntil;
                text.Append(i == _hover
                    ? "<color=#7FE7FF>› "
                    : "<color=#8FA3B8>  ");
                text.Append(confirmation ? "CONFIRMER " : "");
                text.Append(action.Label).Append("</color>");
                if (i + 1 < _actions.Count) text.Append('\n');
            }
            _panel.Body.text = text.ToString();
        }

        private void PlaceBesideObject()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null) return;
            Vector2 anchor = _hasBbox
                ? new Vector2(Mathf.Clamp01(_bbox.xMax + 0.035f),
                    Mathf.Clamp01(1f - _bbox.center.y))
                : new Vector2(0.72f, 0.58f);
            Ray ray = cam.ViewportPointToRay(anchor);
            Vector3 position = ray.GetPoint(_planeDistance);
            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(position - cam.transform.position, Vector3.up));
        }
    }
}
