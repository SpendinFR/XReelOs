// MLOmega V19 — E25
// Base class for every liquid-glass UI component. It owns the shared lifecycle the
// UIRuntime drives — admit -> display -> fade -> recycle — and the receipt
// semantics of GUIDE_V19_REFERENCE §13.3, so no concrete component can forget to
// emit them:
//   * displayed : emitted once the panel is actually on screen (first frame the
//                 fade-in reaches visible), proving the message reached the glasses;
//   * seen      : emitted after a prudent, configurable dwell timer (gaze/timer,
//                 §13.3 "exposition, pas compréhension") — never on load;
//   * acted     : emitted on an explicit confirmation interaction;
//   * dismissed : emitted when the user (or the broker) removes it;
//   * corrected : emitted by CorrectionChip via RaiseCorrected.
//
// It also applies the §17.2 truth ladder uniformly: a "probable" intent gets a
// discreet badge, a "remembered"/last-seen intent shows its age, an "inferred"
// intent is labelled as a hypothesis, and the rim accent colour encodes the level.
// Concrete components override Bind() to fill their own content; everything about
// glass styling, fade/scale animation (<=200 ms) and receipts lives here.
using System;
using System.Collections.Generic;
using System.Globalization;
using MLOmega.Contracts.V19;
using MLOmega.XR.Scene;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Shared services a component needs beyond its intent: the live SceneCache
    /// (for anchoring to tracks/entities/spatial), the shared glass material, and
    /// the camera it head-locks / billboards against. Set once by the UIRuntime
    /// when the component is created, before the first Admit.
    /// </summary>
    public sealed class UIComponentContext
    {
        public SceneCache SceneCache { get; }
        public Material GlassMaterial { get; }
        public Camera Camera { get; }

        public UIComponentContext(SceneCache sceneCache, Material glassMaterial, Camera camera)
        {
            SceneCache = sceneCache;
            GlassMaterial = glassMaterial;
            Camera = camera;
        }
    }

    /// <summary>Lifecycle phase of a component, mirrored to receipts.</summary>
    public enum UIComponentPhase
    {
        Idle = 0,
        Appearing = 1,
        Visible = 2,
        Fading = 3
    }

    [DisallowMultipleComponent]
    public abstract class UIComponentBase : MonoBehaviour
    {
        [Header("Theme + timings")]
        [SerializeField] protected UITheme _theme;

        [Tooltip("Prudent dwell before a 'seen' receipt is emitted (§13.3). Seconds.")]
        [Min(0f)]
        [SerializeField] private float _seenDwellSeconds = 1.2f;

        /// <summary>The intent this component currently renders (null when idle/pooled).</summary>
        public UIIntent Intent { get; private set; }

        /// <summary>Current lifecycle phase.</summary>
        public UIComponentPhase Phase { get; private set; } = UIComponentPhase.Idle;

        /// <summary>Component key from §13.1 this concrete type renders (e.g. "object_outline").</summary>
        public abstract string ComponentKey { get; }

        protected IReceiptSink Sink { get; private set; }
        protected UITheme Theme => _theme;

        /// <summary>Shared services (SceneCache/material/camera), set by the runtime.</summary>
        protected UIComponentContext Context { get; private set; }

        /// <summary>Called once by the UIRuntime right after instantiation, before the first Admit.</summary>
        public void Configure(UIComponentContext context, UITheme theme)
        {
            Context = context;
            if (theme != null) _theme = theme;
            OnConfigured();
        }

        /// <summary>Override to build persistent visuals (panels, meshes) once.</summary>
        protected virtual void OnConfigured() { }

        // Animation state.
        private float _alpha;              // 0..1 current visible alpha
        private float _fadeTarget = 1f;    // 1 = fade in, 0 = fade out
        private float _fadeSpeed;          // per-second alpha delta
        private float _scale;              // 0..1 eased scale
        private Vector3 _baseScale = Vector3.one;

        // Receipt bookkeeping.
        private bool _displayedSent;
        private bool _seenSent;
        private float _visibleSince = -1f;
        private Action<UIComponentBase> _onRecycled;

        // ------------------------------------------------------------------
        //  UIRuntime-facing API
        // ------------------------------------------------------------------

        /// <summary>
        /// Bind this pooled component to an intent and start the appear animation.
        /// Called by the UIRuntime on admission. <paramref name="onRecycled"/> is
        /// invoked once the component has fully faded and is ready to return to
        /// the pool.
        /// </summary>
        public void Admit(UIIntent intent, IReceiptSink sink, Action<UIComponentBase> onRecycled)
        {
            Intent = intent;
            Sink = sink;
            _onRecycled = onRecycled;

            _displayedSent = false;
            _seenSent = false;
            _visibleSince = -1f;
            _alpha = 0f;
            _scale = _theme != null ? _theme.ScaleFrom : 0.92f;
            _fadeTarget = 1f;
            _baseScale = transform.localScale;
            Phase = UIComponentPhase.Appearing;

            gameObject.SetActive(true);
            ApplyTruth(intent);
            Bind(intent);
            ApplyVisual();
        }

        /// <summary>Refresh the payload of an already-admitted intent (dedup path in the broker).</summary>
        public void Refresh(UIIntent intent)
        {
            Intent = intent;
            ApplyTruth(intent);
            Bind(intent);
        }

        /// <summary>
        /// Begin fading the component out. <paramref name="userDismissed"/> selects
        /// the receipt: an explicit user dismissal emits "dismissed"; any other
        /// reason (TTL/track-loss/eviction) fades silently (the broker already
        /// journaled the drop-reason, and a dismissed receipt should only reflect a
        /// deliberate user action per §13.3).
        /// </summary>
        public void BeginFadeOut(bool userDismissed)
        {
            if (Phase == UIComponentPhase.Fading || Phase == UIComponentPhase.Idle) return;
            if (userDismissed)
            {
                EmitReceipt("dismissed", null);
            }
            _fadeTarget = 0f;
            Phase = UIComponentPhase.Fading;
        }

        // ------------------------------------------------------------------
        //  Interaction hooks for concrete components
        // ------------------------------------------------------------------

        /// <summary>Emit an "acted" receipt for an explicit confirmation (voice/gesture/tap).</summary>
        public void RaiseActed(Dictionary<string, object> userAction = null)
        {
            EmitReceipt("acted", userAction);
        }

        /// <summary>Emit a "dismissed" receipt and start fading (user chose to hide this).</summary>
        public void RaiseDismissed()
        {
            BeginFadeOut(true);
        }

        /// <summary>Emit a "corrected" receipt (CorrectionChip / voice correction, §13.3).</summary>
        public void RaiseCorrected(Dictionary<string, object> userAction)
        {
            EmitReceipt("corrected", userAction);
        }

        // ------------------------------------------------------------------
        //  Subclass contract
        // ------------------------------------------------------------------

        /// <summary>Fill component-specific content from the intent. Called on Admit/Refresh.</summary>
        protected abstract void Bind(UIIntent intent);

        /// <summary>
        /// Apply the current alpha/scale to the concrete visuals. Override to push
        /// alpha into materials/canvas groups. Base implementation scales the
        /// transform for the appear ease.
        /// </summary>
        protected virtual void ApplyVisual()
        {
            transform.localScale = _baseScale * _scale;
        }

        /// <summary>Current animated alpha (0..1) for subclasses to tint their materials.</summary>
        protected float CurrentAlpha => _alpha;

        // ------------------------------------------------------------------
        //  Lifecycle animation + receipts
        // ------------------------------------------------------------------

        protected virtual void Update()
        {
            Tick(Time.unscaledTime, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Deterministic lifecycle step. Public so EditMode tests (and any headless
        /// driver) advance the animation + receipt timeline without a running player
        /// loop, mirroring SceneCache.Tick / UIIntentBroker.Tick.
        /// <paramref name="now"/> is the current unscaled time in seconds.
        /// </summary>
        public void Tick(float now, float dt)
        {
            if (Phase == UIComponentPhase.Idle) return;

            // Alpha easing, capped so the whole transition stays <=200 ms.
            float fadeSeconds = _fadeTarget > 0.5f
                ? (_theme != null ? _theme.FadeInSeconds : 0.16f)
                : (_theme != null ? _theme.FadeOutSeconds : 0.18f);
            _fadeSpeed = fadeSeconds > 0f ? 1f / fadeSeconds : 1000f;
            _alpha = Mathf.MoveTowards(_alpha, _fadeTarget, _fadeSpeed * dt);

            // Scale ease-in toward 1 while appearing/visible.
            float scaleSeconds = _theme != null ? _theme.ScaleInSeconds : 0.14f;
            float scaleSpeed = scaleSeconds > 0f ? 1f / scaleSeconds : 1000f;
            _scale = Mathf.MoveTowards(_scale, _fadeTarget > 0.5f ? 1f : _scale, scaleSpeed * dt);

            ApplyVisual();

            if (Phase == UIComponentPhase.Appearing)
            {
                // "displayed" fires the moment the panel is actually visible.
                if (!_displayedSent && _alpha > 0.01f)
                {
                    _displayedSent = true;
                    _visibleSince = now;
                    EmitReceipt("displayed", null);
                }
                if (_alpha >= 0.999f)
                {
                    Phase = UIComponentPhase.Visible;
                }
            }

            if (Phase == UIComponentPhase.Visible)
            {
                // "seen" after a prudent dwell — exposure, never comprehension.
                if (!_seenSent && _visibleSince >= 0f &&
                    now - _visibleSince >= _seenDwellSeconds)
                {
                    _seenSent = true;
                    EmitReceipt("seen", null);
                }
            }

            if (Phase == UIComponentPhase.Fading && _alpha <= 0.001f)
            {
                Recycle();
            }
        }

        private void Recycle()
        {
            Phase = UIComponentPhase.Idle;
            Intent = null;
            gameObject.SetActive(false);
            transform.localScale = _baseScale;
            _onRecycled?.Invoke(this);
        }

        // ------------------------------------------------------------------
        //  §17.2 truth application (shared by all components)
        // ------------------------------------------------------------------

        private void ApplyTruth(UIIntent intent)
        {
            OnTruth(TruthDescriptor.From(intent, _theme));
        }

        /// <summary>
        /// Override to render the truth badge/age/hypothesis label. Default no-op
        /// so components with no text surface (e.g. an arrow) can ignore it; text
        /// components call <see cref="TruthDescriptor"/> helpers.
        /// </summary>
        protected virtual void OnTruth(TruthDescriptor truth) { }

        // ------------------------------------------------------------------
        //  Receipt emission (§13.3)
        // ------------------------------------------------------------------

        private void EmitReceipt(string ev, Dictionary<string, object> userAction)
        {
            if (Sink == null || Intent == null) return;
            var receipt = new UIReceipt
            {
                ContractsVersion = Intent.ContractsVersion ?? "v19.0",
                UiIntentId = Intent.UiIntentId,
                DeliveryId = Intent.DeliveryId,
                Event = ev,
                ObservedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Source = "ui_runtime",
                UserAction = userAction
            };
            Sink.Send(receipt);
        }
    }

    /// <summary>
    /// Resolved §17.2 display attributes for an intent: whether to show a
    /// "probable" badge, a last-seen age, a hypothesis label, and which accent
    /// colour the rim should carry. Kept as a small pure struct so it is unit
    /// testable without a running component.
    /// </summary>
    public readonly struct TruthDescriptor
    {
        public readonly string Level;          // observed | probable | remembered | inferred | replay
        public readonly bool ShowProbableBadge;
        public readonly bool ShowHypothesisLabel;
        public readonly string AgeText;        // non-null when last-seen age should be shown
        public readonly Color Accent;

        public TruthDescriptor(string level, bool probable, bool hypothesis, string ageText, Color accent)
        {
            Level = level;
            ShowProbableBadge = probable;
            ShowHypothesisLabel = hypothesis;
            AgeText = ageText;
            Accent = accent;
        }

        public static TruthDescriptor From(UIIntent intent, UITheme theme)
        {
            string level = Norm(intent?.TruthLevel);
            bool probable = level == "probable";
            bool hypothesis = level == "inferred";
            string age = ResolveAgeText(intent, level);
            Color accent = theme != null ? theme.AccentFor(level) : Color.white;
            return new TruthDescriptor(level, probable, hypothesis, age, accent);
        }

        /// <summary>
        /// §17.2: remembered/last-seen intents display age + place/condition. We
        /// read an explicit last_seen_ms/age_ms/last_seen from ui_hint or content;
        /// when the level is remembered/replay and none is present we still label
        /// it as remembered rather than pretending it is fresh.
        /// </summary>
        private static string ResolveAgeText(UIIntent intent, string level)
        {
            if (intent == null) return null;
            double ageMs = ReadAgeMs(intent.UiHint);
            if (double.IsNaN(ageMs)) ageMs = ReadAgeMs(intent.Content);

            bool remembered = level == "remembered" || level == "replay";
            if (double.IsNaN(ageMs))
            {
                return remembered ? "last seen: earlier" : null;
            }
            return "last seen " + HumanizeAge(ageMs);
        }

        private static double ReadAgeMs(Dictionary<string, object> d)
        {
            if (d == null) return double.NaN;
            foreach (string key in new[] { "age_ms", "last_seen_ms", "last_seen_age_ms" })
            {
                if (d.TryGetValue(key, out object v) && v != null &&
                    double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
                {
                    return ms;
                }
            }
            return double.NaN;
        }

        private static string HumanizeAge(double ms)
        {
            double s = ms / 1000.0;
            if (s < 60) return $"{Mathf.RoundToInt((float)s)}s ago";
            double m = s / 60.0;
            if (m < 60) return $"{Mathf.RoundToInt((float)m)}m ago";
            double h = m / 60.0;
            if (h < 24) return $"{Mathf.RoundToInt((float)h)}h ago";
            return $"{Mathf.RoundToInt((float)(h / 24.0))}d ago";
        }

        private static string Norm(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Trim().ToLowerInvariant();
    }
}
