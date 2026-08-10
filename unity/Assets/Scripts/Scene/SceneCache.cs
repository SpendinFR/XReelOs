// MLOmega V19 â€” E25
// SceneCache: the live, short-lived scene state the UI runtime renders against.
// Six sub-caches with the exact TTL rules of GUIDE_V19_REFERENCE Â§9.1:
//   tracks           â€” very short; disappears with the anchor
//   entities_hot     â€” reconciled via SceneDelta; no name below identity confidence (Â§17.2)
//   spatial_hot      â€” no precise arrow below map_quality threshold
//   task_hot         â€” a single active task in UI at a time
//   translation_hot  â€” expires on turn change or excessive delay
//   ui_state         â€” mandatory TTL; reset on session
//
// Threading model: writers (DataChannel SceneDelta from E24, local tracks, live
// UIIntents) may arrive on any thread, so they enqueue onto a lock-free-ish
// ConcurrentQueue. Update() (Unity main thread) drains that queue and then ages
// out every sub-cache. All reads on the public API are main-thread only, matching
// how UIRuntime consumes it. TTLs/thresholds come from SceneCacheConfig (no magic
// numbers in the logic).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using UnityEngine;

namespace MLOmega.XR.Scene
{
    /// <summary>
    /// Live scene state for the renderer. Thread-safe ingress via a concurrent
    /// queue drained on the Unity main thread in <see cref="Update"/>.
    /// </summary>
    public sealed class SceneCache : MonoBehaviour
    {
        [SerializeField] private SceneCacheConfig _config;

        private readonly ConcurrentQueue<Action<SceneCache>> _ingress =
            new ConcurrentQueue<Action<SceneCache>>();

        private readonly TrackSubCache _tracks = new TrackSubCache();
        private readonly EntitiesHotSubCache _entitiesHot = new EntitiesHotSubCache();
        private readonly SpatialHotSubCache _spatialHot = new SpatialHotSubCache();
        private readonly TaskHotSubCache _taskHot = new TaskHotSubCache();
        private readonly TranslationHotSubCache _translationHot = new TranslationHotSubCache();
        private readonly UiStateSubCache _uiState = new UiStateSubCache();

        /// <summary>Monotonic "now" in ms, driven by <see cref="Update"/> and overridable in tests.</summary>
        public long NowMs { get; private set; }

        public SceneCacheConfig Config => _config;
        public TrackSubCache Tracks => _tracks;
        public EntitiesHotSubCache EntitiesHot => _entitiesHot;
        public SpatialHotSubCache SpatialHot => _spatialHot;
        public TaskHotSubCache TaskHot => _taskHot;
        public TranslationHotSubCache TranslationHot => _translationHot;
        public UiStateSubCache UiState => _uiState;

        private void Awake()
        {
            if (_config == null)
            {
                _config = SceneCacheConfig.CreateDefault();
                Debug.LogWarning("[SceneCache] no SceneCacheConfig assigned; using runtime defaults.");
            }
        }

        private void Update()
        {
            // Drive the clock from unscaled real time (ms).
            Tick((long)(Time.unscaledTimeAsDouble * 1000.0));
        }

        /// <summary>
        /// Advance the cache: drain the ingress queue, then age out every
        /// sub-cache against <paramref name="nowMs"/>. Exposed so EditMode tests
        /// can drive time deterministically without a running player loop.
        /// </summary>
        public void Tick(long nowMs)
        {
            NowMs = nowMs;
            while (_ingress.TryDequeue(out Action<SceneCache> work))
            {
                try { work(this); }
                catch (Exception ex) { Debug.LogError($"[SceneCache] ingress apply failed: {ex}"); }
            }
            SceneCacheConfig c = _config;
            _tracks.AgeOut(nowMs, c.TrackTtlMs);
            _entitiesHot.AgeOut(nowMs, c.EntityHotTtlMs);
            _spatialHot.AgeOut(nowMs, c.SpatialHotTtlMs);
            _taskHot.AgeOut(nowMs, c.TaskHotTtlMs);
            _translationHot.AgeOut(nowMs, c.TranslationHotTtlMs);
            _uiState.AgeOut(nowMs);
        }

        // --- thread-safe ingress --------------------------------------------------

