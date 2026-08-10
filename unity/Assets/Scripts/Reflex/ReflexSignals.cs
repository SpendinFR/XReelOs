// MLOmega V19 — E26
// Shared enums for the Ultra-Live reflex layer: the activation signals of
// GUIDE_V19_REFERENCE §9.3, the skill identities, and the gesture/ASR event kinds
// that the native bridges surface (mirroring the Kotlin GestureKind and the
// AsrKws callbacks 1:1, so the C# side never re-derives them).
namespace MLOmega.XR.Reflex
{
    /// <summary>
    /// The environmental signals the ReflexScheduler activates skills from
    /// (§9.3 — "activation par signal, pas de modes"). A frame can raise several
    /// at once; the scheduler maps each to its skill(s) within the budget.
    /// </summary>
    public enum ReflexSignal
    {
        /// <summary>Centre of view on text → LensWindow + OCR ROI.</summary>
        ViewCentreOnText = 0,
        /// <summary>Hand + near object → HandAction/StableTrack.</summary>
        HandNearObject = 1,
        /// <summary>Multi-language conversation → Subtitle.</summary>
        MultiLanguageConversation = 2,
        /// <summary>Fast motion / proximity → MotionProximity.</summary>
        FastMotionOrProximity = 3,
        /// <summary>Spoken "where is …" command → FocusSearch.</summary>
        WhereIsCommand = 4,
        /// <summary>Zone change → keyframe / WorldBrain change candidate (not an on-device skill).</summary>
        ZoneChange = 5,
        /// <summary>PhoneOnly session active: keep ASR + Subtitle warm for wake word/offline speech.</summary>
        ContinuousSpeech = 6,
        /// <summary>PhoneOnly session active: keep the gesture detector warm so a hand can be discovered.</summary>
        ContinuousGestures = 7
    }

    /// <summary>The on-device Ultra-Live skills the scheduler owns.</summary>
    public enum ReflexSkillId
    {
        StableTrack = 0,
        LensWindow = 1,
        MotionProximity = 2,
        FocusSearch = 3,
        Subtitle = 4
    }

    /// <summary>
    /// Recognised gesture kinds — mirrors the Kotlin
    /// com.mlomega.xr.reflexvision.GestureKind exactly.
    /// </summary>
    public enum GestureKind
    {
        PinchBegin = 0,
        PinchUpdate = 1,
        PinchEnd = 2,
        OpenPalmMenu = 3,
        SwipeHide = 4,
        FistToggle = 5,
        TwoPalmMenu = 6,
        IndexScrollBegin = 7,
        IndexScrollUpdate = 8,
        IndexScrollEnd = 9,
        TwoFingerKeyboard = 10,
        ThumbUpQuickMenu = 11
    }
}
