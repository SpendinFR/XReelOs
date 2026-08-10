// MLOmega V19 — E26
// StableTrackSkill (§9.2): keeps an ObjectOutline glued to the present via
// LocalTrackStore. Given a track that exists in SceneCache.tracks (fed by the
// PC when connected, or the pure-local TemplateTracker when not), it emits an
// object_outline UIIntent bound to that track_id. The outline sticks to the live
// track and — because the intent carries target_track_id — the broker fades it
// the instant the track leaves the cache (§13.2), so it never sticks to a stale
// object. Aggregated ReflexEvents (not per frame) feed the reflex audit.
using System.Collections.Generic;
using MLOmega.XR.Scene;
using UnityEngine;

namespace MLOmega.XR.Reflex.Skills
{
    public sealed class StableTrackSkill : ReflexSkillBase
    {
        [SerializeField] private LocalTrackStore _trackStore;

        public override ReflexSkillId SkillId => ReflexSkillId.StableTrack;

        private readonly Dictionary<string, string> _intentByTrack = new Dictionary<string, string>();
        private long _lastRefreshMs;

        protected override void Awake()
        {
            base.Awake();
            if (_trackStore == null) _trackStore = FindAnyObjectByType<LocalTrackStore>();
        }

        /// <summary>
        /// Start (or refresh) a stable outline on a track with a label. Called by the
        /// scheduler when a hand-near-object signal seeds a track, or by the demo/tests.
        /// </summary>
        public void TrackObject(string trackId, string label, string truthLevel = "observed")
        {
            if (!IsActive || string.IsNullOrEmpty(trackId)) return;
            long now = NowMs();

            if (!_intentByTrack.TryGetValue(trackId, out string intentId))
            {
                intentId = "outline_" + trackId;
                _intentByTrack[trackId] = intentId;
            }

            var intent = NewIntent("object_outline", intentId);
            intent.TargetTrackId = trackId;
            intent.TruthLevel = truthLevel;
            intent.Content["label"] = label ?? "";
            EmitIntent(intent);

            // One aggregated reflex event per refresh window, not per frame.
            RecordReflex(
                aggregateKey: "stable_track:" + trackId,
                prediction: new Dictionary<string, object> { { "track_id", trackId }, { "label", label ?? "" } },
                horizonMs: 0, confidence: 1.0, severity: "info", nowMs: now);
            _lastRefreshMs = now;
        }

        /// <summary>
        /// Refresh all tracked outlines that still exist in the cache; drop those
        /// whose track is gone. Cheap; safe to call every frame (throttled internally).
        /// </summary>
        public void RefreshFromCache()
        {
            if (!IsActive || _trackStore == null || _trackStore.SceneCache == null) return;
            long now = NowMs();
            long interval = _config != null ? _config.StableTrackReflexIntervalMs : 1000;
            if (now - _lastRefreshMs < interval) return;

            SceneCache cache = _trackStore.SceneCache;
            List<string> dead = null;
            foreach (KeyValuePair<string, string> kv in _intentByTrack)
            {
                if (!cache.Tracks.Contains(kv.Key)) (dead ??= new List<string>()).Add(kv.Key);
            }
            if (dead != null) foreach (string id in dead) _intentByTrack.Remove(id);
            _lastRefreshMs = now;
        }

        protected override void OnDeactivated() => _intentByTrack.Clear();

        private void Update()
        {
            if (IsActive) RefreshFromCache();
        }
    }
}
