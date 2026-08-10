// MLOmega V19 — E25
// CorrectionChip (§13.1, §17.1 "correction simple"): a small chip letting the user
// correct an entity / memory / UI label. Activating it raises a `corrected` receipt
// (§13.3) which suspends/rectifies the target and triggers a downstream revision,
// then the chip fades. Any source can attach one. Carries the target ref so the PC
// side knows what to revise. Being a control it is a raycast target (the only
// component whose glass is interactive).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MLOmega.XR.UI.Components
{
    public sealed class CorrectionChip : UIComponentBase, IPointerClickHandler
    {
        [SerializeField] private Vector2 _size = new Vector2(0.30f, 0.09f);
        [SerializeField] private Vector3 _offset = new Vector3(0.30f, 0.20f, 1.0f);

        private GlassPanel _panel;

        public override string ComponentKey => "correction_chip";

        protected override void OnConfigured()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: false, withBody: true, withTruthChip: false);
            if (_panel.Body != null)
            {
                _panel.Body.alignment = TMPro.TextAlignmentOptions.Center;
                _panel.Body.fontSize = 0.042f;
            }
            // This is the one interactive glass surface.
            if (_panel.Background != null) _panel.Background.raycastTarget = true;
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            if (_panel.Body != null)
            {
                _panel.Body.text = IntentRead.Content(intent, "label", "✎ Correct");
            }
            Place();
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            _panel?.SetAccent(truth.Accent);
        }

        public void OnPointerClick(PointerEventData eventData) => Activate();

        /// <summary>Activate the correction (pointer/voice/gesture) — emits `corrected`.</summary>
        public void Activate()
        {
            var action = new Dictionary<string, object>
            {
                { "kind", "correction" },
                { "target_track_id", Intent?.TargetTrackId },
                { "entity_id", Intent?.EntityId },
                { "correction", IntentRead.Content(Intent, "correction", null) }
            };
            RaiseCorrected(action);
            BeginFadeOut(false); // corrected already emitted; fade without a dismissed
        }

        private void Place()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null) return;
            transform.SetPositionAndRotation(
                cam.transform.TransformPoint(_offset),
                Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up));
        }

        protected override void Update()
        {
            base.Update();
            if (Phase != UIComponentPhase.Idle)
            {
                Place();
                _panel?.SetAlpha(CurrentAlpha);
            }
        }
    }
}