        /// <summary>
        /// Apply a full <see cref="SceneDelta"/> (from the DataChannel, E24):
        /// reconcile entities_hot, refresh spatial_hot bearings, note map_quality.
        /// Safe to call from any thread.
        /// </summary>
        public void SubmitSceneDelta(SceneDelta delta)
        {
            if (delta == null) return;
            _ingress.Enqueue(self => self.ApplySceneDelta(delta));
        }

        /// <summary>
        /// Apply an <c>entity_hot_update</c> (E34 Â§5): a prefetched relation pack
        /// for a just-identified person, pushed by the PC scene adapter the moment
        /// identity_fusion names someone. The device folds it into entities_hot so
        /// the ContextCard renders from the local cache with zero round-trip.
        /// Safe to call from any thread.
        /// </summary>
        public void SubmitEntityHotUpdate(EntityHotUpdate update)
        {
            if (update == null || string.IsNullOrEmpty(update.EntityId)) return;
            _ingress.Enqueue(self => self._entitiesHot.ApplyHotUpdate(update, self.NowMs));
        }

        /// <summary>
        /// Apply a <c>spatial_hot_update</c> (E35 Â§4a): a recognised session zone +
        /// map_quality + matching daily routines pushed by the PC scene adapter.
        /// Additive to the SceneDelta-driven spatial_hot; folds the zone/routines so
        /// a "ici, d'habitude tuâ€¦" card can render from the local cache. Any thread.
        /// </summary>
        public void SubmitSpatialHotUpdate(SpatialHotUpdate update)
        {
            if (update == null || string.IsNullOrEmpty(update.Zone)) return;
            _ingress.Enqueue(self => self._spatialHot.ApplyHotUpdate(update, self.NowMs));
        }

        /// <summary>
        /// Apply a <c>task_hot_update</c> (E35 Â§4c): the active task/situation the PC
        /// scene adapter reports (goal/step/tools) â†’ the single task_hot slot. Any
        /// thread.
        /// </summary>
        public void SubmitTaskHotUpdate(TaskHotUpdate update)
        {
            if (update == null || string.IsNullOrEmpty(update.TaskKey)) return;
            _ingress.Enqueue(self => self._taskHot.ApplyHotUpdate(update, self.NowMs));
        }

        /// <summary>Ingest/refresh a local track (from the device optical-flow path). Any thread.</summary>
        public void SubmitLocalTrack(LocalTrack track)
        {
            if (track == null || string.IsNullOrEmpty(track.TrackId)) return;
            _ingress.Enqueue(self => self._tracks.Upsert(track, self.NowMs));
        }

        /// <summary>Record a live translation line (partial/final). Any thread.</summary>
        public void SubmitTranslation(string speakerTrackId, string text, bool isFinal, string language)
        {
            _ingress.Enqueue(self =>
                self._translationHot.Set(speakerTrackId, text, isFinal, language, null, null, self.NowMs));
        }

        /// <summary>
        /// E48-A: record a live translation line WITH its on-device translation
        /// (the offline reflex translated a FINAL segment). <paramref name="translation"/>
        /// is the translated text, <paramref name="translationLang"/> its language;
        /// both null falls back to the original-only line above. Any thread.
        /// </summary>
        public void SubmitTranslation(string speakerTrackId, string text, bool isFinal,
            string language, string translation, string translationLang)
        {
            _ingress.Enqueue(self =>
                self._translationHot.Set(speakerTrackId, text, isFinal, language,
                    translation, translationLang, self.NowMs));
        }

        /// <summary>Set the single active UI task (Â§9.1: one task_hot at a time). Any thread.</summary>
        public void SubmitActiveTask(string taskId, string goal, string step)
        {
            _ingress.Enqueue(self => self._taskHot.SetActive(taskId, goal, step, self.NowMs));
        }

        /// <summary>Record that an intent is currently visible in ui_state. Any thread.</summary>
        public void SubmitVisibleIntent(string uiIntentId, long ttlMs)
        {
            if (string.IsNullOrEmpty(uiIntentId)) return;
            _ingress.Enqueue(self =>
            {
                long ttl = ttlMs > 0 ? ttlMs : self._config.UiStateDefaultTtlMs;
                self._uiState.MarkVisible(uiIntentId, self.NowMs, ttl);
            });
        }

        /// <summary>Record that an intent was suppressed/dismissed by the user. Any thread.</summary>
        public void SubmitSuppressed(string uiIntentId)
        {
            if (string.IsNullOrEmpty(uiIntentId)) return;
            _ingress.Enqueue(self => self._uiState.MarkSuppressed(uiIntentId, self.NowMs));
        }

