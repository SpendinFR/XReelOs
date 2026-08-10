// MLOmega V19 â€” E33
// DeviceCommandHandler: executes `device_command` messages the PC IntentRouter
// pushes over the same reliable DataChannel as UIIntents (Â§4). One execution path
// for BOTH voice (PC router) and the on-glasses menu (MenuPanel emits the same
// command locally) â€” nothing here is voice- or menu-specific.
//
// Actions:
//   * set_ui_mode {hide_all|minimal|normal|freeguy} -> UIIntentBroker.SetDensity
//     (hide_all leaves only the standalone StatusBar + privacy, Â§13.2-1);
//   * privacy_pause                                  -> StatusBar.PrivacyPaused toggle;
//   * open_app {maps|youtube|package,...}            -> Kotlin AppLauncher bridge;
//   * open_menu                                      -> raises MenuRequested (MenuPanel);
//   * replay {time}                                  -> raises ReplayRequested.
//
// Each executed command raises CommandExecuted so the app can send a UIReceipt
// (delivered) back to the PC. Lives in the UI assembly (which references Transport,
// Scene and Contracts) â€” a Transport->UI dependency would be a cycle.
using System;
using System.Collections;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Transport;
using MLOmega.XR.Core;
using MLOmega.XR.UI.Components;
using Newtonsoft.Json;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>A parsed device_command message (contract-lite, PC->device Â§4).</summary>
    public sealed class DeviceCommand
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("ui_mode")] public string UiMode { get; set; }
        [JsonProperty("app")] public string App { get; set; }
        [JsonProperty("destination")] public string Destination { get; set; }
        [JsonProperty("query")] public string Query { get; set; }
        [JsonProperty("package")] public string Package { get; set; }
        [JsonProperty("time")] public string Time { get; set; }
        /// <summary>E48-A: on/off for toggle commands (translate_live). Null = flip current.</summary>
        [JsonProperty("on")] public bool? On { get; set; }
        /// <summary>E58: the new wake word for the set_wake_word command (PC push, no rebuild).</summary>
        [JsonProperty("word")] public string Word { get; set; }
        [JsonProperty("command_id")] public string CommandId { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("source_language")] public string SourceLanguage { get; set; }
        [JsonProperty("target_language")] public string TargetLanguage { get; set; }
        [JsonProperty("feature")] public string Feature { get; set; }

        public static bool IsDeviceCommand(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            return json.IndexOf("\"device_command\"", StringComparison.Ordinal) >= 0;
        }
    }

    public sealed class DeviceCommandHandler : MonoBehaviour
    {
        [SerializeField] private UIIntentBroker _broker;
        [SerializeField] private StatusBar _statusBar;
        [SerializeField] private AppLauncherBridge _appLauncher;
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private XrSessionController _session;
        [SerializeField] private AugmentedRealityFeatureRegistry _augmentedReality;
        [SerializeField] private LocalIntentSource _localIntents;
        [SerializeField] private MonoBehaviour _xrealSpatial;

        private IXrealSpatialProvider SpatialProvider =>
            _xrealSpatial as IXrealSpatialProvider;
        public IReadOnlyList<WorldMapSelection> AvailableWorldMaps =>
            SpatialProvider?.AvailableWorldMaps ??
            Array.Empty<WorldMapSelection>();

        /// <summary>Raised when a "menu" command arrives (MenuPanel opens the panel).</summary>
        public event Action MenuRequested;

        /// <summary>
        /// E48-A: raised for "translate_live" (null = flip current state). The Reflex
        /// TranslateBridge subscribes — UI cannot reference the Reflex assembly
        /// (Reflex already references UI; a typed field would be a cycle).
        /// </summary>
        public event Action<bool?> TranslateLiveRequested;
        /// <summary>One-shot OCR text translation, executed by the same offline Reflex model.</summary>
        public event Action<string, string, string> TranslateTextRequested;

        /// <summary>
        /// E58: raised for "set_wake_word" — the PC pushes the owner-chosen wake word
        /// (detected in the French ASR transcript, changeable without an APK rebuild).
        /// AsrBridge (Reflex) subscribes; UI cannot reference Reflex (cycle).
        /// </summary>
        public event Action<string> SetWakeWordRequested;

        /// <summary>Raised when a "replay {time}" command arrives (VirtualScreen replay).</summary>
        public event Action<string> ReplayRequested;

        /// <summary>Raised for every executed command with its (action, ok) result.</summary>
        public event Action<string, bool> CommandExecuted;
        public event Action<bool> PrivacyPauseChanged;

        private void Awake()
        {
            if (_broker == null) _broker = FindAnyObjectByType<UIIntentBroker>();
            if (_statusBar == null) _statusBar = FindAnyObjectByType<StatusBar>();
            if (_appLauncher == null) _appLauncher = FindAnyObjectByType<AppLauncherBridge>();
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_session == null) _session = FindAnyObjectByType<XrSessionController>();
            if (_augmentedReality == null)
                _augmentedReality = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
            if (_augmentedReality == null)
                _augmentedReality = gameObject.AddComponent<AugmentedRealityFeatureRegistry>();
            if (_localIntents == null)
                _localIntents = FindAnyObjectByType<LocalIntentSource>();
            if (_xrealSpatial == null)
            {
                foreach (MonoBehaviour behaviour in
                    FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (behaviour is IXrealSpatialProvider)
                    {
                        _xrealSpatial = behaviour;
                        break;
                    }
                }
            }
        }

        private void OnEnable()
        {
            if (_transport != null) _transport.MessageReceived += OnTransportMessage;
        }

        private void OnDisable()
        {
            if (_transport != null) _transport.MessageReceived -= OnTransportMessage;
        }

        private void OnTransportMessage(string json) => TryHandleRaw(json);

        /// <summary>Parse and execute a raw DataChannel message if it is a device_command.
        /// Returns true when it was a device command (handled), false otherwise (so the
        /// caller can route it as a normal UIIntent). Never throws.</summary>
        public bool TryHandleRaw(string json)
        {
            if (!DeviceCommand.IsDeviceCommand(json)) return false;
            DeviceCommand cmd;
            try { cmd = ContractJson.Deserialize<DeviceCommand>(json); }
            catch (Exception ex) { Debug.LogWarning($"[DeviceCommand] bad json: {ex.Message}"); return true; }
            if (cmd != null)
            {
                bool ok = Execute(cmd);
                SendCommandResult(cmd, ok);
            }
            return true;
        }

        private void SendCommandResult(DeviceCommand cmd, bool ok)
        {
            if (_transport == null || cmd == null) return;
            string json = ContractJson.Serialize(new
            {
                type = "device_command_result",
                command_id = cmd.CommandId,
                action = cmd.Action,
                ok,
            });
            _transport.SendContractMessage(json);
        }

        /// <summary>Execute a parsed device command. Idempotent and null-safe.</summary>
        public bool Execute(DeviceCommand cmd)
        {
            if (cmd == null) return false;
            bool ok;
            switch ((cmd.Action ?? string.Empty).ToLowerInvariant())
            {
                case "set_ui_mode":
                    ok = SetUiMode(cmd.UiMode, cmd.On);
                    break;
                case "privacy_pause":
                    ok = PrivacyPause();
                    break;
                case "open_app":
                    ok = OpenApp(cmd);
                    break;
                case "name_indoor_place":
                    ok = SpatialProvider != null &&
                        SpatialProvider.NameCurrentIndoorPlace(
                            string.IsNullOrWhiteSpace(cmd.Destination)
                                ? cmd.Text
                                : cmd.Destination);
                    break;
                case "import_world_map":
                    ok = SpatialProvider != null &&
                        SpatialProvider.ImportAnchoredWorld();
                    break;
                case "toggle_world_map":
                {
                    WorldMapSelection selected = null;
                    foreach (WorldMapSelection map in AvailableWorldMaps)
                        if (string.Equals(
                                map.mapId,
                                cmd.Feature,
                                StringComparison.Ordinal))
                        {
                            selected = map;
                            break;
                        }
                    ok = selected != null &&
                        SpatialProvider.SetWorldMapActive(
                            selected.mapId,
                            cmd.On ?? !selected.active);
                    break;
                }
                case "remove_world_map":
                    ok = SpatialProvider != null &&
                        SpatialProvider.RemoveInstalledWorldMap(cmd.Feature);
                    break;
                case "open_menu":
                    MenuRequested?.Invoke();
                    ok = true;
                    break;
                case "replay":
                    ReplayRequested?.Invoke(cmd.Time);
                    ok = true;
                    break;
                case "translate_live":
                    ok = TranslateLive(cmd.On);
                    break;
                case "translate_text":
                    ok = TranslateText(cmd.Text, cmd.SourceLanguage, cmd.TargetLanguage);
                    break;
                case "set_wake_word":
                    ok = SetWakeWord(cmd.Word);
                    break;
                case "set_augmented_feature":
                    ok = SetAugmentedFeature(cmd.Feature, cmd.On);
                    break;
                default:
                    Debug.LogWarning($"[DeviceCommand] unknown action: {cmd.Action}");
                    ok = false;
                    break;
            }
            CommandExecuted?.Invoke(cmd.Action, ok);
            EmitCommandFeedback(cmd, ok);
            return ok;
        }

        /// <summary>Menu-only entry: actions owned by BrainLive/IntentRouter travel
        /// upstream; truly local actions keep the shared Execute path.</summary>
        public bool ExecuteFromMenu(DeviceCommand cmd)
        {
            if (cmd == null) return false;
            string action = (cmd.Action ?? string.Empty).ToLowerInvariant();
            switch (action)
            {
                case "ask_memory_prompt":
                case "owner_enroll":
                case "replay":
                case "sherlock_toggle":
                case "virtual_screen":
                case "paid_mode":
                case "local_mode":
                    return _transport != null && _transport.SendContractMessage(
                        ContractJson.Serialize(new { type = "device_intent", action, time = cmd.Time }));
                default:
                    return Execute(cmd);
            }
        }

        private bool SetUiMode(string uiMode, bool? requested)
        {
            string normalised = (uiMode ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty);
            bool anchored = normalised == "freeguyanchored";
            bool freeGuy = normalised == "freeguy" || anchored;
            bool enable = requested ?? true;
            UIDensityMode mode = freeGuy && !enable
                ? UIDensityMode.Normal
                : freeGuy
                    ? UIDensityMode.FreeGuy
                    : UIIntentBroker.ParseDensity(uiMode);
            if (_broker != null) _broker.SetDensity(mode);
            if (_statusBar != null)
            {
                _statusBar.UiMode = mode == UIDensityMode.Normal
                    ? "live"
                    : mode == UIDensityMode.FreeGuy ? "freeguy" : uiMode;
            }
            if (freeGuy)
                return EnsureAugmentedReality() != null &&
                    _augmentedReality.SetPreset(
                        anchored ? "freeguyanchored" : "freeguy",
                        enable);
            return true;
        }

        private AugmentedRealityFeatureRegistry EnsureAugmentedReality()
        {
            if (_augmentedReality == null)
                _augmentedReality = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
            if (_augmentedReality == null)
                _augmentedReality = gameObject.AddComponent<AugmentedRealityFeatureRegistry>();
            return _augmentedReality;
        }

        private bool SetAugmentedFeature(string feature, bool? requested)
        {
            AugmentedRealityFeatureRegistry registry = EnsureAugmentedReality();
            string id = (feature ?? string.Empty).Trim().ToLowerInvariant();
            bool known = Array.Exists(
                AugmentedRealityFeatureRegistry.FeatureIds,
                candidate => string.Equals(candidate, id, StringComparison.Ordinal));
            if (registry == null || !known) return false;

            // A spoken explicit activation means "make it run", not merely
            // "preselect it for later". Menu toggles use null and preserve the
            // master-as-kill-switch/ARMÉ workflow.
            if (requested == true && !registry.MasterEnabled)
                registry.SetFeature(AugmentedRealityFeatureRegistry.Master, true);
            return registry.SetFeature(id, requested);
        }

        private void EmitCommandFeedback(DeviceCommand cmd, bool ok)
        {
            if (_localIntents == null || cmd == null) return;
            string action = (cmd.Action ?? string.Empty).Trim().ToLowerInvariant();
            string text = null;
            switch (action)
            {
                case "set_ui_mode":
                    string mode = (cmd.UiMode ?? "normal").Trim();
                    bool on = cmd.On ?? true;
                    text = ok
                        ? string.Equals(
                            mode.Replace(" ", string.Empty),
                            "freeguy",
                            StringComparison.OrdinalIgnoreCase)
                            ? $"Mode FreeGuy {(on ? "activé" : "désactivé")}."
                            : $"Mode {mode} activé."
                        : $"Impossible d'activer le mode {mode}.";
                    break;
                case "set_augmented_feature":
                    string label = AugmentedRealityFeatureRegistry.DisplayName(cmd.Feature);
                    bool selected = _augmentedReality != null &&
                        _augmentedReality.IsSelected(cmd.Feature);
                    text = ok
                        ? $"{label} {(selected ? "activé" : "désactivé")}."
                        : $"{label} indisponible.";
                    break;
                case "translate_live":
                    text = ok ? "Traduction directe mise à jour." : "Traduction indisponible.";
                    break;
                case "privacy_pause":
                    text = ok ? "Mode privé mis à jour." : "Mode privé indisponible.";
                    break;
                case "import_world_map":
                    text = ok
                        ? "Choisis le monde ancré créé dans World Atelier."
                        : "Import du monde ancré indisponible.";
                    break;
            }
            if (string.IsNullOrEmpty(text)) return;

            _localIntents.Emit(new UIIntent
            {
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "ul_voice_command_status",
                Producer = "ultralive",
                Component = "context_card",
                TruthLevel = "observed",
                Confidence = 1.0,
                Priority = 1.0,
                TtlMs = 3200,
                Content = new Dictionary<string, object>
                {
                    { "title", "VIKI" },
                    { "text", text },
                },
                UiHint = new Dictionary<string, object>
                {
                    { "focus", true },
                    { "transient", true },
                },
                Anchor = new Dictionary<string, object>
                {
                    { "type", "head_locked" },
                    { "side", "right" },
                },
                EvidenceRefs = new List<string>(),
            });
        }

        // E48-A: toggle the live on-device translation reflex (« traduis en direct » /
        // « stop traduction » voice command, or the menu entry — same command path).
        // `on` null (the menu toggle) flips; the subscribed TranslateBridge applies it
        // and updates the StatusBar. No subscriber (no reflex layer) = honest failure.
        private bool TranslateLive(bool? on)
        {
            if (TranslateLiveRequested == null) return false;
            TranslateLiveRequested.Invoke(on);
            return true;
        }

        private bool TranslateText(string text, string sourceLanguage, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text) || TranslateTextRequested == null) return false;
            TranslateTextRequested.Invoke(text.Trim(), sourceLanguage, targetLanguage);
            return true;
        }

        // E58: apply a new wake word pushed by the PC (owner-chosen, no rebuild). The
        // subscribed AsrBridge re-points the on-device ASR-transcript matcher. Empty =
        // ignored (never silently disable the wake word).
        private bool SetWakeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || SetWakeWordRequested == null) return false;
            SetWakeWordRequested.Invoke(word.Trim());
            return true;
        }

        private bool PrivacyPause()
        {
            if (_statusBar == null) return false;
            bool paused = !_statusBar.PrivacyPaused;
            // Notify before WebRTC disposal so the PC watchdog does not mistake an
            // explicit privacy pause for an abandoned session.
            if (paused)
                _transport?.SendContractMessage(ContractJson.Serialize(
                    new { type = "privacy_state", paused = true }));
            bool cameraOk = _session == null || _session.SetEyeCapturePaused(paused);
            if (paused) StartCoroutine(StopTransportAfterPrivacyNotice());
            else _transport?.StartTransport();
            _statusBar.PrivacyPaused = paused;
            _statusBar.CameraOn = !paused;
            _statusBar.MicOn = !paused;
            PrivacyPauseChanged?.Invoke(paused);
            return cameraOk;
        }

        private IEnumerator StopTransportAfterPrivacyNotice()
        {
            // Give the reliable DataChannel one player-loop turn to queue the
            // privacy_state and command acknowledgement before native disposal.
            yield return null;
            _transport?.StopTransport();
        }

        private void OnGUI()
        {
            // Once camera+gesture+mic are genuinely released, a sensor-based command
            // cannot resume them. Keep one explicit local touch escape hatch.
            if (_statusBar == null || !_statusBar.PrivacyPaused) return;
            if (GUI.Button(new Rect(16, 16, 330, 56), "Reprendre caméra et micro"))
                PrivacyPause();
        }

        private bool OpenApp(DeviceCommand cmd)
        {
            if (_appLauncher == null)
            {
                Debug.Log($"[DeviceCommand] (no launcher) open_app {cmd.App} {cmd.Destination}{cmd.Query}{cmd.Package}");
                return false;
            }
            switch ((cmd.App ?? string.Empty).ToLowerInvariant())
            {
                case "maps":
                    if (
                        SpatialProvider != null &&
                        _augmentedReality != null &&
                        _augmentedReality.IsEffective(
                            AugmentedRealityFeatureRegistry.StreetNavigation) &&
                        SpatialProvider.StartNavigation(cmd.Destination))
                        return true;
                    return _appLauncher.OpenMaps(cmd.Destination);
                case "youtube": return _appLauncher.OpenYouTube(cmd.Query);
                default: return _appLauncher.OpenPackage(cmd.Package);
            }
        }
    }
}
