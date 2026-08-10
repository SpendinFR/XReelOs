// MLOmega V19 â€” E26
// FocusSearchSkill (Â§9.2/Â§14.6): resolve a spoken "where is X" against the
// SceneCache FIRST (local-first), then the PC only on a miss:
//   * entity visible now (in entities_hot / tracks) â†’ object_outline;
//   * known but not visible (spatial_hot last-seen) â†’ context_card with age + a
//     prudent arrow only if map_quality clears the threshold (Â§17.2);
//   * otherwise â†’ if transport connected, send a DataChannel VisionRT request and
//     show a discreet spinner; if not connected, show an HONEST last-seen/miss
//     message (never a fake arrow).
// The DataChannel request is a UIReceipt-like control message sent through an
// injected sender so the skill has no hard transport dependency (and tests can
// assert it fired).
using System;
using System.Collections.Generic;
using MLOmega.XR.Scene;
using UnityEngine;

namespace MLOmega.XR.Reflex.Skills
{
    public sealed class FocusSearchSkill : ReflexSkillBase
    {
        [SerializeField] private LocalTrackStore _trackStore;

        public override ReflexSkillId SkillId => ReflexSkillId.FocusSearch;

        /// <summary>Outcome of a focus search, for callers/tests to assert.</summary>
        public enum SearchOutcome { VisibleOutline, LastSeenCard, RequestedVisionRt, HonestMiss }

        /// <summary>
        /// Sends a VisionRT search request over the DataChannel. Injected so the
        /// skill stays transport-agnostic. Return true if the request was sent
        /// (transport connected), false if there is no connection.
        /// </summary>
        public Func<string, bool> VisionRtRequestSender { get; set; }

        /// <summary>Whether the PC transport is currently connected (set by the scheduler).</summary>
        public bool TransportConnected { get; set; }

        protected override void Awake()
        {
            base.Awake();
            if (_trackStore == null) _trackStore = FindAnyObjectByType<LocalTrackStore>();
        }

        /// <summary>
        /// Locate a named target. Deterministic: returns the chosen outcome and
        /// emits the matching UIIntent. `entityId` is optional; when absent the
        /// query string is matched against entities_hot labels.
        /// </summary>
        public SearchOutcome Locate(string query, string entityId = null)
        {
            if (!IsActive) return SearchOutcome.HonestMiss;
            long now = NowMs();
            SceneCache cache = _trackStore != null ? _trackStore.SceneCache : null;

            string resolvedEntity = entityId;
            string visibleTrack = null;
            if (cache != null)
            {
                resolvedEntity ??= MatchEntity(cache, query, out visibleTrack);
                if (resolvedEntity != null && visibleTrack == null)
                {
                    visibleTrack = TrackForEntity(cache, resolvedEntity);
                }
            }

            // 1) Visible now â†’ outline.
            if (visibleTrack != null && cache.Tracks.Contains(visibleTrack))
            {
                var intent = NewIntent("object_outline", "focus_" + Norm(query));
                intent.TargetTrackId = visibleTrack;
                intent.Content["label"] = query;
                EmitIntent(intent);
                RecordReflex("focus_search:" + Norm(query),
                    Pred(query, "visible"), 0, 1.0, "info", now);
                return SearchOutcome.VisibleOutline;
            }

            // 2) Known but not visible â†’ last-seen card (+ prudent arrow if map qualifies).
            if (resolvedEntity != null && cache != null &&
                cache.SpatialHot.TryGet(resolvedEntity, out SceneCache.SpatialHotEntry sp))
            {
                var card = NewIntent("context_card", "focus_" + Norm(query));
                card.EntityId = resolvedEntity;
                card.TruthLevel = "remembered";
                card.Content["title"] = query;
                card.Content["text"] = "last seen";
                long ageMs = now - sp.LastSeenMs;
                card.UiHint["age_ms"] = ageMs;
                bool arrowOk = cache.SpatialHot.ArrowAllowed(
                    cache.Config != null ? cache.Config.MapQualityArrowThreshold : 0.55f) && sp.HasBearing;
                if (arrowOk) card.Content["bearing_deg"] = sp.BearingDeg;
                EmitIntent(card);
                RecordReflex("focus_search:" + Norm(query),
                    Pred(query, "last_seen"), 0, 0.8, "info", now);
                return SearchOutcome.LastSeenCard;
            }

            // 3) Miss â†’ VisionRT if connected (spinner), else honest message.
            if (TransportConnected && VisionRtRequestSender != null && VisionRtRequestSender(query))
            {
                var spinner = NewIntent("context_card", "focus_" + Norm(query));
                spinner.TruthLevel = "probable";
                spinner.Content["title"] = query;
                spinner.Content["text"] = "searchingâ€¦";
                spinner.UiHint["spinner"] = true;
                spinner.TtlMs = (long)((_config != null ? _config.FocusSearchTimeoutSeconds : 3f) * 1000f);
                EmitIntent(spinner);
                RecordReflex("focus_search:" + Norm(query),
                    Pred(query, "requested_visionrt"), 0, 0.5, "info", now);
                return SearchOutcome.RequestedVisionRt;
            }

            var honest = NewIntent("context_card", "focus_" + Norm(query));
            honest.TruthLevel = "remembered";
            honest.Content["title"] = query;
            honest.Content["text"] = "not seen recently";
            EmitIntent(honest);
            RecordReflex("focus_search:" + Norm(query),
                Pred(query, "honest_miss"), 0, 0.3, "info", now);
            return SearchOutcome.HonestMiss;
        }

        private static string MatchEntity(SceneCache cache, string query, out string trackId)
        {
            trackId = null;
            string q = Norm(query);
            foreach (SceneCache.EntityHot e in cache.EntitiesHot.All)
            {
                if (!string.IsNullOrEmpty(e.Label) && Norm(e.Label).Contains(q))
                {
                    trackId = e.TrackId;
                    return e.EntityId;
                }
            }
            return null;
        }

        private static string TrackForEntity(SceneCache cache, string entityId)
        {
            if (cache.EntitiesHot.TryGet(entityId, out SceneCache.EntityHot e)) return e.TrackId;
            return null;
        }

        private Dictionary<string, object> Pred(string query, string via) =>
            new Dictionary<string, object> { { "query", query }, { "via", via } };

        private static string Norm(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Trim().ToLowerInvariant();
    }
}