        /// <summary>Clear everything (session reset â€” Â§9.1 ui_state "reset session possible").</summary>
        public void ResetSession()
        {
            _ingress.Enqueue(self =>
            {
                self._tracks.Clear();
                self._entitiesHot.Clear();
                self._spatialHot.Clear();
                self._taskHot.Clear();
                self._translationHot.Clear();
                self._uiState.Clear();
            });
        }

        private void ApplySceneDelta(SceneDelta delta)
        {
            if (delta.Entities != null)
            {
                foreach (Dictionary<string, object> e in delta.Entities)
                {
                    _entitiesHot.Reconcile(e, NowMs);
                }
            }
            // spatial: bearings + last_seen carried on changes/relations; map_quality is delta-level.
            _spatialHot.NoteMapQuality(delta.MapQuality, NowMs);
            if (delta.Changes != null)
            {
                foreach (Dictionary<string, object> ch in delta.Changes)
                {
                    _spatialHot.NoteChange(ch, delta.MapQuality, NowMs);
                }
            }
        }

        // ======================================================================
        //  Sub-caches
        // ======================================================================

        /// <summary>tracks â€” bbox/masks, velocities, visibility, age. Very short TTL.</summary>
        public sealed class TrackSubCache
        {
            private readonly Dictionary<string, TrackEntry> _byId = new Dictionary<string, TrackEntry>();

            public int Count => _byId.Count;

            public void Upsert(LocalTrack track, long nowMs)
            {
                _byId[track.TrackId] = new TrackEntry(track, nowMs);
            }

            public bool Contains(string trackId) =>
                !string.IsNullOrEmpty(trackId) && _byId.ContainsKey(trackId);

            public bool TryGet(string trackId, out TrackEntry entry) =>
                _byId.TryGetValue(trackId ?? string.Empty, out entry);

            public IReadOnlyCollection<TrackEntry> All => _byId.Values;

            public void AgeOut(long nowMs, long ttlMs)
            {
                if (_byId.Count == 0) return;
                List<string> dead = null;
                foreach (KeyValuePair<string, TrackEntry> kv in _byId)
                {
                    if (nowMs - kv.Value.LastSeenMs > ttlMs)
                    {
                        (dead ??= new List<string>()).Add(kv.Key);
                    }
                }
                if (dead != null)
                {
                    foreach (string id in dead) _byId.Remove(id);
                }
            }

            public void Clear() => _byId.Clear();
        }

        public readonly struct TrackEntry
        {
            public readonly LocalTrack Track;
            public readonly long LastSeenMs;
            public TrackEntry(LocalTrack track, long lastSeenMs)
            {
                Track = track;
                LastSeenMs = lastSeenMs;
            }
        }

        /// <summary>entities_hot â€” reconciled via SceneDelta; no name below identity confidence.</summary>
        public sealed class EntitiesHotSubCache
        {
            private readonly Dictionary<string, EntityHot> _byId = new Dictionary<string, EntityHot>();
            // E34 Â§5: prefetched relation pack (name + last topics / promises) per
            // entity, delivered ahead of the ContextCard so it renders locally.
            private readonly Dictionary<string, EntityHotUpdate> _relationPacks =
                new Dictionary<string, EntityHotUpdate>();

            public int Count => _byId.Count;
            public IReadOnlyCollection<EntityHot> All => _byId.Values;

            public bool TryGet(string entityId, out EntityHot entity) =>
                _byId.TryGetValue(entityId ?? string.Empty, out entity);

            /// <summary>The prefetched relation pack for an entity, if one arrived.</summary>
            public bool TryGetRelationPack(string entityId, out EntityHotUpdate pack) =>
                _relationPacks.TryGetValue(entityId ?? string.Empty, out pack);

            public void Reconcile(Dictionary<string, object> e, long nowMs)
            {
                if (e == null) return;
                string id = AsString(e, "entity_id");
                if (string.IsNullOrEmpty(id)) return;
                string label = AsString(e, "label") ?? AsString(e, "name");
                string kind = AsString(e, "kind") ?? AsString(e, "type");
                string trackId = AsString(e, "track_id") ?? AsString(e, "last_track");
                double confidence = AsDouble(e, "confidence", 0.0);
                _byId[id] = new EntityHot(id, label, kind, trackId, confidence, nowMs);
            }

