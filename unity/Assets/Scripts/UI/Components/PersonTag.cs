// MLOmega V19 — E25
// PersonTag (§13.1): a small label anchored ABOVE a face track — never over the
// face itself (offset up by the bbox height + a margin). It shows a name only when
// identity confidence clears the SceneCacheConfig threshold (§17.2 "faible
// confiance identité → aucun nom"); below it, it shows a neutral "person" tag with
// no name. A relational note stays a sourced hypothesis (§17.3). Follows the track
// each frame; the broker removes it if the track is lost.
using MLOmega.XR.Scene;
using System.Collections.Generic;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    public sealed class PersonTag : UIComponentBase
    {
        [SerializeField] private float _planeDistance = 1.6f;
        [SerializeField] private Vector2 _size = new Vector2(0.30f, 0.10f);
        [Tooltip("Extra upward margin above the face bbox (viewport fraction).")]
        [SerializeField] private float _aboveMargin = 0.06f;

        private GlassPanel _panel;
        private string _trackId;

        public override string ComponentKey => "person_tag";

        protected override void OnConfigured()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: false, withBody: true, withTruthChip: true);
            if (_panel.Body != null)
            {
                _panel.Body.alignment = TMPro.TextAlignmentOptions.Center;
                _panel.Body.fontSize = 0.045f;
            }
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            _trackId = intent.TargetTrackId;
            RenderName(intent);
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            if (_panel == null) return;
            _panel.SetAccent(truth.Accent);
            if (_panel.TruthChip != null) _panel.TruthChip.text = ContextCard.TruthChipText(truth);
        }

        private void RenderName(Contracts.V19.UIIntent intent)
        {
            string name = IntentRead.Content(intent, "name",
                IntentRead.Content(intent, "label", null));
            double confidence = IntentRead.Num(intent.Content, "identity_confidence",
                intent.Confidence);

            float threshold = 0.62f;
            if (Context != null && Context.SceneCache != null && Context.SceneCache.Config != null)
            {
                threshold = Context.SceneCache.Config.IdentityNameConfidenceThreshold;
            }

            bool nameAllowed = !string.IsNullOrEmpty(name) && confidence >= threshold;
            if (_panel.Body != null)
            {
                string relation = nameAllowed ? RelationSummary(intent.EntityId) : null;
                _panel.Body.text = nameAllowed
                    ? (string.IsNullOrEmpty(relation)
                        ? name
                        : $"{name}\n<size=72%><color=#9FB3C8>{relation}</color></size>")
                    : "person";
            }
        }

        private string RelationSummary(string entityId)
        {
            if (string.IsNullOrEmpty(entityId) || Context == null ||
                Context.SceneCache == null ||
                !Context.SceneCache.EntitiesHot.TryGetRelationPack(entityId, out EntityHotUpdate pack) ||
                pack?.RelationPack == null)
            {
                return null;
            }
            foreach (Dictionary<string, object> item in pack.RelationPack)
            {
                if (item == null) continue;
                foreach (string key in new[] { "summary", "relationship_type", "person_hint" })
                {
                    if (item.TryGetValue(key, out object value) && value != null)
                    {
                        string text = value.ToString();
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                }
            }
            return null;
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle) return;
            // entity_hot_update can arrive after the tag itself; refresh locally
            // so the prefetched CardProfil relation is actually consumed.
            if (Intent != null) RenderName(Intent);
            FollowFace();
            _panel?.SetAlpha(CurrentAlpha);
        }

        private void FollowFace()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null) return;

            // Default centre; place ABOVE the face bbox top.
            Vector2 anchor = new Vector2(0.5f, 0.35f);
            if (Context != null && Context.SceneCache != null &&
                Context.SceneCache.Tracks.TryGet(_trackId, out SceneCache.TrackEntry entry) &&
                entry.Track.BboxOrMask != null)
            {
                float x = (float)IntentRead.Num(entry.Track.BboxOrMask, "x", 0.4f);
                float y = (float)IntentRead.Num(entry.Track.BboxOrMask, "y", 0.4f);
                float w = (float)IntentRead.Num(entry.Track.BboxOrMask, "w",
                    (float)IntentRead.Num(entry.Track.BboxOrMask, "width", 0.2f));
                // Centre-x of the face, top of the bbox minus the above-margin.
                anchor = new Vector2(x + w * 0.5f, Mathf.Max(0f, y - _aboveMargin));
            }

            Ray ray = cam.ViewportPointToRay(new Vector3(anchor.x, 1f - anchor.y, 0f));
            Vector3 pos = ray.GetPoint(_planeDistance);
            transform.SetPositionAndRotation(pos,
                Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
        }
    }
}
