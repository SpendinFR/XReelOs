// MLOmega V19 — E25
// Reasons an intent is refused / expired / evicted, journaled as the
// `ui_intent_drop_reason` observability metric (GUIDE_V19_REFERENCE §15.3).
namespace MLOmega.XR.UI
{
    /// <summary>
    /// Every broker decision that removes or refuses an intent carries one of
    /// these. Emitted with the intent id so the drop can be correlated to its
    /// source frame / track (§16.2 "vérité visible").
    /// </summary>
    public enum UIIntentDropReason
    {
        /// <summary>Admitted for rendering (not a drop; logged for symmetry / audit).</summary>
        Admitted = 0,
        /// <summary>ttl_ms elapsed since the intent was admitted.</summary>
        TtlExpired = 1,
        /// <summary>target_track_id is no longer present in SceneCache.tracks.</summary>
        TrackLost = 2,
        /// <summary>Density cap reached; a lower-priority intent was evicted / this one refused.</summary>
        DensityCap = 3,
        /// <summary>Duplicate ui_intent_id already known.</summary>
        Duplicate = 4,
        /// <summary>User suppressed / dismissed this intent (ui_state).</summary>
        UserSuppressed = 5,
        /// <summary>Confidence fell below the level required for this component.</summary>
        LowConfidence = 6
    }
}
