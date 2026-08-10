// MLOmega V19 — E25
// Runtime demo driver for the E25 scene (built by Editor/E25SceneBuilder). On
// Start it seeds the SceneCache with a couple of simulated tracks (so the anchored
// components have something to follow) and emits, through a LocalIntentSource, one
// UIIntent for each of the nine admitted components plus a translation/subtitle —
// covering every truth level so the §17.2 badges/ages/hypothesis labels are all
// visible at once. This lets the whole design system be eyeballed in the editor
// Play mode without a PC/transport. It is demo-only: nothing here ships in a device
// build (the scene is a validation harness), but it lives in the runtime assembly
// so it can run in Play mode.
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Scene;
using UnityEngine;

namespace MLOmega.XR.UI
{
    public sealed class E25DemoDriver : MonoBehaviour
    {
        [SerializeField] private SceneCache _sceneCache;
        [SerializeField] private LocalIntentSource _source;
        [SerializeField] private UIIntentBroker _broker;

        [Tooltip("Seconds to wait after start before injecting the demo intents.")]
        [SerializeField] private float _delay = 0.5f;

        private bool _done;
        private float _t;

        private void Awake()
        {
            if (_sceneCache == null) _sceneCache = FindAnyObjectByType<SceneCache>();
            if (_broker == null) _broker = FindAnyObjectByType<UIIntentBroker>();
            if (_source == null) _source = FindAnyObjectByType<LocalIntentSource>();
        }

        private void Update()
        {
            if (_done) return;
            _t += Time.unscaledDeltaTime;
            if (_t < _delay) return;
            _done = true;
            SeedTracks();
            EmitAll();
        }

        private void SeedTracks()
        {
            if (_sceneCache == null) return;
            _sceneCache.SubmitLocalTrack(Track("obj-1", 0.55f, 0.5f, 0.18f, 0.18f));
            _sceneCache.SubmitLocalTrack(Track("face-1", 0.35f, 0.4f, 0.14f, 0.2f));
            // Spatial + map quality so the OffscreenArrow is allowed to draw.
            var delta = new SceneDelta
            {
                MapQuality = 0.8,
                Entities = new List<Dictionary<string, object>>(),
                Changes = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { { "entity_id", "keys-1" }, { "bearing_deg", 35.0 } }
                }
            };
            _sceneCache.SubmitSceneDelta(delta);
        }

        private LocalTrack Track(string id, float x, float y, float w, float h) => new LocalTrack
        {
            TrackId = id,
            Kind = "object",
            Visibility = 1.0,
            Confidence = 0.9,
            BboxOrMask = new Dictionary<string, object> { { "x", x }, { "y", y }, { "w", w }, { "h", h } }
        };

        private void EmitAll()
        {
            if (_source == null) return;

            Emit("ObjectOutline", "object_outline", "observed", trackId: "obj-1",
                new Dictionary<string, object> { { "label", "mug" } });

            Emit("PersonTag", "person_tag", "probable", trackId: "face-1",
                new Dictionary<string, object> { { "name", "Alex" }, { "identity_confidence", 0.71 } });

            Emit("Subtitle", "subtitle", "observed", trackId: null,
                new Dictionary<string, object> { { "text", "Hello, how are you?" }, { "final", true }, { "language", "en" } });

            Emit("LensWindow", "lens_window", "observed", trackId: null,
                new Dictionary<string, object> { { "title", "Lens" }, { "text", "SN: 4471-882" } },
                focus: true);

            Emit("OffscreenArrow", "offscreen_arrow", "remembered", trackId: null,
                new Dictionary<string, object> { { "label", "keys" }, { "age_ms", 240000.0 } },
                entityId: "keys-1");

            Emit("ContextCard", "context_card", "inferred", trackId: null,
                new Dictionary<string, object> { { "title", "Relation" }, { "text", "Likely a colleague from the 3pm meeting." }, { "source", "BrainLive" } });

            Emit("TaskCard", "task_card", "observed", trackId: null,
                new Dictionary<string, object> { { "goal", "Assemble shelf" }, { "step", "Insert dowel A into panel B" }, { "tool", "hex key 4mm" } });

            Emit("VirtualScreen", "virtual_screen", "observed", trackId: null,
                new Dictionary<string, object> { { "title", "Notes" } });

            Emit("CorrectionChip", "correction_chip", "probable", trackId: "face-1",
                new Dictionary<string, object> { { "label", "✎ Not Alex?" }, { "correction", "identity" } });
        }

        private void Emit(string idSuffix, string component, string truth, string trackId,
            Dictionary<string, object> content, string entityId = null, bool focus = false)
        {
            var intent = new UIIntent
            {
                ContractsVersion = "v19.0",
                UiIntentId = "demo-" + idSuffix,
                Producer = "brainlive",
                Component = component,
                TargetTrackId = trackId,
                EntityId = entityId,
                TruthLevel = truth,
                Confidence = 0.8,
                Priority = 0.5,
                TtlMs = 60000,
                Content = content,
                UiHint = focus ? new Dictionary<string, object> { { "focus", true } } : null
            };
            _source.Emit(intent);
        }
    }
}
