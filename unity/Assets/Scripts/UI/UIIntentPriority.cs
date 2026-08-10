// MLOmega V19 — E25
// The exact render-priority ladder of GUIDE_V19_REFERENCE §13.2, turned into an
// ordered enum + a pure classifier. Lower numeric value = higher priority (rung 1
// wins). The classifier maps a UIIntent to its rung from (component, producer,
// ui_hint) — never from the PC dictating a rank, only from the contract's
// semantics, matching the reference's "après opt-out utilisateur" ladder.
using System.Collections.Generic;
using MLOmega.Contracts.V19;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// §13.2 render-priority rungs. Ordered so a numeric comparison sorts intents
    /// correctly (rung 1 = <see cref="StatusPrivacy"/> is the strongest).
    /// </summary>
    public enum RenderPriority
    {
        // 1. status/privacy/pause
        StatusPrivacy = 1,
        // 2. UltraLive critical or explicit focus
        UltraLiveCriticalOrFocus = 2,
        // 3. subtitle / active translation
        SubtitleTranslation = 3,
        // 4. requested VisionRT result
        VisionRtRequested = 4,
        // 5. current task
        Task = 5,
        // 6. contextualized BrainLive
        BrainLive = 6,
        // 7. Free Guy / decorative
        Decorative = 7
    }

    /// <summary>
    /// Pure classifier: given a <see cref="UIIntent"/>, return its §13.2 rung.
    /// Kept static and side-effect-free so it is trivially unit-testable and the
    /// broker's ordering is auditable.
    /// </summary>
    public static class UIIntentPriority
    {
        // Components that are, by nature, the permanent head-locked status surface.
        private static readonly HashSet<string> StatusComponents = new HashSet<string>
        {
            "status_bar", "statusbar", "privacy", "pause"
        };

        // Components that are subtitles / live translation.
        private static readonly HashSet<string> SubtitleComponents = new HashSet<string>
        {
            "subtitle", "translation"
        };

        // Components that represent the current task.
        private static readonly HashSet<string> TaskComponents = new HashSet<string>
        {
            "task_card", "taskcard"
        };

        public static RenderPriority Classify(UIIntent intent)
        {
            if (intent == null) return RenderPriority.Decorative;

            string component = Norm(intent.Component);
            string producer = Norm(intent.Producer);

            // 1. status / privacy / pause — always top, regardless of producer.
            if (StatusComponents.Contains(component))
            {
                return RenderPriority.StatusPrivacy;
            }

            // 2. UltraLive critical OR explicit focus.
            if (producer == "ultralive")
            {
                if (IsCritical(intent) || IsFocus(intent))
                {
                    return RenderPriority.UltraLiveCriticalOrFocus;
                }
            }
            if (IsFocus(intent))
            {
                // Explicit focus request from any producer (e.g. LensWindow focus).
                return RenderPriority.UltraLiveCriticalOrFocus;
            }

            // 3. subtitle / active translation.
            if (SubtitleComponents.Contains(component))
            {
                return RenderPriority.SubtitleTranslation;
            }

            // 4. requested VisionRT result.
            if (producer == "visionrt")
            {
                return RenderPriority.VisionRtRequested;
            }

            // 5. current task.
            if (TaskComponents.Contains(component))
            {
                return RenderPriority.Task;
            }

            // 6. contextualized BrainLive.
            if (producer == "brainlive")
            {
                return RenderPriority.BrainLive;
            }

            // 7. Free Guy / decorative — everything else.
            return RenderPriority.Decorative;
        }

        private static bool IsCritical(UIIntent intent)
        {
            return HintFlag(intent, "critical") || Norm(HintString(intent, "severity")) == "critical";
        }

        private static bool IsFocus(UIIntent intent)
        {
            return HintFlag(intent, "focus") || Norm(HintString(intent, "reason")) == "focus";
        }

        private static bool HintFlag(UIIntent intent, string key)
        {
            if (intent.UiHint != null && intent.UiHint.TryGetValue(key, out object v) && v != null)
            {
                if (v is bool b) return b;
                if (bool.TryParse(v.ToString(), out bool parsed)) return parsed;
            }
            return false;
        }

        private static string HintString(UIIntent intent, string key)
        {
            if (intent.UiHint != null && intent.UiHint.TryGetValue(key, out object v) && v != null)
            {
                return v as string ?? v.ToString();
            }
            return null;
        }

        private static string Norm(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Trim().ToLowerInvariant();
    }
}
