// MLOmega V19 — E26
// ReflexScheduler: activation-by-signal, EXACTLY per GUIDE_V19_REFERENCE §9.3 —
// there are NO visible "modes". Environmental signals raised each frame map to
// the skills (and native detectors) that should be warm:
//   centre of view on text     → LensWindow (+ OCR ROI on the PC)
//   hand + near object         → HandAction/StableTrack
//   multi-language conversation→ Subtitle
//   fast motion / proximity    → MotionProximity
//   "where is …" command       → FocusSearch
// It respects a budget (§9.4 — never all detectors in parallel): at most N skills
// active at once; when a higher-priority signal needs a slot the lowest-priority
// active skill is dropped. It also drives the Kotlin detectors on demand: the
// GesturePipeline only runs while a gesture-relevant signal is up, the AsrKws
// service only while a speech-relevant signal (or the wake gate) is up — battery.
using System;
using System.Collections.Generic;
using MLOmega.XR.Reflex.Skills;
using MLOmega.XR.Transport;
using MLOmega.XR.UI;
using MLOmega.XR.UI.Components;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    public sealed class ReflexScheduler : MonoBehaviour
    {
        [SerializeField] private ReflexConfig _config;
        [SerializeField] private GestureBridge _gestureBridge;
        [SerializeField] private AsrBridge _asrBridge;
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private DeviceCommandHandler _commands;

        // E59: when the PanelManipulator claims a pinch (grab/resize/button-tap on a
        // manipulable panel), the SAME pinch must NOT also drive the LensWindow zoom.
        // Optional — absent = old behaviour (every pinch zooms). See PanelManipulator.
        [SerializeField] private PanelManipulator _panelManipulator;

        [Header("Skills")]
        [SerializeField] private StableTrackSkill _stableTrack;
        [SerializeField] private LensWindowSkill _lensWindow;
        [SerializeField] private MotionProximitySkill _motionProximity;
        [SerializeField] private FocusSearchSkill _focusSearch;
        [SerializeField] private SubtitleSkill _subtitle;

        /// <summary>Signal priority for budget eviction: lower value = keep first.</summary>
        private static readonly Dictionary<ReflexSkillId, int> SkillPriority =
            new Dictionary<ReflexSkillId, int>
            {
                { ReflexSkillId.MotionProximity, 0 }, // safety-adjacent, keep first
                { ReflexSkillId.LensWindow, 1 },      // explicit focus
                { ReflexSkillId.Subtitle, 2 },
                { ReflexSkillId.FocusSearch, 3 },
                { ReflexSkillId.StableTrack, 4 }
            };

        // signal -> which skills it wants active.
        private readonly Dictionary<ReflexSignal, ReflexSkillId[]> _signalMap =
            new Dictionary<ReflexSignal, ReflexSkillId[]>
            {
                { ReflexSignal.ViewCentreOnText, new[] { ReflexSkillId.LensWindow } },
                { ReflexSignal.HandNearObject, new[] { ReflexSkillId.StableTrack, ReflexSkillId.LensWindow } },
                { ReflexSignal.MultiLanguageConversation, new[] { ReflexSkillId.Subtitle } },
                { ReflexSignal.FastMotionOrProximity, new[] { ReflexSkillId.MotionProximity } },
                { ReflexSignal.WhereIsCommand, new[] { ReflexSkillId.FocusSearch } },
                // ZoneChange is a WorldBrain/keyframe concern, not an on-device skill.
                { ReflexSignal.ZoneChange, Array.Empty<ReflexSkillId>() },
                // Baseline PhoneOnly signals break the detector chicken-and-egg:
                // ASR must already run to discover speech/wake words and gestures must
                // already run to discover a hand. Skills remain budgeted/event-driven.
                { ReflexSignal.ContinuousSpeech, new[] { ReflexSkillId.Subtitle } },
                { ReflexSignal.ContinuousGestures, Array.Empty<ReflexSkillId>() }
            };

        // Last time each skill was requested by a signal (for linger).
        private readonly Dictionary<ReflexSkillId, long> _lastRequestedMs =
            new Dictionary<ReflexSkillId, long>();

        private readonly HashSet<ReflexSignal> _activeSignals = new HashSet<ReflexSignal>();
        private bool _privacyPaused;
        private ObjectProfileCard _objectCardPinchTarget;

        public IReadOnlyDictionary<ReflexSkillId, ReflexSkillBase> Skills => _skills;
        private readonly Dictionary<ReflexSkillId, ReflexSkillBase> _skills =
            new Dictionary<ReflexSkillId, ReflexSkillBase>();

        private void Awake()
        {
            if (_config == null) _config = ReflexConfig.CreateDefault();
            if (_asrBridge == null) _asrBridge = FindAnyObjectByType<AsrBridge>();
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            Register(_stableTrack);
            Register(_lensWindow);
            Register(_motionProximity);
            Register(_focusSearch);
            Register(_subtitle);
        }

        private void OnEnable()
        {
            // Pinch → LensWindow zoom (E47-B: the pinch handler existed on the skill
            // but was never subscribed to the bridge). Palm/swipe/pinch-commit are
            // wired separately by MenuGestureController.
            if (_gestureBridge != null) _gestureBridge.GestureRecognized += OnGestureForLens;
            if (_asrBridge == null) _asrBridge = FindAnyObjectByType<AsrBridge>();
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_asrBridge != null) _asrBridge.Transcript += OnOfflineFocusTranscript;
            if (_commands == null) _commands = FindAnyObjectByType<DeviceCommandHandler>();
            if (_commands != null) _commands.PrivacyPauseChanged += SetPrivacyPaused;
        }

        private void OnDisable()
        {
            if (_gestureBridge != null) _gestureBridge.GestureRecognized -= OnGestureForLens;
            if (_asrBridge != null) _asrBridge.Transcript -= OnOfflineFocusTranscript;
            if (_commands != null) _commands.PrivacyPauseChanged -= SetPrivacyPaused;
        }

        private void OnGestureForLens(GestureEvent ev)
        {
            // Interactive object-card rows own the pinch before either panel
            // manipulation or LensWindow. This avoids the old event-subscriber race
            // where one pinch could trigger both a card action and a zoom.
            if (HandleObjectCardPinch(ev)) return;
            // E59: run the window-manager FIRST so its pinch-begin hit-test can claim a
            // grab/resize on a manipulable panel. Only when it does NOT claim does the
            // pinch fall through to the LensWindow zoom (never steal the existing zoom).
            if (_panelManipulator != null)
            {
                _panelManipulator.OnGesture(ev);
                if (_panelManipulator.HasClaim) return;
            }
            if (_lensWindow != null) _lensWindow.OnGesture(ev);
        }

        private bool HandleObjectCardPinch(GestureEvent ev)
        {
            if (ev.Kind == GestureKind.PinchBegin)
            {
                _objectCardPinchTarget = null;
                foreach (ObjectProfileCard card in ObjectProfileCard.ActiveCards)
                {
                    if (card == null || card.ResolveActionAtViewport(ev.ScreenPoint) < 0) continue;
                    _objectCardPinchTarget = card;
                    card.HoverAtViewport(ev.ScreenPoint);
                    return true;
                }
                return false;
            }
            if (_objectCardPinchTarget == null) return false;
            _objectCardPinchTarget.HoverAtViewport(ev.ScreenPoint);
            if (ev.Kind == GestureKind.PinchEnd)
            {
                _objectCardPinchTarget.PinchCommit();
                _objectCardPinchTarget = null;
            }
            return true;
        }

        private void OnOfflineFocusTranscript(TranscriptEvent ev)
        {
            // Connected commands already traverse device_transcript -> PC
            // IntentRouter -> WorldBrain/VisionRT. Local SceneCache is the honest
            // fallback only, otherwise one utterance would create two searches.
            if (_privacyPaused || !ev.IsFinal || !ev.IsCommand || _focusSearch == null) return;
            if (_transport != null && (_transport.State == LiveTransportState.Connected ||
                                       _transport.State == LiveTransportState.Degraded)) return;
            if (!TryExtractWhereTarget(ev.Text, out string target)) return;

            RaiseSignal(ReflexSignal.WhereIsCommand);
            Tick((long)(Time.unscaledTimeAsDouble * 1000.0));
            // An explicit command must not disappear if other detector signals
            // consume the regular skill budget for this exact frame.
            if (!_focusSearch.IsActive) _focusSearch.Activate();
            _focusSearch.Locate(target);
        }

        public static bool TryExtractWhereTarget(string text, out string target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = text.Trim().TrimEnd('?', '!', '.', ',', ';', ':').ToLowerInvariant();
            string[] prefixes =
            {
                "where is ", "where are ",
                "où est ", "ou est ", "où sont ", "ou sont ",
                "où se trouve ", "ou se trouve ",
                "où se trouvent ", "ou se trouvent "
            };
            foreach (string prefix in prefixes)
            {
                if (!value.StartsWith(prefix, StringComparison.Ordinal)) continue;
                value = value.Substring(prefix.Length).Trim();
                string[] articles = { "my ", "the ", "mes ", "mon ", "ma ", "le ", "la ", "les ", "des " };
                foreach (string article in articles)
                {
                    if (!value.StartsWith(article, StringComparison.Ordinal)) continue;
                    value = value.Substring(article.Length).Trim();
                    break;
                }
                if (value.Length == 0) return false;
                target = value;
                return true;
            }
            return false;
        }

        private void Register(ReflexSkillBase skill)
        {
            if (skill != null) _skills[skill.SkillId] = skill;
        }

        /// <summary>
        /// Raise a signal for this frame. Signals are edge-cleared each Tick, so a
        /// caller re-raises them while the condition holds; the linger keeps a skill
        /// warm briefly after its signal stops (anti-flap).
        /// </summary>
        public void RaiseSignal(ReflexSignal signal) => _activeSignals.Add(signal);

        private void Update() => Tick((long)(Time.unscaledTimeAsDouble * 1000.0));

        /// <summary>
        /// Reconcile active skills + native detectors against the raised signals,
        /// within budget. Deterministic (takes now) for EditMode tests.
        /// </summary>
        public void Tick(long nowMs)
        {
            if (_privacyPaused)
            {
                _activeSignals.Clear();
                foreach (ReflexSkillBase skill in _skills.Values)
                    if (skill.IsActive) skill.Deactivate();
                if (_gestureBridge != null && _gestureBridge.IsRunning) _gestureBridge.Deactivate();
                if (_asrBridge != null && _asrBridge.IsRunning) _asrBridge.Deactivate();
                return;
            }
            bool continuousSpeech = _activeSignals.Contains(ReflexSignal.ContinuousSpeech);
            bool continuousGestures = _activeSignals.Contains(ReflexSignal.ContinuousGestures);
            // 1) collect desired skills from raised signals.
            var desired = new HashSet<ReflexSkillId>();
            foreach (ReflexSignal s in _activeSignals)
            {
                if (_signalMap.TryGetValue(s, out ReflexSkillId[] ids))
                {
                    foreach (ReflexSkillId id in ids)
                    {
                        if (_skills.ContainsKey(id))
                        {
                            desired.Add(id);
                            _lastRequestedMs[id] = nowMs;
                        }
                    }
                }
            }
            _activeSignals.Clear();

            // 2) keep skills whose linger has not elapsed even without a fresh signal.
            long linger = _config != null ? _config.SkillLingerMs : 1500;
            foreach (KeyValuePair<ReflexSkillId, long> kv in _lastRequestedMs)
            {
                if (nowMs - kv.Value <= linger) desired.Add(kv.Key);
            }

            // 3) enforce the budget (§9.4): keep the highest-priority desired skills.
            int budget = _config != null ? _config.MaxSimultaneousSkills : 3;
            List<ReflexSkillId> ordered = new List<ReflexSkillId>(desired);
            ordered.Sort((a, b) => Prio(a).CompareTo(Prio(b)));
            var keep = new HashSet<ReflexSkillId>();
            for (int i = 0; i < ordered.Count && keep.Count < budget; i++) keep.Add(ordered[i]);

            // 4) apply: activate kept, deactivate the rest.
            foreach (KeyValuePair<ReflexSkillId, ReflexSkillBase> kv in _skills)
            {
                bool shouldRun = keep.Contains(kv.Key);
                if (shouldRun && !kv.Value.IsActive) kv.Value.Activate();
                else if (!shouldRun && kv.Value.IsActive) kv.Value.Deactivate();
            }

            // 5) drive native detectors on demand (battery — §9.4).
            DriveDetectors(keep, continuousSpeech, continuousGestures);
        }

        public void SetPrivacyPaused(bool paused)
        {
            _privacyPaused = paused;
            if (paused) Tick((long)(Time.unscaledTimeAsDouble * 1000.0));
        }

        private void DriveDetectors(HashSet<ReflexSkillId> keep,
            bool continuousSpeech, bool continuousGestures)
        {
            // Gestures are needed when LensWindow (pinch zoom) or StableTrack (hand) run.
            bool wantGestures = continuousGestures ||
                                keep.Contains(ReflexSkillId.LensWindow) ||
                                keep.Contains(ReflexSkillId.StableTrack);
            if (_gestureBridge != null)
            {
                if (wantGestures && !_gestureBridge.IsRunning) _gestureBridge.Activate();
                else if (!wantGestures && _gestureBridge.IsRunning) _gestureBridge.Deactivate();
            }

            // ASR is needed when Subtitle runs. (The WakeWordGate manages the mic
            // independently for wake-word-only listening.)
            bool wantAsr = continuousSpeech || keep.Contains(ReflexSkillId.Subtitle);
            if (_asrBridge != null)
            {
                if (wantAsr && !_asrBridge.IsRunning) _asrBridge.Activate();
                else if (!wantAsr && _asrBridge.IsRunning) _asrBridge.Deactivate();
            }
        }

        private static int Prio(ReflexSkillId id) =>
            SkillPriority.TryGetValue(id, out int p) ? p : 99;

        /// <summary>Configure every registered skill with the session + reflex sink.</summary>
        public void ConfigureSkills(string sessionId, IReflexEventSink reflexSink)
        {
            foreach (ReflexSkillBase skill in _skills.Values)
            {
                skill.Configure(sessionId, reflexSink, _config);
            }
        }

        /// <summary>Test/introspection: is a given skill currently active?</summary>
        public bool IsSkillActive(ReflexSkillId id) =>
            _skills.TryGetValue(id, out ReflexSkillBase s) && s.IsActive;
    }
}
