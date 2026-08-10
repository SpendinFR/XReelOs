// MLOmega V19 â€” E25
// UIIntentBroker: arbitrates EVERY UIIntent source (IUIIntentSource) into the set
// of intents the UIRuntime renders, applying GUIDE_V19_REFERENCE Â§13.2 exactly:
//   * the 7-rung priority ladder (UIIntentPriority);
//   * ttl_ms expiry (contract field);
//   * track loss â€” an intent whose target_track_id has left SceneCache.tracks
//     fades then disappears, never re-attached to another object;
//   * a configurable density cap (N simultaneous non-status intents);
//   * dedup by ui_intent_id;
//   * every decision (admitted / refused / expired / evicted) journaled with a
//     `ui_intent_drop_reason` (Â§15.3).
//
// The broker is pure-ish MonoBehaviour glue: sources push intents in (main
// thread), Tick() ages/evicts, and it raises IntentAdmitted / IntentDropped for
// the UIRuntime. It reads SceneCache for track presence but never mutates it.
using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Scene;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>An intent the broker currently holds, plus its arbitration metadata.</summary>
    public sealed class ActiveIntent
    {
        public UIIntent Intent { get; }
        public RenderPriority Priority { get; }
        public long AdmittedMs { get; }
        public string SourceName { get; }

        /// <summary>Set when the intent is fading out (track lost / TTL / eviction) before removal.</summary>
        public bool Fading { get; internal set; }
        /// <summary>Monotonic ms at which the fade began (0 if not fading).</summary>
        public long FadeStartedMs { get; internal set; }
        /// <summary>Reason recorded once the intent is being removed.</summary>
        public UIIntentDropReason PendingDrop { get; internal set; }

        public ActiveIntent(UIIntent intent, RenderPriority priority, long admittedMs, string source)
        {
            Intent = intent;
            Priority = priority;
            AdmittedMs = admittedMs;
            SourceName = source;
            PendingDrop = UIIntentDropReason.Admitted;
        }

        public bool IsStatus => Priority == RenderPriority.StatusPrivacy;
    }

    /// <summary>
    /// Named UI density modes (Â§13.2, E33 voice/menu toggles). The mode gates which
    /// non-status intents the broker admits/keeps:
    ///   * <see cref="Normal"/> â€” everything, capped by the config density cap.
    ///   * <see cref="Minimal"/> â€” only high-priority rungs (Privacy..Reflex),
    ///     conversational/ambient cards suppressed.
    ///   * <see cref="HideAll"/> â€” nothing except StatusBar/Privacy (Â§13.2-1): the
    ///     head-locked StatusBar is a standalone surface and privacy intents are the
    ///     only admitted rung.
    ///   * <see cref="FreeGuy"/> â€” playful/normal density; alias of Normal for the
    ///     broker (the visual theme differs, handled by the renderer).
    /// </summary>
    public enum UIDensityMode
    {
        Normal = 0,
        Minimal = 1,
        HideAll = 2,
        FreeGuy = 3
    }

    public sealed class UIIntentBroker : MonoBehaviour
    {
        [SerializeField] private SceneCache _sceneCache;
        [SerializeField] private SceneCacheConfig _config;

        // Lazy fallback: Awake is not called in EditMode and call order at boot
        // can reach the broker before Awake. Never null-ref on config reads.
        private SceneCacheConfig Cfg
        {
            get
            {
                if (_config == null) _config = _sceneCache != null && _sceneCache.Config != null ? _sceneCache.Config : SceneCacheConfig.CreateDefault();
                return _config;
            }
        }

        /// <summary>Current density mode (voice/menu "cache tout" / "mode Free Guy" / â€¦).</summary>
        public UIDensityMode Density { get; private set; } = UIDensityMode.Normal;

        /// <summary>Raised when the density mode changes (for the StatusBar / receipts).</summary>
        public event Action<UIDensityMode> DensityChanged;

        /// <summary>
        /// Set the named density mode. In <see cref="UIDensityMode.HideAll"/> every
        /// currently-admitted non-status intent is dropped immediately (only the
        /// standalone StatusBar and any privacy intent survive, Â§13.2-1); switching
        /// to a looser mode simply lets future intents back in.
        /// </summary>
        public void SetDensity(UIDensityMode mode)
        {
            if (Density == mode) return;
            Density = mode;
            if (mode == UIDensityMode.HideAll || mode == UIDensityMode.Minimal)
            {
                List<ActiveIntent> drop = null;
                foreach (ActiveIntent ai in _active.Values)
                {
                    if (ai.IsStatus || ai.Fading) continue;
                    if (!AllowedUnderDensity(ai.Priority))
                    {
                        (drop ??= new List<ActiveIntent>()).Add(ai);
                    }
                }
                if (drop != null)
                {
                    foreach (ActiveIntent ai in drop) RemoveNow(ai, UIIntentDropReason.DensityCap);
                }
            }
            DensityChanged?.Invoke(mode);
        }

        /// <summary>Map a named string ("hide_all"/"minimal"/"normal"/"freeguy") to the mode.</summary>
        public static UIDensityMode ParseDensity(string name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "").Replace(" ", ""))
            {
                case "hideall": return UIDensityMode.HideAll;
                case "minimal": return UIDensityMode.Minimal;
                case "freeguy": return UIDensityMode.FreeGuy;
                default: return UIDensityMode.Normal;
            }
        }

        /// <summary>Whether a priority rung is admitted under the current density mode.</summary>
        private bool AllowedUnderDensity(RenderPriority prio)
        {
            switch (Density)
            {
                case UIDensityMode.HideAll:
                    // Only Privacy (rung 1) survives; StatusPrivacy is never counted here.
                    return prio == RenderPriority.StatusPrivacy;
                case UIDensityMode.Minimal:
                    // Keep the safety/focus/subtitle/requested rungs (1-4); drop
                    // ambient task/conversational/decorative (5-7).
                    return (int)prio <= (int)RenderPriority.VisionRtRequested;
                default:
                    return true;
            }
        }

        // ui_intent_id -> active intent.
        private readonly Dictionary<string, ActiveIntent> _active = new Dictionary<string, ActiveIntent>();
        // ui_intent_id -> the "admit" work queued from a source (drained in Tick).
        private readonly Queue<UIIntent> _incoming = new Queue<UIIntent>();
        private readonly List<IUIIntentSource> _sources = new List<IUIIntentSource>();

        /// <summary>Raised when an intent is admitted for rendering (main thread).</summary>
        public event Action<ActiveIntent> IntentAdmitted;

        /// <summary>Raised when an intent begins fading out (still visible during fade).</summary>
        public event Action<ActiveIntent, UIIntentDropReason> IntentFading;

        /// <summary>Raised when an intent is fully removed. Carries its drop reason.</summary>
        public event Action<UIIntent, UIIntentDropReason> IntentDropped;

        /// <summary>Raised for every drop-reason decision, for the `ui_intent_drop_reason` metric (Â§15.3).</summary>
        public event Action<string, UIIntentDropReason, string> DropReasonJournaled;

        /// <summary>Current admitted intents, highest priority first. Read-only snapshot for the UIRuntime.</summary>
        public IReadOnlyCollection<ActiveIntent> ActiveIntents => _active.Values;

        private void Awake()
        {
            if (_sceneCache == null) _sceneCache = FindAnyObjectByType<SceneCache>();
            if (_config == null && _sceneCache != null) _config = _sceneCache.Config;
            if (_config == null) _config = SceneCacheConfig.CreateDefault();
        }

        private void Update() => Tick((long)(Time.unscaledTimeAsDouble * 1000.0));

        /// <summary>
        /// Register a source. The broker subscribes to its <c>IntentProduced</c> and
        /// queues each intent for admission on the next <see cref="Tick"/>.
        /// </summary>
        public void RegisterSource(IUIIntentSource source)
        {
            if (source == null || _sources.Contains(source)) return;
            _sources.Add(source);
            source.IntentProduced += OnSourceIntent;
        }

        public void UnregisterSource(IUIIntentSource source)
        {
            if (source == null) return;
            if (_sources.Remove(source)) source.IntentProduced -= OnSourceIntent;
        }

        private void OnDestroy()
        {
            foreach (IUIIntentSource s in _sources) s.IntentProduced -= OnSourceIntent;
            _sources.Clear();
        }

        private void OnSourceIntent(UIIntent intent)
        {
            if (intent != null) _incoming.Enqueue(intent);
        }

        /// <summary>Push an intent directly (used by local sources and tests).</summary>
        public void Submit(UIIntent intent)
        {
            if (intent != null) _incoming.Enqueue(intent);
        }

        /// <summary>Explicit user dismissal of an intent (from a component / voice command).</summary>
        public void Dismiss(string uiIntentId)
        {
            if (string.IsNullOrEmpty(uiIntentId)) return;
            _sceneCache?.SubmitSuppressed(uiIntentId);
            if (_active.TryGetValue(uiIntentId, out ActiveIntent ai))
            {
                RemoveNow(ai, UIIntentDropReason.UserSuppressed);
            }
            else
            {
                Journal(uiIntentId, UIIntentDropReason.UserSuppressed, "dismiss");
            }
        }

        /// <summary>
        /// Producer withdrawal for ephemeral overlays. Unlike a user dismissal it
        /// does not suppress the stable id, so an explicitly re-enabled feature may
        /// render it again later.
        /// </summary>
        public void Withdraw(string uiIntentId)
        {
            if (string.IsNullOrEmpty(uiIntentId)) return;
            if (_active.TryGetValue(uiIntentId, out ActiveIntent ai))
                RemoveNow(ai, UIIntentDropReason.TtlExpired);
        }

        /// <summary>
        /// Advance arbitration: admit incoming intents, then age/evict the active
        /// set. Exposed so EditMode tests drive time deterministically.
        /// </summary>
        public void Tick(long nowMs)
        {
            while (_incoming.Count > 0)
            {
                Admit(_incoming.Dequeue(), nowMs);
            }
            AgeAndEvict(nowMs);
        }

        // --- admission ------------------------------------------------------------

        private void Admit(UIIntent intent, long nowMs)
        {
            string id = intent.UiIntentId;
            if (string.IsNullOrEmpty(id))
            {
                Journal("<no-id>", UIIntentDropReason.Duplicate, intent.Producer);
                return;
            }

            // Dedup by ui_intent_id: a repeat refreshes the existing intent's
            // payload/admission time rather than adding a second copy.
            if (_active.TryGetValue(id, out ActiveIntent existing))
            {
                Journal(id, UIIntentDropReason.Duplicate, existing.SourceName);
                var refreshed = new ActiveIntent(intent, UIIntentPriority.Classify(intent), nowMs, existing.SourceName);
                _active[id] = refreshed;
                _sceneCache?.SubmitVisibleIntent(id, intent.TtlMs);
                // UIRuntime listens to this same event and recognises the live id,
                // then calls UIComponentBase.Refresh instead of allocating again.
                IntentAdmitted?.Invoke(refreshed);
                return;
            }

            // User already suppressed this id this session -> refuse.
            if (_sceneCache != null && _sceneCache.UiState.IsSuppressed(id))
            {
                Journal(id, UIIntentDropReason.UserSuppressed, intent.Producer);
                return;
            }

            // Track binding: if it claims a target_track_id, that track must exist now.
            if (!string.IsNullOrEmpty(intent.TargetTrackId) &&
                _sceneCache != null && !_sceneCache.Tracks.Contains(intent.TargetTrackId))
            {
                Journal(id, UIIntentDropReason.TrackLost, intent.Producer);
                return;
            }

            RenderPriority prio = UIIntentPriority.Classify(intent);

            // Named density mode (voice/menu "cache tout"/"minimal"): refuse intents
            // whose rung the current mode suppresses. StatusBar is standalone (not
            // admitted here) so "hide_all" leaves only StatusBar + privacy (Â§13.2-1).
            if (!AllowedUnderDensity(prio))
            {
                Journal(id, UIIntentDropReason.DensityCap, intent.Producer);
                return;
            }

            var candidate = new ActiveIntent(intent, prio, nowMs, ResolveSource(intent));

            // Density cap: status/privacy is never counted nor capped.
            int densityCap = Density == UIDensityMode.FreeGuy
                ? Cfg.FreeGuyMaxSimultaneousIntents
                : Cfg.MaxSimultaneousIntents;
            if (!candidate.IsStatus && NonStatusCount() >= densityCap)
            {
                // Try to evict the weakest currently-active non-status intent that is
                // strictly lower priority than the candidate.
                ActiveIntent weakest = WeakestNonStatus();
                if (weakest != null && (int)weakest.Priority > (int)candidate.Priority)
                {
                    RemoveNow(weakest, UIIntentDropReason.DensityCap);
                }
                else
                {
                    // Nothing weaker: refuse the candidate itself.
                    Journal(id, UIIntentDropReason.DensityCap, intent.Producer);
                    return;
                }
            }

            _active[id] = candidate;
            Journal(id, UIIntentDropReason.Admitted, candidate.SourceName);
            _sceneCache?.SubmitVisibleIntent(id, intent.TtlMs);
            IntentAdmitted?.Invoke(candidate);
        }

        // --- ageing / eviction ----------------------------------------------------

        private void AgeAndEvict(long nowMs)
        {
            if (_active.Count == 0) return;
            List<ActiveIntent> toRemove = null;

            foreach (ActiveIntent ai in _active.Values)
            {
                // Already fading: remove once the fade completes.
                if (ai.Fading)
                {
                    if (nowMs - ai.FadeStartedMs >= Cfg.FadeOutMs)
                    {
                        (toRemove ??= new List<ActiveIntent>()).Add(ai);
                    }
                    continue;
                }

                UIIntentDropReason reason = EvaluateDrop(ai, nowMs);
                if (reason != UIIntentDropReason.Admitted)
                {
                    BeginFade(ai, reason);
                }
            }

            if (toRemove != null)
            {
                foreach (ActiveIntent ai in toRemove) FinishRemove(ai);
            }
        }

        private UIIntentDropReason EvaluateDrop(ActiveIntent ai, long nowMs)
        {
            UIIntent intent = ai.Intent;

            // TTL: contract ttl_ms since admission (0 / negative => use ui_state default).
            long ttl = intent.TtlMs > 0 ? intent.TtlMs : Cfg.UiStateDefaultTtlMs;
            if (nowMs - ai.AdmittedMs > ttl)
            {
                return UIIntentDropReason.TtlExpired;
            }

            // Track loss: never re-attach; the intent fades then disappears (Â§13.2).
            if (!string.IsNullOrEmpty(intent.TargetTrackId) &&
                _sceneCache != null && !_sceneCache.Tracks.Contains(intent.TargetTrackId))
            {
                return UIIntentDropReason.TrackLost;
            }

            return UIIntentDropReason.Admitted;
        }

        private void BeginFade(ActiveIntent ai, UIIntentDropReason reason)
        {
            // A zero-length fade removes immediately; otherwise mark and let the
            // next ticks carry it out so the renderer can animate the fade.
            ai.Fading = true;
            ai.FadeStartedMs = _sceneCache != null ? _sceneCache.NowMs : ai.AdmittedMs;
            ai.PendingDrop = reason;
            Journal(ai.Intent.UiIntentId, reason, ai.SourceName);
            IntentFading?.Invoke(ai, reason);
            if (Cfg.FadeOutMs <= 0)
            {
                FinishRemove(ai);
            }
        }

        private void RemoveNow(ActiveIntent ai, UIIntentDropReason reason)
        {
            ai.PendingDrop = reason;
            Journal(ai.Intent.UiIntentId, reason, ai.SourceName);
            IntentFading?.Invoke(ai, reason);
            FinishRemove(ai);
        }

        private void FinishRemove(ActiveIntent ai)
        {
            _active.Remove(ai.Intent.UiIntentId);
            IntentDropped?.Invoke(ai.Intent, ai.PendingDrop);
        }

        // --- helpers --------------------------------------------------------------

        private int NonStatusCount()
        {
            int n = 0;
            foreach (ActiveIntent ai in _active.Values)
            {
                if (!ai.IsStatus && !ai.Fading) n++;
            }
            return n;
        }

        private ActiveIntent WeakestNonStatus()
        {
            ActiveIntent weakest = null;
            foreach (ActiveIntent ai in _active.Values)
            {
                if (ai.IsStatus || ai.Fading) continue;
                if (weakest == null || (int)ai.Priority > (int)weakest.Priority)
                {
                    weakest = ai;
                }
            }
            return weakest;
        }

        private string ResolveSource(UIIntent intent) => intent.Producer ?? "unknown";

        private void Journal(string uiIntentId, UIIntentDropReason reason, string source)
        {
            DropReasonJournaled?.Invoke(uiIntentId, reason, source);
        }
    }
}