            /// <summary>
            /// Fold a prefetched relation pack into entities_hot (E34 Â§5). Additive:
            /// it seeds/refreshes the entity's name + person id and stores the pack.
            /// </summary>
            public void ApplyHotUpdate(EntityHotUpdate update, long nowMs)
            {
                if (update == null || string.IsNullOrEmpty(update.EntityId)) return;
                _relationPacks[update.EntityId] = update;
                // E35 Â§4b: a durable object carries a Label (+ kind=object); a person
                // carries a Name. Use whichever is present so both fold into the same
                // entities_hot store additively.
                bool isObject = update.IsObject;
                string label = isObject ? (update.Label ?? update.Name) : (update.Name ?? update.Label);
                string kind = isObject ? "object" : "person";
                if (_byId.TryGetValue(update.EntityId, out EntityHot existing))
                {
                    // Keep the reconciled confidence/track; refresh label + kind.
                    _byId[update.EntityId] = new EntityHot(
                        update.EntityId, label ?? existing.Label,
                        isObject ? "object" : existing.Kind,
                        existing.TrackId, existing.Confidence, nowMs);
                }
                else
                {
                    double conf = isObject && update.Confidence > 0 ? update.Confidence : 1.0;
                    _byId[update.EntityId] = new EntityHot(
                        update.EntityId, label, kind, null, conf, nowMs);
                }
            }

            public void AgeOut(long nowMs, long ttlMs)
            {
                if (_byId.Count == 0) return;
                List<string> dead = null;
                foreach (KeyValuePair<string, EntityHot> kv in _byId)
                {
                    if (nowMs - kv.Value.LastReconciledMs > ttlMs)
                    {
                        (dead ??= new List<string>()).Add(kv.Key);
                    }
                }
                if (dead != null)
                {
                    foreach (string id in dead)
                    {
                        _byId.Remove(id);
                        _relationPacks.Remove(id);
                    }
                }
            }

            public void Clear()
            {
                _byId.Clear();
                _relationPacks.Clear();
            }
        }

        public readonly struct EntityHot
        {
            public readonly string EntityId;
            public readonly string Label;
            public readonly string Kind;
            public readonly string TrackId;
            public readonly double Confidence;
            public readonly long LastReconciledMs;

            public EntityHot(string entityId, string label, string kind, string trackId,
                double confidence, long lastReconciledMs)
            {
                EntityId = entityId;
                Label = label;
                Kind = kind;
                TrackId = trackId;
                Confidence = confidence;
                LastReconciledMs = lastReconciledMs;
            }

            /// <summary>
            /// Â§17.2 / Â§9.1: a name/person-tag is only allowed when identity
            /// confidence clears the configured threshold. Otherwise the entity is
            /// shown without a name.
            /// </summary>
            public bool NameAllowed(float identityThreshold) => Confidence >= identityThreshold;
        }

        /// <summary>spatial_hot â€” bearing/last_seen (session), map quality. No arrow below threshold.</summary>
        public sealed class SpatialHotSubCache
        {
            private readonly Dictionary<string, SpatialHotEntry> _byEntity = new Dictionary<string, SpatialHotEntry>();
            private double _lastMapQuality;
            private long _lastMapQualityMs;
            // E35 Â§4a: the recognised session zone + its matching daily routines,
            // pushed by the PC scene adapter (additive to the delta-driven bearings).
            private SpatialHotUpdate _zonePack;
            private long _zonePackMs;

            public double MapQuality => _lastMapQuality;
            public int Count => _byEntity.Count;
            public IReadOnlyCollection<SpatialHotEntry> All => _byEntity.Values;

            /// <summary>The active zone pack (zone id + routines), if one arrived (E35 Â§4a).</summary>
            public SpatialHotUpdate ZonePack => _zonePack;
            public string ActiveZone => _zonePack?.Zone;

            public void NoteMapQuality(double mapQuality, long nowMs)
            {
                _lastMapQuality = mapQuality;
                _lastMapQualityMs = nowMs;
            }

            /// <summary>Fold a PC-pushed zone pack (E35 Â§4a). Refreshes map_quality
            /// from the measured value carried by the update.</summary>
            public void ApplyHotUpdate(SpatialHotUpdate update, long nowMs)
            {
                if (update == null || string.IsNullOrEmpty(update.Zone)) return;
                _zonePack = update;
                _zonePackMs = nowMs;
                if (update.MapQuality > 0)
                {
                    _lastMapQuality = update.MapQuality;
                    _lastMapQualityMs = nowMs;
                }
            }

