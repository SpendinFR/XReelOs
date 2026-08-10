// MLOmega V19 — E26
// Base for every Ultra-Live skill. A skill NEVER talks to the broker directly: it
// emits its UIIntents through the shared LocalIntentSource (priority-2 "UL
// critique/focus", the existing E25 seam) and its aggregated ReflexEvents through
// a ReflexEventAggregator. The ReflexScheduler owns activation (Activate/Deactivate)
// so no more than the configured number of skills run at once (§9.4). Concrete
// skills only implement their sensing→intent logic.
using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using MLOmega.XR.UI;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    public abstract class ReflexSkillBase : MonoBehaviour
    {
        [SerializeField] protected LocalIntentSource _intentSource;
        [SerializeField] protected ReflexConfig _config;

        /// <summary>Stable id of this skill for the scheduler + ReflexEvent.skill.</summary>
        public abstract ReflexSkillId SkillId { get; }

        /// <summary>Whether the scheduler currently has this skill active.</summary>
        public bool IsActive { get; private set; }

        protected string SessionId { get; private set; } = "local";
        protected ReflexEventAggregator Aggregator { get; private set; }

        protected virtual void Awake()
        {
            if (_intentSource == null) _intentSource = FindAnyObjectByType<LocalIntentSource>();
            if (_config == null) _config = ReflexConfig.CreateDefault();
        }

        /// <summary>Wire the shared services (session id + reflex-event sink). Called by the scheduler.</summary>
        public void Configure(string sessionId, IReflexEventSink reflexSink, ReflexConfig config)
        {
            if (!string.IsNullOrEmpty(sessionId)) SessionId = sessionId;
            if (config != null) _config = config;
            long window = _config != null ? _config.StableTrackReflexIntervalMs : 1000;
            Aggregator = new ReflexEventAggregator(reflexSink, window);
            OnConfigured();
        }

        protected virtual void OnConfigured() { }

        /// <summary>Activate the skill (scheduler-driven). Idempotent.</summary>
        public void Activate()
        {
            if (IsActive) return;
            IsActive = true;
            OnActivated();
        }

        /// <summary>Deactivate the skill (scheduler-driven). Idempotent.</summary>
        public void Deactivate()
        {
            if (!IsActive) return;
            IsActive = false;
            OnDeactivated();
        }

        protected virtual void OnActivated() { }
        protected virtual void OnDeactivated() { }

        // ------------------------------------------------------------------
        //  Emission helpers (all skills go through these)
        // ------------------------------------------------------------------

        /// <summary>Emit a UIIntent via the shared LocalIntentSource (the broker seam).</summary>
        protected void EmitIntent(UIIntent intent)
        {
            if (_intentSource == null || intent == null) return;
            _intentSource.Emit(intent);
        }

        /// <summary>Build a local UIIntent with the ultralive producer + sensible defaults.</summary>
        protected UIIntent NewIntent(string component, string uiIntentId)
        {
            return new UIIntent
            {
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = uiIntentId,
                Producer = "ultralive",
                Component = component,
                TruthLevel = "observed",
                Confidence = 1.0,
                Priority = 0.9,
                TtlMs = _config != null ? _config.LocalIntentTtlMs : 2500,
                Content = new Dictionary<string, object>(),
                Anchor = new Dictionary<string, object>(),
                UiHint = new Dictionary<string, object>(),
                EvidenceRefs = new List<string>()
            };
        }

        /// <summary>Record an aggregated ReflexEvent (never one per frame — §8.3).</summary>
        protected void RecordReflex(string aggregateKey, Dictionary<string, object> prediction,
            long horizonMs, double confidence, string severity, long nowMs,
            string sourceFrameId = null, IList<string> evidenceRefs = null)
        {
            Aggregator?.Observe(SessionId, sourceFrameId, SkillName(SkillId), aggregateKey,
                prediction, horizonMs, confidence, severity, evidenceRefs, nowMs);
        }

        /// <summary>Force-flush one aggregate at a natural boundary (e.g. a
        /// finalised segment): exactly one aggregated event per closed turn.</summary>
        protected void FlushReflex(string aggregateKey, long nowMs, long horizonMs = 0)
        {
            Aggregator?.FlushNow(SessionId, SkillName(SkillId), aggregateKey, horizonMs, nowMs);
        }

        protected static long NowMs() => (long)(Time.unscaledTimeAsDouble * 1000.0);

        internal static string SkillName(ReflexSkillId id) => id switch
        {
            ReflexSkillId.StableTrack => "stable_track",
            ReflexSkillId.LensWindow => "lens_window",
            ReflexSkillId.MotionProximity => "motion_proximity",
            ReflexSkillId.FocusSearch => "focus_search",
            ReflexSkillId.Subtitle => "subtitle",
            _ => "reflex"
        };
    }
}
