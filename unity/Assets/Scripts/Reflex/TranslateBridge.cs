// MLOmega V19 — E48-A
// TranslateBridge: the live translation reflex, DEVICE-side and OFFLINE.
//
// It wires the on-device ASR (AsrBridge) to the SubtitleSkill and, when live
// translation is toggled ON and a FINAL segment's language differs from the target
// language, translates it on the phone (Kotlin OfflineTranslator — ONNX Runtime +
// OPUS-MT int8) and renders the translation under the original subtitle. Works with
// the PC absent, like the subtitle path itself (guide §3.2).
//
// Invariants (E48-A):
//   * finals only — partials are forwarded verbatim, never translated;
//   * honest degraded — if the model is absent or a translation fails, the original
//     line still shows (translation = null), never a crash;
//   * budget — translation runs on a background thread; the native sessions release
//     themselves after ~60 s idle (OfflineTranslator), ticked here.
//
// Same DIRECT_ANDROID / editor split as AsrBridge/LiveTransportBridge: on device it
// drives the Kotlin OfflineTranslatorBridge (AndroidJavaObject); in the editor a
// lightweight marker translation ("[fr→en] …") exercises the whole subtitle path
// without a plugin.
using System;
using System.Collections.Generic;
using System.Threading;
using MLOmega.XR.Core;
using MLOmega.XR.Reflex.Skills;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    public sealed class TranslateBridge : MonoBehaviour
    {
        [SerializeField] private MLOmegaConfig _config;
        [SerializeField] private AsrBridge _asrBridge;
        [SerializeField] private SubtitleSkill _subtitle;
        // UI side (Reflex references UI; the reverse would be an assembly cycle, so
        // the DeviceCommandHandler exposes an event we subscribe to instead).
        [SerializeField] private MLOmega.XR.UI.DeviceCommandHandler _commands;
        [SerializeField] private MLOmega.XR.UI.Components.StatusBar _statusBar;

        [Tooltip("E48-A: relative dir (under getExternalFilesDir()/models/) that " +
                 "holds the provisioned OPUS-MT translation directions " +
                 "(opus-mt-fr-en / opus-mt-en-fr). The native bridge resolves each " +
                 "direction under it.")]
        [SerializeField] private string _modelsRelativeRoot = "models";

        /// <summary>Whether live translation is currently ON. Toggled by the menu / voice.</summary>
        public bool TranslateLive { get; private set; }

        /// <summary>Raised on the main thread whenever TranslateLive changes.</summary>
        public event Action<bool> TranslateLiveChanged;

        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();

        // A translation runs on a background thread; a monotonically increasing token
        // discards stale results (a newer final supersedes an in-flight translation).
        private long _translationSeq;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _native;
#endif

        private void Awake()
        {
            if (_config == null) _config = FindAnyObjectByType<SessionPairing>()?.Config;
            if (_asrBridge == null) _asrBridge = FindAnyObjectByType<AsrBridge>();
            if (_subtitle == null) _subtitle = FindAnyObjectByType<SubtitleSkill>();
            if (_commands == null) _commands = FindAnyObjectByType<MLOmega.XR.UI.DeviceCommandHandler>();
            if (_statusBar == null) _statusBar = FindAnyObjectByType<MLOmega.XR.UI.Components.StatusBar>();
            // Default the toggle from config so a build can ship with it pre-armed.
            TranslateLive = _config != null && _config.TranslateLiveDefault;
        }

        private void OnEnable()
        {
            if (_asrBridge != null) _asrBridge.Transcript += OnTranscript;
            if (_commands != null) _commands.TranslateLiveRequested += OnTranslateLiveRequested;
            if (_commands != null) _commands.TranslateTextRequested += OnTranslateTextRequested;
        }

        private void OnDisable()
        {
            if (_asrBridge != null) _asrBridge.Transcript -= OnTranscript;
            if (_commands != null) _commands.TranslateLiveRequested -= OnTranslateLiveRequested;
            if (_commands != null) _commands.TranslateTextRequested -= OnTranslateTextRequested;
        }

        /// <summary>The translate_live device command (menu flip / voice on-off).</summary>
        private void OnTranslateLiveRequested(bool? on)
        {
            bool target = on ?? !TranslateLive;
            SetTranslateLive(target);
            if (_statusBar != null) _statusBar.TranslateLive = target;
        }

        private void OnTranslateTextRequested(string text, string source, string target)
        {
            if (_subtitle == null || string.IsNullOrWhiteSpace(text)) return;
            source = string.IsNullOrWhiteSpace(source) ? (TargetLang == "fr" ? "en" : "fr") : source;
            target = string.IsNullOrWhiteSpace(target) ? TargetLang : target;
            _subtitle.OnTranscript(text, true, source);
            long token = Interlocked.Increment(ref _translationSeq);
            RunTranslation(text, source, target, token);
        }

        private void Update()
        {
            DrainMainThread();
            // Let the native sessions free themselves after idle (battery).
            TickIdleRelease();
        }

        /// <summary>
        /// Toggle live translation on/off (from the menu entry or the PC "traduis en
        /// direct" / "stop traduction" device command). Idempotent. Releases the
        /// native sessions when turned off.
        /// </summary>
        public void SetTranslateLive(bool on)
        {
            if (TranslateLive == on) return;
            TranslateLive = on;
            if (!on) ReleaseNative();
            TranslateLiveChanged?.Invoke(on);
        }

        /// <summary>The configured target language ("fr"/"en") the reflex translates INTO.</summary>
        private string TargetLang =>
            _config != null && _config.AsrLanguage == ReflexAsrLanguage.Fr ? "fr" : "en";

        // --- transcript handling --------------------------------------------------

        private void OnTranscript(TranscriptEvent ev)
        {
            if (_subtitle == null) return;

            // Partials render immediately, never translated (finals-only invariant).
            if (!ev.IsFinal)
            {
                _subtitle.OnTranscript(ev.Text, false, ev.Language);
                return;
            }

            string target = TargetLang;
            bool wantTranslate = TranslateLive
                && !string.IsNullOrEmpty(ev.Language)
                && !string.Equals(ev.Language, target, StringComparison.OrdinalIgnoreCase);

            if (!wantTranslate)
            {
                _subtitle.OnTranscript(ev.Text, true, ev.Language);
                return;
            }

            // Show the original line now; the translation arrives asynchronously and
            // refreshes the SAME line (same turn) once ready — never blocks capture.
            _subtitle.OnTranscript(ev.Text, true, ev.Language);

            long token = Interlocked.Increment(ref _translationSeq);
            string text = ev.Text;
            string src = ev.Language;
            RunTranslation(text, src, target, token);
        }

        private void RunTranslation(string text, string src, string target, long token)
        {
            // Off the main thread: native ORT inference is tens-to-hundreds of ms.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string translation = TranslateNative(text, src, target);
                if (string.IsNullOrEmpty(translation)) return; // degraded: original only
                Enqueue(() =>
                {
                    // Discard a stale result superseded by a newer final.
                    if (Interlocked.Read(ref _translationSeq) != token) return;
                    if (_subtitle == null) return;
                    _subtitle.OnTranscript(text, true, src, null, translation, target);
                });
            });
        }

        // --- native / editor translation -----------------------------------------

        private string TranslateNative(string text, string src, string target)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                EnsureNative();
                return _native?.Call<string>("translate", text, src, target);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TranslateBridge] native translate failed: {ex.Message}");
                return null; // honest degraded
            }