            public void NoteChange(Dictionary<string, object> change, double mapQuality, long nowMs)
            {
                if (change == null) return;
                string entityId = AsString(change, "entity_id");
                if (string.IsNullOrEmpty(entityId)) return;
                double bearing = AsDouble(change, "bearing_deg", double.NaN);
                _byEntity[entityId] = new SpatialHotEntry(entityId, bearing, mapQuality, nowMs);
            }

            public bool TryGet(string entityId, out SpatialHotEntry spatial) =>
                _byEntity.TryGetValue(entityId ?? string.Empty, out spatial);

            /// <summary>Â§13/Â§17.2: precise arrows only when map_quality clears the threshold.</summary>
            public bool ArrowAllowed(float mapQualityThreshold) => _lastMapQuality >= mapQualityThreshold;

            public void AgeOut(long nowMs, long ttlMs)
            {
                if (_byEntity.Count > 0)
                {
                    List<string> dead = null;
                    foreach (KeyValuePair<string, SpatialHotEntry> kv in _byEntity)
                    {
                        if (nowMs - kv.Value.LastSeenMs > ttlMs)
                        {
                            (dead ??= new List<string>()).Add(kv.Key);
                        }
                    }
                    if (dead != null)
                    {
                        foreach (string id in dead) _byEntity.Remove(id);
                    }
                }
                if (_lastMapQualityMs != 0 && nowMs - _lastMapQualityMs > ttlMs)
                {
                    _lastMapQuality = 0.0;
                }
                if (_zonePack != null && nowMs - _zonePackMs > ttlMs)
                {
                    _zonePack = null;
                }
            }

            public void Clear()
            {
                _byEntity.Clear();
                _lastMapQuality = 0.0;
                _lastMapQualityMs = 0;
                _zonePack = null;
                _zonePackMs = 0;
            }
        }

        public readonly struct SpatialHotEntry
        {
            public readonly string EntityId;
            public readonly double BearingDeg;
            public readonly double MapQuality;
            public readonly long LastSeenMs;

            public SpatialHotEntry(string entityId, double bearingDeg, double mapQuality, long lastSeenMs)
            {
                EntityId = entityId;
                BearingDeg = bearingDeg;
                MapQuality = mapQuality;
                LastSeenMs = lastSeenMs;
            }

            public bool HasBearing => !double.IsNaN(BearingDeg);
        }

        /// <summary>task_hot â€” one active task in UI at a time (Â§9.1).</summary>
        public sealed class TaskHotSubCache
        {
            private TaskHotEntry? _active;

            public bool HasActive => _active.HasValue;
            public TaskHotEntry? Active => _active;

            /// <summary>
            /// Setting a task replaces any previous one: the reference is explicit
            /// that only a single task_hot is ever active in the UI.
            /// </summary>
            public void SetActive(string taskId, string goal, string step, long nowMs)
            {
                if (string.IsNullOrEmpty(taskId))
                {
                    _active = null;
                    return;
                }
                _active = new TaskHotEntry(taskId, goal, step, nowMs);
            }

            /// <summary>Fold a PC-pushed task hot update (E35 Â§4c) into the single
            /// active task slot (task_key â†’ id, goal/step carried through).</summary>
            public void ApplyHotUpdate(TaskHotUpdate update, long nowMs)
            {
                if (update == null || string.IsNullOrEmpty(update.TaskKey)) return;
                _active = new TaskHotEntry(update.TaskKey, update.Goal, update.Step, nowMs);
            }

            public void AgeOut(long nowMs, long ttlMs)
            {
                if (_active.HasValue && nowMs - _active.Value.UpdatedMs > ttlMs)
                {
                    _active = null;
                }
            }

            public void Clear() => _active = null;
        }

        public readonly struct TaskHotEntry
        {
            public readonly string TaskId;
            public readonly string Goal;
            public readonly string Step;
            public readonly long UpdatedMs;
            public TaskHotEntry(string taskId, string goal, string step, long updatedMs)
            {
                TaskId = taskId;
                Goal = goal;
                Step = step;
                UpdatedMs = updatedMs;
            }
        }

        /// <summary>translation_hot â€” one live line; expires on turn change / delay (Â§9.1).</summary>
        public sealed class TranslationHotSubCache
        {
            private TranslationHotEntry? _current;

