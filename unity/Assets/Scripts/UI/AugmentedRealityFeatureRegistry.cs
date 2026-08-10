using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using MLOmega.XR.Transport;
using MLOmega.XR.UI.Components;
using Newtonsoft.Json;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Device-side preferences for optional augmented-reality capabilities.
    ///
    /// Every key defaults OFF. Enabling a key only publishes a bounded preference
    /// contract to the isolated PC service; this class never starts a model,
    /// camera, worker or network client of its own.
    /// </summary>
    public sealed class AugmentedRealityFeatureRegistry : MonoBehaviour
    {
        public const string Master = "master";
        public const string ObjectMenus = "object_menus";
        public const string ActionRecognition = "action_recognition";
        public const string SemanticSound = "semantic_sound";
        public const string ContextualKnowledge = "contextual_knowledge";
        public const string EnhancedZoom = "enhanced_zoom";
        public const string ArMeasurement = "ar_measurement";
        public const string StreetNavigation = "street_navigation";
        public const string WorldLabels = "world_labels";
        public const string PersistentAnchors = "persistent_anchors";
        public const string DepthOcclusion = "depth_occlusion";
        public const string WorldStyling = "world_styling";
        public const string TrajectoryForecast = "trajectory_forecast";
        public const string SpatialKeyboard = "spatial_keyboard";
        public const string EventVision = "event_vision";
        public const string BallisticPreview = "ballistic_preview";
        public const string RadioField = "radio_field";
        public const string ConsentedPeople = "consented_people";
        public const string PulseAura = "pulse_aura";
        public const string AutomaticWorldFx = "automatic_world_fx";
        public const string WorldText = "world_text";
        public const string IndoorNavigation = "indoor_navigation";
        public const string Planetarium = "planetarium";
        public const string WeatherContext = "weather_context";
        public const string LegalContext = "legal_context";

        private const string PreferencePrefix = "mlomega.augmented_reality.";

        public static readonly string[] FeatureIds =
        {
            ObjectMenus,
            ActionRecognition,
            SemanticSound,
            ContextualKnowledge,
            EnhancedZoom,
            ArMeasurement,
            StreetNavigation,
            WorldLabels,
            PersistentAnchors,
            DepthOcclusion,
            WorldStyling,
            TrajectoryForecast,
            SpatialKeyboard,
            EventVision,
            BallisticPreview,
            RadioField,
            ConsentedPeople,
            PulseAura,
            AutomaticWorldFx,
            WorldText,
            IndoorNavigation,
            Planetarium,
            WeatherContext,
            LegalContext,
        };

        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private StatusBar _statusBar;
        [SerializeField] private AugmentedRealityCapabilityProbe _probe;
        [SerializeField] private bool _persistPreferences = true;

        private readonly Dictionary<string, bool> _selected =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly HashSet<string> _active =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _localActive =
            new HashSet<string>(StringComparer.Ordinal);

        public event Action<string, bool> FeatureChanged;
        public event Action<string, string> ServiceStatusChanged;

        public bool MasterEnabled => IsSelected(Master);
        public string LastServiceStatus { get; private set; } = "disabled";
        public string LastServiceDetail { get; private set; } = string.Empty;

        /// <summary>
        /// Atomically apply a named visual preset. A preset only selects existing
        /// switches; it never bypasses capability checks or starts another provider.
        /// Menu and voice commands both use this path.
        /// </summary>
        public bool SetPreset(string preset, bool enabled)
        {
            string id = (preset ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty);
            bool anchored = id == "freeguyanchored";
            if (id != "freeguy" && !anchored) return false;

            var changed = new List<KeyValuePair<string, bool>>();
            if (enabled && StoreSelection(Master, true))
                changed.Add(new KeyValuePair<string, bool>(Master, true));
            bool styling =
                enabled ||
                (anchored
                    ? IsSelected(AutomaticWorldFx)
                    : IsSelected(PersistentAnchors));
            if (StoreSelection(WorldStyling, styling))
                changed.Add(new KeyValuePair<string, bool>(
                    WorldStyling, styling));
            string primary =
                anchored ? PersistentAnchors : AutomaticWorldFx;
            if (StoreSelection(primary, enabled))
                changed.Add(new KeyValuePair<string, bool>(primary, enabled));
            if (anchored && enabled && StoreSelection(DepthOcclusion, true))
                changed.Add(new KeyValuePair<string, bool>(
                    DepthOcclusion, true));

            if (enabled && changed.Exists(pair => pair.Key == Master))
            {
                LastServiceStatus = "pending";
                LastServiceDetail = string.Empty;
                EnsureProbe().Probe();
            }
            if (changed.Count == 0) return true;

            SavePreferences();
            ApplyStatusBar();
            foreach (KeyValuePair<string, bool> pair in changed)
                FeatureChanged?.Invoke(pair.Key, pair.Value);
            PublishPreferences();
            return true;
        }

        private void Awake()
        {
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_statusBar == null) _statusBar = FindAnyObjectByType<StatusBar>();
            LoadPreferences();
            ApplyStatusBar();
        }

        private void OnEnable()
        {
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_transport == null) return;
            _transport.StateChanged += OnTransportStateChanged;
            _transport.MessageReceived += OnTransportMessage;
        }

        private void OnDisable()
        {
            if (_transport == null) return;
            _transport.StateChanged -= OnTransportStateChanged;
            _transport.MessageReceived -= OnTransportMessage;
        }

        public bool IsSelected(string feature)
        {
            string id = Normalise(feature);
            return id != null && _selected.TryGetValue(id, out bool value) && value;
        }

        public bool IsEffective(string feature) =>
            MasterEnabled && feature != Master && IsSelected(feature);

        public bool IsActive(string feature)
        {
            string id = Normalise(feature);
            return id != null &&
                id != Master &&
                MasterEnabled &&
                IsSelected(id) &&
                (_active.Contains(id) || _localActive.Contains(id));
        }

        /// <summary>
        /// Advertise one capability proven by the active device XR provider.
        /// This never selects the feature: every switch remains explicit opt-in.
        /// PhoneOnly has no local spatial provider and therefore stays unchanged.
        /// </summary>
        public bool SetLocalCapability(string feature, bool available)
        {
            string id = Normalise(feature);
            if (id == null || id == Master) return false;
            bool changed = available
                ? _localActive.Add(id)
                : _localActive.Remove(id);
            if (changed)
            {
                ApplyStatusBar();
                ServiceStatusChanged?.Invoke(
                    LastServiceStatus,
                    available
                        ? $"local XR provider ready: {id}"
                        : $"local XR provider unavailable: {id}");
            }
            return changed;
        }

        public bool IsLocalCapabilityAvailable(string feature)
        {
            string id = Normalise(feature);
            return id != null && id != Master && _localActive.Contains(id);
        }

        public string DisplayState(string feature)
        {
            string id = Normalise(feature);
            if (id == null || !IsSelected(id)) return "OFF";
            if (id == Master)
                return LastServiceStatus == "ready" || _localActive.Count > 0
                    ? "ON"
                    : "ATTENTE";
            if (!MasterEnabled) return "ARMÉ";
            return IsActive(id) ? "ON" : "ATTENTE";
        }

        public bool SetFeature(string feature, bool? requested)
        {
            string id = Normalise(feature);
            if (id == null) return false;
            bool next = requested ?? !IsSelected(id);
            bool changed = StoreSelection(id, next);
            if (changed) SavePreferences();
            if (id == Master)
            {
                LastServiceStatus = next ? "pending" : "disabled";
                LastServiceDetail = string.Empty;
                if (next) EnsureProbe().Probe();
            }
            ApplyStatusBar();
            if (changed) FeatureChanged?.Invoke(id, next);
            PublishPreferences();
            return true;
        }

        public static string DisplayName(string feature)
        {
            switch (Normalise(feature))
            {
                case Master: return "Réalité augmentée";
                case ObjectMenus: return "Menus d'objets";
                case ActionRecognition: return "Reconnaissance d'actions";
                case SemanticSound: return "Sons sémantiques";
                case ContextualKnowledge: return "Connaissances contextuelles";
                case EnhancedZoom: return "Zoom amélioré";
                case ArMeasurement: return "Mètre AR";
                case StreetNavigation: return "Navigation extérieure";
                case WorldLabels: return "Labels du monde";
                case PersistentAnchors: return "Ancres persistantes";
                case DepthOcclusion: return "Occlusion";
                case WorldStyling: return "Style FreeGuy";
                case TrajectoryForecast: return "Trajectoires de foule";
                case SpatialKeyboard: return "Clavier spatial";
                case EventVision: return "Vision événementielle";
                case BallisticPreview: return "Trajectoire ludique";
                case RadioField: return "Champs Wi-Fi et Bluetooth";
                case ConsentedPeople: return "Profils consentis";
                case PulseAura: return "Aura physiologique";
                case AutomaticWorldFx: return "Effets monde automatiques";
                case WorldText: return "Texte du monde";
                case IndoorNavigation: return "Navigation intérieure";
                case Planetarium: return "Planétarium";
                case WeatherContext: return "Météo contextuelle";
                case LegalContext: return "Assistance contextuelle";
                default: return "Fonction";
            }
        }

        public Dictionary<string, bool> Snapshot()
        {
            var snapshot = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string id in FeatureIds) snapshot[id] = IsSelected(id);
            return snapshot;
        }

        public bool PublishPreferences()
        {
            if (_transport == null) return false;
            var payload = new
            {
                type = "augmented_reality_preferences",
                schema_version = 1,
                master_enabled = MasterEnabled,
                features = Snapshot(),
                probe = MasterEnabled ? EnsureProbe().Probe() : null,
                sent_at_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            return _transport.SendContractMessage(ContractJson.Serialize(payload));
        }

        private void LoadPreferences()
        {
            _selected.Clear();
            _selected[Master] = ReadPreference(Master);
            foreach (string id in FeatureIds) _selected[id] = ReadPreference(id);
        }

        private bool ReadPreference(string id) =>
            _persistPreferences && PlayerPrefs.HasKey(PreferencePrefix + id) &&
            PlayerPrefs.GetInt(PreferencePrefix + id, 0) == 1;

        private bool StoreSelection(string id, bool value)
        {
            if (_selected.TryGetValue(id, out bool previous) && previous == value)
                return false;
            _selected[id] = value;
            if (_persistPreferences)
                PlayerPrefs.SetInt(PreferencePrefix + id, value ? 1 : 0);
            return true;
        }

        private void SavePreferences()
        {
            if (_persistPreferences) PlayerPrefs.Save();
        }

        private void OnTransportStateChanged(LiveTransportState state, string detail)
        {
            if (state == LiveTransportState.Connected) PublishPreferences();
        }

        private void OnTransportMessage(string json)
        {
            if (string.IsNullOrEmpty(json) ||
                json.IndexOf("\"augmented_reality_status\"", StringComparison.Ordinal) < 0)
                return;
            try
            {
                var status = ContractJson.Deserialize<AugmentedRealityStatusMessage>(json);
                if (status == null) return;
                LastServiceStatus = string.IsNullOrEmpty(status.Status)
                    ? "unavailable"
                    : status.Status;
                LastServiceDetail = status.Detail ?? string.Empty;
                _active.Clear();
                if (status.ActiveFeatures != null)
                {
                    foreach (string feature in status.ActiveFeatures)
                    {
                        string id = Normalise(feature);
                        if (id != null && id != Master) _active.Add(id);
                    }
                }
                ApplyStatusBar();
                ServiceStatusChanged?.Invoke(LastServiceStatus, LastServiceDetail);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AugmentedReality] invalid status: {ex.Message}");
            }
        }

        private AugmentedRealityCapabilityProbe EnsureProbe()
        {
            if (_probe == null) _probe = GetComponent<AugmentedRealityCapabilityProbe>();
            if (_probe == null) _probe = gameObject.AddComponent<AugmentedRealityCapabilityProbe>();
            return _probe;
        }

        private void ApplyStatusBar()
        {
            if (_statusBar == null) _statusBar = FindAnyObjectByType<StatusBar>();
            if (_statusBar == null) return;
            _statusBar.AugmentedRealityEnabled = MasterEnabled;
            _statusBar.AugmentedRealityStatus = LastServiceStatus;
        }

        private static string Normalise(string feature)
        {
            string id = (feature ?? string.Empty).Trim().ToLowerInvariant();
            if (id == Master) return Master;
            foreach (string known in FeatureIds)
                if (id == known) return known;
            return null;
        }

        [Serializable]
        private sealed class AugmentedRealityStatusMessage
        {
            [JsonProperty("status")] public string Status { get; set; }
            [JsonProperty("detail")] public string Detail { get; set; }
            [JsonProperty("active_features")] public List<string> ActiveFeatures { get; set; }
        }
    }
}
