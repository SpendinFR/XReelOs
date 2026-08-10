// MLOmega V19 — E25
// TTLs, thresholds and density rules for the SceneCache and UIIntentBroker,
// exactly per GUIDE_V19_REFERENCE §9.1 (sub-caches / TTL), §13.2 (render
// priority + density) and §17.2 (truth thresholds). Authored as a
// ScriptableObject so nothing is hard-coded: a build can be retuned for a
// device profile without recompiling (menu "MLOmega/Config/Scene Cache Config").
using UnityEngine;

namespace MLOmega.XR.Scene
{
    /// <summary>
    /// Central tuning asset for the live UI runtime. Every TTL and threshold the
    /// SceneCache / broker use comes from here; there are no magic numbers in the
    /// logic. Defaults reflect the reference's qualitative rules ("très court",
    /// "expire au changement de tour", "TTL obligatoire") turned into millisecond
    /// budgets, and are all documented in DECISIONS.md §E25.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SceneCacheConfig",
        menuName = "MLOmega/Config/Scene Cache Config",
        order = 1)]
    public sealed class SceneCacheConfig : ScriptableObject
    {
        [Header("tracks sub-cache (§9.1: très court ; disparaît avec l'ancre)")]
        [Tooltip("A track is dropped this many ms after its last observation. Very short.")]
        [Min(1)]
        [SerializeField] private long _trackTtlMs = 600;

        [Header("entities_hot (§9.1: réconcilié via SceneDelta)")]
        [Tooltip("A hot entity that stops being reconciled expires after this many ms.")]
        [Min(1)]
        [SerializeField] private long _entityHotTtlMs = 8000;

        [Tooltip("§17.2/§9.1: below this identity confidence, no name is shown for the entity.")]
        [Range(0f, 1f)]
        [SerializeField] private float _identityNameConfidenceThreshold = 0.62f;

        [Header("spatial_hot (§9.1: pas de flèche si qualité sous seuil)")]
        [Tooltip("Session-scoped bearing/last_seen entry lifetime in ms.")]
        [Min(1)]
        [SerializeField] private long _spatialHotTtlMs = 20000;

        [Tooltip("§13/§17.2: below this map_quality, no precise spatial arrow is drawn.")]
        [Range(0f, 1f)]
        [SerializeField] private float _mapQualityArrowThreshold = 0.55f;

        [Header("task_hot (§9.1: une tâche active à la fois en UI)")]
        [Tooltip("Active task entry lifetime in ms.")]
        [Min(1)]
        [SerializeField] private long _taskHotTtlMs = 30000;

        [Header("translation_hot (§9.1: expire au changement de tour ou délai)")]
        [Tooltip("A partial/final subtitle line expires this many ms after its last update.")]
        [Min(1)]
        [SerializeField] private long _translationHotTtlMs = 4000;

        [Header("ui_state (§9.1: TTL obligatoire ; reset session)")]
        [Tooltip("Default lifetime for a visible-intent record when the intent carries no ttl_ms.")]
        [Min(1)]
        [SerializeField] private long _uiStateDefaultTtlMs = 5000;

        [Header("Broker density (§13.2)")]
        [Tooltip("Max simultaneous non-status intents rendered at once (status/privacy is never counted or capped).")]
        [Min(1)]
        [SerializeField] private int _maxSimultaneousIntents = 4;

        [Tooltip("FreeGuy-only cap for compact world labels/navigation. Normal/minimal modes keep the product cap above.")]
        [Min(1)]
        [SerializeField] private int _freeGuyMaxSimultaneousIntents = 12;

        [Tooltip("Fade duration applied when an intent loses its track / TTL / confidence before it disappears.")]
        [Min(0)]
        [SerializeField] private long _fadeOutMs = 180;

        public long TrackTtlMs => _trackTtlMs;
        public long EntityHotTtlMs => _entityHotTtlMs;
        public float IdentityNameConfidenceThreshold => _identityNameConfidenceThreshold;
        public long SpatialHotTtlMs => _spatialHotTtlMs;
        public float MapQualityArrowThreshold => _mapQualityArrowThreshold;
        public long TaskHotTtlMs => _taskHotTtlMs;
        public long TranslationHotTtlMs => _translationHotTtlMs;
        public long UiStateDefaultTtlMs => _uiStateDefaultTtlMs;
        public int MaxSimultaneousIntents => _maxSimultaneousIntents;
        public int FreeGuyMaxSimultaneousIntents =>
            Mathf.Max(_maxSimultaneousIntents, _freeGuyMaxSimultaneousIntents);
        public long FadeOutMs => _fadeOutMs;

        /// <summary>
        /// Build a config with the documented defaults at runtime (used by the
        /// scene builder and by EditMode tests, which cannot load a .asset).
        /// </summary>
        public static SceneCacheConfig CreateDefault()
        {
            return CreateInstance<SceneCacheConfig>();
        }
    }
}