            public bool HasLine => _current.HasValue;
            public TranslationHotEntry? Current => _current;

            public void Set(string speakerTrackId, string text, bool isFinal, string language,
                string translation, string translationLang, long nowMs)
            {
                // A speaker change is a turn change: the previous line is replaced.
                _current = new TranslationHotEntry(speakerTrackId, text, isFinal, language,
                    translation, translationLang, nowMs);
            }

            public void AgeOut(long nowMs, long ttlMs)
            {
                if (_current.HasValue && nowMs - _current.Value.UpdatedMs > ttlMs)
                {
                    _current = null;
                }
            }

            public void Clear() => _current = null;
        }

        public readonly struct TranslationHotEntry
        {
            public readonly string SpeakerTrackId;
            public readonly string Text;
            public readonly bool IsFinal;
            public readonly string Language;
            /// <summary>E48-A: the on-device translation of <see cref="Text"/>, or null.</summary>
            public readonly string Translation;
            /// <summary>E48-A: language of <see cref="Translation"/>, or null.</summary>
            public readonly string TranslationLanguage;
            public readonly long UpdatedMs;

            public TranslationHotEntry(string speakerTrackId, string text, bool isFinal, string language,
                string translation, string translationLang, long updatedMs)
            {
                SpeakerTrackId = speakerTrackId;
                Text = text;
                IsFinal = isFinal;
                Language = language;
                Translation = translation;
                TranslationLanguage = translationLang;
                UpdatedMs = updatedMs;
            }

            /// <summary>True when a non-empty on-device translation is attached (E48-A).</summary>
            public bool HasTranslation => !string.IsNullOrEmpty(Translation);
        }

        /// <summary>ui_state â€” visible intents, suppression, density prefs. Mandatory TTL (Â§9.1).</summary>
        public sealed class UiStateSubCache
        {
            private readonly Dictionary<string, VisibleRecord> _visible = new Dictionary<string, VisibleRecord>();
            private readonly Dictionary<string, long> _suppressed = new Dictionary<string, long>();

            public int VisibleCount => _visible.Count;
            public IReadOnlyDictionary<string, VisibleRecord> Visible => _visible;

            public void MarkVisible(string uiIntentId, long nowMs, long ttlMs)
            {
                _visible[uiIntentId] = new VisibleRecord(nowMs, ttlMs);
            }

            public bool IsVisible(string uiIntentId) =>
                !string.IsNullOrEmpty(uiIntentId) && _visible.ContainsKey(uiIntentId);

            public void MarkSuppressed(string uiIntentId, long nowMs)
            {
                _suppressed[uiIntentId] = nowMs;
                _visible.Remove(uiIntentId);
            }

            public bool IsSuppressed(string uiIntentId) =>
                !string.IsNullOrEmpty(uiIntentId) && _suppressed.ContainsKey(uiIntentId);

            public void AgeOut(long nowMs)
            {
                if (_visible.Count == 0) return;
                List<string> dead = null;
                foreach (KeyValuePair<string, VisibleRecord> kv in _visible)
                {
                    if (nowMs - kv.Value.ShownMs > kv.Value.TtlMs)
                    {
                        (dead ??= new List<string>()).Add(kv.Key);
                    }
                }
                if (dead != null)
                {
                    foreach (string id in dead) _visible.Remove(id);
                }
            }

            public void Clear()
            {
                _visible.Clear();
                _suppressed.Clear();
            }
        }

        public readonly struct VisibleRecord
        {
            public readonly long ShownMs;
            public readonly long TtlMs;
            public VisibleRecord(long shownMs, long ttlMs)
            {
                ShownMs = shownMs;
                TtlMs = ttlMs;
            }
        }

        // --- small typed readers for the loosely-typed contract dictionaries -----

        private static string AsString(Dictionary<string, object> d, string key)
        {
            if (d != null && d.TryGetValue(key, out object v) && v != null)
            {
                return v as string ?? v.ToString();
            }
            return null;
        }

        private static double AsDouble(Dictionary<string, object> d, string key, double fallback)
        {
            if (d != null && d.TryGetValue(key, out object v) && v != null)
            {
                switch (v)
                {
                    case double dv: return dv;
                    case float fv: return fv;
                    case long lv: return lv;
                    case int iv: return iv;
                    default:
                        if (double.TryParse(v.ToString(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                        {
                            return parsed;
                        }
                        break;
                }
            }
            return fallback;
        }
    }
}