#else
            // Editor: a marker translation so the subtitle path is exercised without a
            // plugin/model. Not a real translation — never shipped (guarded by define).
            return $"[{src}→{target}] {text}";
#endif
        }

        private void TickIdleRelease()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _native?.Call("maybeReleaseIdle"); } catch { /* ignore */ }
#endif
        }

        private void ReleaseNative()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _native?.Call("release"); } catch { /* ignore */ }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void EnsureNative()
        {
            if (_native != null) return;
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var ctx = activity.Call<AndroidJavaObject>("getApplicationContext");
            using var extDir = ctx.Call<AndroidJavaObject>("getExternalFilesDir", (object)null);
            string filesDir = extDir != null
                ? extDir.Call<string>("getAbsolutePath")
                : ctx.Call<AndroidJavaObject>("getFilesDir").Call<string>("getAbsolutePath");
            string root = filesDir + "/" + _modelsRelativeRoot;
            _native = new AndroidJavaObject(
                "com.mlomega.xr.reflexvision.OfflineTranslatorBridge", root);
        }
#endif

        private void OnDestroy() => ReleaseNative();

        // --- main-thread marshalling ---------------------------------------------

        private void Enqueue(Action a) { lock (_queueLock) { _mainThreadQueue.Enqueue(a); } }

        private void DrainMainThread()
        {
            while (true)
            {
                Action work = null;
                lock (_queueLock) { if (_mainThreadQueue.Count > 0) work = _mainThreadQueue.Dequeue(); }
                if (work == null) break;
                try { work(); } catch (Exception ex) { Debug.LogError($"[TranslateBridge] {ex}"); }
            }
        }

        // --- test seams -----------------------------------------------------------

        /// <summary>EditMode: inject the collaborators without a scene.</summary>
        internal void ConfigureForTest(AsrBridge asr, SubtitleSkill subtitle, MLOmegaConfig config)
        {
            _asrBridge = asr;
            _subtitle = subtitle;
            _config = config;
        }
    }
}
