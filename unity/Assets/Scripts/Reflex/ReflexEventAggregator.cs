// MLOmega V19 — E26
// Aggregates ReflexEvents so a skill never emits one event per frame (handoff
// §8.3 "Cue UL0 non critique → agrégat compteur/latence, pas de clip"; contract
// ReflexEvent.aggregate_key). Detections sharing an aggregate_key within a window
// collapse into a single ReflexEvent carrying a count; the event is flushed when
// the window elapses or the severity escalates. This is the reflex-audit / metrics
// path (§15.3), separate from the UIIntent shown to the user.
using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;

namespace MLOmega.XR.Reflex
{
    /// <summary>Where aggregated ReflexEvents are delivered (transport DataChannel, or a test sink).</summary>
    public interface IReflexEventSink
    {
        void Send(ReflexEvent evt);
    }

    /// <summary>
    /// Per-aggregate_key accumulator. Call <see cref="Observe"/> for each detection;
    /// it emits an aggregated ReflexEvent at most once per <see cref="_windowMs"/>
    /// (or immediately when severity rises to critical). Deterministic:
    /// <see cref="Observe"/> takes the current time so tests drive it.
    /// </summary>
    public sealed class ReflexEventAggregator
    {
        private readonly IReflexEventSink _sink;
        private readonly long _windowMs;
        private readonly Dictionary<string, Bucket> _buckets = new Dictionary<string, Bucket>();

        public ReflexEventAggregator(IReflexEventSink sink, long windowMs)
        {
            _sink = sink;
            _windowMs = windowMs;
        }

        /// <summary>
        /// Record one detection under <paramref name="aggregateKey"/>. Returns the
        /// emitted event if this observation flushed the bucket, else null.
        /// </summary>
        public ReflexEvent Observe(
            string sessionId, string sourceFrameId, string skill, string aggregateKey,
            Dictionary<string, object> prediction, long horizonMs, double confidence,
            string severity, IList<string> evidenceRefs, long nowMs)
        {
            if (!_buckets.TryGetValue(aggregateKey, out Bucket b))
            {
                b = new Bucket { FirstMs = nowMs };
                _buckets[aggregateKey] = b;
            }
            b.Count++;
            b.LastConfidence = confidence;
            b.LastSeverity = severity;
            b.LastPrediction = prediction;
            b.LastFrameId = sourceFrameId;
            b.LastEvidence = evidenceRefs;

            bool escalated = severity == "critical" && !b.EmittedCritical;
            bool windowElapsed = nowMs - b.FirstMs >= _windowMs;
            if (escalated || windowElapsed)
            {
                if (escalated) b.EmittedCritical = true;
                ReflexEvent evt = Flush(sessionId, skill, aggregateKey, horizonMs, b, nowMs);
                _sink?.Send(evt);
                _buckets.Remove(aggregateKey);
                return evt;
            }
            return null;
        }

        /// <summary>
        /// Force-flush one aggregate bucket at a natural boundary (e.g. a finalised
        /// subtitle segment closes its turn). Returns the emitted event, or null if
        /// nothing was pending under <paramref name="aggregateKey"/>.
        /// </summary>
        public ReflexEvent FlushNow(string sessionId, string skill, string aggregateKey,
            long horizonMs, long nowMs)
        {
            if (!_buckets.TryGetValue(aggregateKey, out Bucket b)) return null;
            ReflexEvent evt = Flush(sessionId, skill, aggregateKey, horizonMs, b, nowMs);
            _sink?.Send(evt);
            _buckets.Remove(aggregateKey);
            return evt;
        }

        private static ReflexEvent Flush(string sessionId, string skill, string aggregateKey,
            long horizonMs, Bucket b, long nowMs)
        {
            var prediction = b.LastPrediction != null
                ? new Dictionary<string, object>(b.LastPrediction)
                : new Dictionary<string, object>();
            prediction["count"] = b.Count;
            prediction["window_ms"] = nowMs - b.FirstMs;
            return new ReflexEvent
            {
                ContractsVersion = ContractDefaults.Version,
                SessionId = sessionId,
                SourceFrameId = b.LastFrameId,
                Skill = skill,
                Prediction = prediction,
                HorizonMs = horizonMs,
                Confidence = b.LastConfidence,
                Severity = b.LastSeverity,
                EvidenceRefs = b.LastEvidence != null ? new List<string>(b.LastEvidence) : new List<string>(),
                AggregateKey = aggregateKey
            };
        }

        private sealed class Bucket
        {
            public long FirstMs;
            public int Count;
            public double LastConfidence;
            public string LastSeverity;
            public string LastFrameId;
            public Dictionary<string, object> LastPrediction;
            public IList<string> LastEvidence;
            public bool EmittedCritical;
        }
    }
}
