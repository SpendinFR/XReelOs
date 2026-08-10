// MLOmega V19 — E26
// Tuning for the Ultra-Live reflex layer: the scheduler budget, the signal→skill
// activation, and the thresholds every skill reads. Authored as a ScriptableObject
// so nothing is hard-coded (menu "MLOmega/Config/Reflex Config"); the defaults
// mirror GUIDE_V19_REFERENCE §9.3/§9.4 and are documented in DECISIONS.md §E26.
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    [CreateAssetMenu(
        fileName = "ReflexConfig",
        menuName = "MLOmega/Config/Reflex Config",
        order = 2)]
    public sealed class ReflexConfig : ScriptableObject
    {
        [Header("Scheduler budget (§9.4: jamais tous les détecteurs en parallèle)")]
        [Tooltip("Max skills that may be active simultaneously. The scheduler drops " +
                 "the lowest-priority active skill when a higher-priority signal needs a slot.")]
        [Min(1)]
        [SerializeField] private int _maxSimultaneousSkills = 3;

        [Tooltip("A skill activated by a signal stays warm this many ms after the " +
                 "signal last fired, to avoid flapping on/off around a threshold.")]
        [Min(0)]
        [SerializeField] private long _skillLingerMs = 1500;

        [Header("MotionProximity (§9.2/§14.10)")]
        [Tooltip("Sub-sampled frame-difference growth (0..1) above which a peripheral cue is raised.")]
        [Range(0f, 1f)]
        [SerializeField] private float _motionGrowthCue = 0.18f;

        [Tooltip("Growth (0..1) above which the cue severity is escalated to critical.")]
        [Range(0f, 1f)]
        [SerializeField] private float _motionGrowthCritical = 0.42f;

        [Tooltip("Minimum ms between two MotionProximity cues (aggregation, not per-frame).")]
        [Min(0)]
        [SerializeField] private long _motionCueMinIntervalMs = 700;

        [Header("FocusSearch (§9.2/§14.6)")]
        [Tooltip("Seconds a discreet spinner is shown while waiting on a VisionRT reply before honest-miss.")]
        [Min(0.5f)]
        [SerializeField] private float _focusSearchTimeoutSeconds = 3f;

        [Header("StableTrack / LensWindow")]
        [Tooltip("A stable-track outline is refreshed at most this often (ms) — one aggregated ReflexEvent per burst.")]
        [Min(0)]
        [SerializeField] private long _stableTrackReflexIntervalMs = 1000;

        [Tooltip("Default TTL (ms) stamped on locally produced UIIntents when the skill has no better value.")]
        [Min(100)]
        [SerializeField] private long _localIntentTtlMs = 2500;

        public int MaxSimultaneousSkills => _maxSimultaneousSkills;
        public long SkillLingerMs => _skillLingerMs;
        public float MotionGrowthCue => _motionGrowthCue;
        public float MotionGrowthCritical => _motionGrowthCritical;
        public long MotionCueMinIntervalMs => _motionCueMinIntervalMs;
        public float FocusSearchTimeoutSeconds => _focusSearchTimeoutSeconds;
        public long StableTrackReflexIntervalMs => _stableTrackReflexIntervalMs;
        public long LocalIntentTtlMs => _localIntentTtlMs;

        /// <summary>Runtime-default instance for the scene builder and EditMode tests (cannot load a .asset).</summary>
        public static ReflexConfig CreateDefault() => CreateInstance<ReflexConfig>();
    }
}
