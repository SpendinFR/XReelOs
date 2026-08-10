using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using MLOmega.XR.Transport;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;
#if XREAL_SDK_PRESENT
using Unity.Collections;
using Unity.XR.CoreUtils;
using Unity.XR.XREAL;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;
#endif
#if XREAL_SDK_PRESENT && XR_HANDS
using UnityEngine.XR.Hands;
#endif

namespace MLOmega.XR.UI
{
    /// <summary>
    /// XREAL-only producer for the calibrated World Canvas.
    ///
    /// XREAL SDK 3.1 exposes its planes/depth mesh through AR Foundation. This
    /// component creates those managers by reflection so a clean PhoneOnly
    /// checkout keeps no AR Foundation dependency. Pixel detections are promoted
    /// to 3D only after a ray hits a real XREAL mesh collider while the head pose
    /// is tracked. No hit means no world intent.
    /// </summary>
    public sealed class XrealSpatialProvider :
        MonoBehaviour,
        IXrealSpatialProvider,
        IWorldCreatorSpatialProvider
    {
        private const string CalibrationId = "xreal-eye-tracking-local-v1";
        private const float SceneCadenceSeconds = 0.20f;
        private const float CapabilityCadenceSeconds = 0.75f;

        [SerializeField] private AugmentedRealityFeatureRegistry _features;
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private LocalIntentSource _intents;
        [SerializeField] private UIIntentBroker _broker;
        [SerializeField] private PosePublisher _pose;
        [SerializeField] private SessionPairing _pairing;
        [SerializeField] private Camera _camera;
        [SerializeField] private Shader _depthOcclusionShader;
        [SerializeField] private Shader _freeGuyMeshShader;
        [SerializeField] private bool _creatorMode;

        private readonly Dictionary<string, TrackHistory> _tracks =
            new Dictionary<string, TrackHistory>(StringComparer.Ordinal);
        private readonly List<RadioSample> _radioSamples = new List<RadioSample>();
        private Component _meshManager;
        private Component _anchorManager;
        private GameObject _meshPrefab;
        private GameObject _arSession;
        private float _nextCapabilityProbe;
        private float _nextSceneAt;
        private float _nextRadioAt;
        private float _nextManagerRetryAt;
        private bool _managersRequested;
        private bool _creatorSpatialRequested;
        private bool _managerStateKnown;
        private bool _managerStateEnabled;
        private Component _managerStateMesh;
        private GameObject _managerStateSession;
        private Vector3? _measureStart;
        private KeyboardPlacement _keyboard;
        private Material _depthOcclusionMaterial;
        private Material _freeGuyMeshMaterial;
        private bool _anchorsLoadStarted;
        private string _anchorMappingDirectory;
        private readonly HashSet<string> _emittedAnchorIds =
            new HashSet<string>(StringComparer.Ordinal);
#if XREAL_SDK_PRESENT
        private readonly Dictionary<string, LoadedWorldAnchor>
            _loadedWorldAnchors =
                new Dictionary<string, LoadedWorldAnchor>(
                    StringComparer.Ordinal);
        private int _worldAnchorBatchExpected;
        private int _worldAnchorBatchCompleted;
#endif
        private Vector3? _ballisticTarget;
        private Vector3 _lastHandPosition;
        private float _lastHandAt;
        private float _nextBallisticAt;
        private GeoDestination _navigationDestination;
        private float _nextNavigationAt;
        private bool _navigationStarting;
        private bool _routeRequestInFlight;
        private float _nextRouteRequestAt;
        private bool _opticalClearConfigured;
        private float _nextOpticalClearAttemptAt;
        private WorldMapStore _worldMap;
        private WorldMapLibrary _worldMapLibrary;
        private WorldMapDraftLibrary _worldMapDrafts;
        private WorldMapDocumentExchange _worldMapExchange;
        private IndoorLiveMapStore _indoorMap;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _indoorFingerprint;
#endif
        private float _nextIndoorSampleAt;
        private readonly HashSet<string> _automaticWorldIntentIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _dynamicWorldExpiry =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private float _nextWorldTextLocationAt;
        private bool _worldTextLocationStarting;

        public bool DepthReady { get; private set; }
        public string LastProviderError { get; private set; } = string.Empty;
        public int ProjectedTrackCount => _tracks.Count;
        public bool CreatorReady =>
            _creatorMode && DepthReady && AnchorProviderReady();
        public WorldMapStore CreatorMap => _worldMap;
        public IReadOnlyList<WorldMapSelection> CreatorMaps =>
            _worldMapDrafts?.List() ?? Array.Empty<WorldMapSelection>();
        public IReadOnlyList<WorldMapSelection> AvailableWorldMaps =>
            _worldMapLibrary?.Selections ?? Array.Empty<WorldMapSelection>();
        public event Action<string, bool, string> CreatorOperationCompleted;

        /// <summary>
        /// Project a top-left-origin normalised image point onto the proven XREAL
        /// spatial mesh. Used by short-lived OCR lenses; no mesh hit means callers
        /// must remain head-locked rather than inventing a world pose.
        /// </summary>
        public bool TryProjectImagePoint(Vector2 imagePoint, out Vector3 worldPoint)
        {
            worldPoint = default;
            Vector2 viewport = new Vector2(
                Mathf.Clamp01(imagePoint.x),
                Mathf.Clamp01(1f - imagePoint.y));
            if (!TryDepthHit(viewport, out RaycastHit hit)) return false;
            worldPoint = hit.point;
            return true;
        }

        private static bool CompiledForXreal
        {
            get
            {
#if XREAL_SDK_PRESENT
                return true;
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            if (_features == null)
                _features = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
            if (_transport == null)
                _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_intents == null) _intents = FindAnyObjectByType<LocalIntentSource>();
            if (_broker == null) _broker = FindAnyObjectByType<UIIntentBroker>();
            if (_pose == null) _pose = FindAnyObjectByType<PosePublisher>();
            if (_pairing == null) _pairing = FindAnyObjectByType<SessionPairing>();
            if (_camera == null) _camera = Camera.main;
            string worldMapDirectory = System.IO.Path.Combine(
                Application.persistentDataPath, "xreal-world-maps");
            _worldMap = new WorldMapStore(
                worldMapDirectory,
                CalibrationId);
            if (_creatorMode)
                _worldMapDrafts = new WorldMapDraftLibrary(
                    worldMapDirectory, CalibrationId);
            else
                _worldMapLibrary = new WorldMapLibrary(
                    System.IO.Path.Combine(worldMapDirectory, "library"));
            _worldMapExchange =
                GetComponent<WorldMapDocumentExchange>() ??
                gameObject.AddComponent<WorldMapDocumentExchange>();
            _indoorMap = new IndoorLiveMapStore(
                System.IO.Path.Combine(
                    Application.persistentDataPath, "xreal-indoor-maps"));
            if (_depthOcclusionShader != null)
                _depthOcclusionMaterial = new Material(_depthOcclusionShader);
            if (_freeGuyMeshShader != null)
                _freeGuyMeshMaterial = new Material(_freeGuyMeshShader);
        }

        private void OnEnable()
        {
            if (_transport != null) _transport.MessageReceived += OnTransportMessage;
            if (_features != null) _features.FeatureChanged += OnFeatureChanged;
            if (_worldMapExchange != null)
                _worldMapExchange.Imported += OnWorldMapImported;
        }

        private void OnDisable()
        {
            if (_transport != null) _transport.MessageReceived -= OnTransportMessage;
            if (_features != null) _features.FeatureChanged -= OnFeatureChanged;
            if (_worldMapExchange != null)
                _worldMapExchange.Imported -= OnWorldMapImported;
            WithdrawAutomaticWorldEffects();
            StopIndoorFingerprint();
            SetLocalCapabilities(false);
            SetManagersEnabled(false);
        }

        private void OnDestroy()
        {
            if (_depthOcclusionMaterial != null) Destroy(_depthOcclusionMaterial);
            if (_freeGuyMeshMaterial != null) Destroy(_freeGuyMeshMaterial);
            StopIndoorFingerprint();
        }

        private void Update()
        {
            if (!CompiledForXreal) return;
#if XREAL_SDK_PRESENT
            // The XREAL compositor owns a separate opaque back colour. Camera
            // alpha/clear flags cannot disable it. Retry until the real display
            // subsystem is running, then switch that layer off once.
            // HelloMR leaves the compositor back-colour policy untouched. The
            // isolated Atelier must preserve that hardware-proven baseline;
            // forcing EnableRenderBackColor(false) was an Atelier-only
            // divergence correlated with the persistent violet eye surface.
            if (!_creatorMode)
                EnsureOpticalSeeThrough();
#endif
            if (_creatorMode)
            {
                if (!_creatorSpatialRequested)
                {
                    DepthReady = false;
                    return;
                }
                const bool wantedByCreator = true;
                if (!_managersRequested &&
                    Time.unscaledTime >= _nextManagerRetryAt)
                    EnsureSpatialManagers();
                SetManagersEnabled(wantedByCreator);
                if (Time.unscaledTime >= _nextCapabilityProbe)
                {
                    _nextCapabilityProbe =
                        Time.unscaledTime + CapabilityCadenceSeconds;
                    DepthReady = PoseTracked() && HasReadableDepthMesh();
                    UpdateMeshRendering();
                }
                if (DepthReady && !_anchorsLoadStarted)
                    LoadPersistentAnchors();
                return;
            }
            if (_features == null) return;
            bool wanted = _features.MasterEnabled && AnySpatialFeatureSelected();
            if (wanted && !_managersRequested &&
                Time.unscaledTime >= _nextManagerRetryAt)
                EnsureSpatialManagers();
            SetManagersEnabled(wanted);

            if (Time.unscaledTime >= _nextCapabilityProbe)
            {
                _nextCapabilityProbe = Time.unscaledTime + CapabilityCadenceSeconds;
                DepthReady = wanted && PoseTracked() && HasReadableDepthMesh();
                SetLocalCapabilities(DepthReady);
                UpdateMeshRendering();
                if (DepthReady &&
                    _features.IsActive(AugmentedRealityFeatureRegistry.SpatialKeyboard) &&
                    _keyboard == null)
                {
                    TryPlaceKeyboard(new Vector2(0.5f, 0.58f));
                }
            }

            if (DepthReady &&
                _features.IsActive(AugmentedRealityFeatureRegistry.RadioField) &&
                Time.unscaledTime >= _nextRadioAt)
            {
                _nextRadioAt = Time.unscaledTime + 5f;
                SampleCurrentWifi();
            }
            if (
                _features.IsActive(AugmentedRealityFeatureRegistry.IndoorNavigation) &&
                Time.unscaledTime >= _nextIndoorSampleAt)
            {
                _nextIndoorSampleAt = Time.unscaledTime + 1f;
                SampleIndoorFingerprint();
            }
            if (DepthReady &&
                _features.IsActive(AugmentedRealityFeatureRegistry.PersistentAnchors) &&
                !_anchorsLoadStarted)
                LoadPersistentAnchors();
            if (DepthReady &&
                _features.IsActive(AugmentedRealityFeatureRegistry.BallisticPreview))
                UpdateBallisticPreview();
            if (
                _navigationDestination != null &&
                _features.IsActive(
                    AugmentedRealityFeatureRegistry.StreetNavigation) &&
                Time.unscaledTime >= _nextNavigationAt)
            {
                _nextNavigationAt = Time.unscaledTime + 2f;
                TickNavigation();
            }
            if (
                (
                    _features.IsActive(AugmentedRealityFeatureRegistry.WorldText) ||
                    _features.IsActive(AugmentedRealityFeatureRegistry.WeatherContext) ||
                    _features.IsActive(AugmentedRealityFeatureRegistry.Planetarium)
                ) &&
                Time.unscaledTime >= _nextWorldTextLocationAt)
            {
                _nextWorldTextLocationAt = Time.unscaledTime + 30f;
                PublishWorldTextLocation();
            }
        }

#if XREAL_SDK_PRESENT
        private void EnsureOpticalSeeThrough()
        {
            if (
                _opticalClearConfigured ||
                Time.unscaledTime < _nextOpticalClearAttemptAt)
                return;
            _nextOpticalClearAttemptAt = Time.unscaledTime + 0.5f;

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (XRDisplaySubsystem display in displays)
            {
                if (display == null || !display.running) continue;

                // XREAL's compositor can render its own opaque back colour
                // independently of Unity's Camera.backgroundColor. Disable it:
                // on optical glasses, unlit black pixels then reveal the real
                // world instead of the SDK's violet diagnostic clear.
                bool disabled = display.EnableRenderBackColor(false);
                if (!disabled) continue;
                _opticalClearConfigured = true;
                Debug.Log(
                    "[XrealSpatialProvider] Optical see-through active: " +
                    "XREAL compositor back colour disabled.");
                break;
            }
        }
#endif

        private bool AnySpatialFeatureSelected()
        {
            string[] ids =
            {
                AugmentedRealityFeatureRegistry.WorldLabels,
                AugmentedRealityFeatureRegistry.TrajectoryForecast,
                AugmentedRealityFeatureRegistry.EventVision,
                AugmentedRealityFeatureRegistry.ArMeasurement,
                AugmentedRealityFeatureRegistry.SpatialKeyboard,
                AugmentedRealityFeatureRegistry.RadioField,
                AugmentedRealityFeatureRegistry.PersistentAnchors,
                AugmentedRealityFeatureRegistry.DepthOcclusion,
                AugmentedRealityFeatureRegistry.WorldStyling,
                AugmentedRealityFeatureRegistry.BallisticPreview,
                AugmentedRealityFeatureRegistry.StreetNavigation,
                AugmentedRealityFeatureRegistry.AutomaticWorldFx,
                AugmentedRealityFeatureRegistry.WorldText,
                AugmentedRealityFeatureRegistry.IndoorNavigation,
                AugmentedRealityFeatureRegistry.Planetarium,
                AugmentedRealityFeatureRegistry.WeatherContext,
            };
            foreach (string id in ids)
                if (_features.IsSelected(id)) return true;
            return false;
        }

        private void OnFeatureChanged(string feature, bool enabled)
        {
            if (!enabled)
            {
                if (feature == AugmentedRealityFeatureRegistry.ArMeasurement)
                    _measureStart = null;
                if (feature == AugmentedRealityFeatureRegistry.SpatialKeyboard)
                    _keyboard = null;
                if (feature == AugmentedRealityFeatureRegistry.BallisticPreview)
                    _ballisticTarget = null;
                if (feature == AugmentedRealityFeatureRegistry.StreetNavigation)
                {
                    _navigationDestination = null;
                    _features?.SetLocalCapability(
                        AugmentedRealityFeatureRegistry.StreetNavigation, false);
                }
                if (feature == AugmentedRealityFeatureRegistry.IndoorNavigation)
                    StopIndoorFingerprint();
                if (
                    feature == AugmentedRealityFeatureRegistry.AutomaticWorldFx ||
                    feature == AugmentedRealityFeatureRegistry.Master)
                    WithdrawAutomaticWorldEffects();
            }
            else if (
                feature == AugmentedRealityFeatureRegistry.WorldText ||
                feature == AugmentedRealityFeatureRegistry.WeatherContext ||
                feature == AugmentedRealityFeatureRegistry.Planetarium)
            {
                _nextWorldTextLocationAt = 0f;
                EnsureWorldTextLocationAsync();
            }
            else if (feature == AugmentedRealityFeatureRegistry.IndoorNavigation)
            {
                _nextIndoorSampleAt = 0f;
                EnsureIndoorFingerprintBridge();
            }
            _nextCapabilityProbe = 0f;
            UpdateMeshRendering();
        }

        private void OnTransportMessage(string json)
        {
            if (
                !DepthReady ||
                string.IsNullOrEmpty(json) ||
                json.IndexOf("\"scene_delta\"", StringComparison.Ordinal) < 0 ||
                Time.unscaledTime < _nextSceneAt)
                return;
            _nextSceneAt = Time.unscaledTime + SceneCadenceSeconds;
            try
            {
                SceneDelta delta = ContractJson.Deserialize<SceneDelta>(json);
                if (delta?.Entities != null) ProjectSceneDelta(delta);
            }
            catch (Exception ex)
            {
                LastProviderError = "scene_delta:" + ex.GetType().Name;
            }
        }

        private void ProjectSceneDelta(SceneDelta delta)
        {
            if (
                !delta.FrameWidth.HasValue ||
                !delta.FrameHeight.HasValue ||
                delta.FrameWidth.Value <= 0 ||
                delta.FrameHeight.Value <= 0)
                return;
            foreach (Dictionary<string, object> entity in delta.Entities)
            {
                string trackId = ReadString(entity, "track_id");
                string label = ReadString(entity, "label");
                string kind = ReadString(entity, "kind");
                if (
                    string.IsNullOrWhiteSpace(trackId) ||
                    !TryViewportPoint(entity, delta, out Vector2 viewport) ||
                    !TryDepthHit(viewport, out RaycastHit hit))
                    continue;
                float confidence = Mathf.Clamp01(
                    (float)ReadNumber(entity, "confidence", 0.6));
                UpdateTrack(
                    trackId,
                    string.IsNullOrWhiteSpace(label) ? kind : label,
                    kind,
                    hit.point,
                    confidence,
                    delta.SourceFrameId);
                if (
                    _features.IsActive(
                        AugmentedRealityFeatureRegistry.AutomaticWorldFx) &&
                    _tracks.TryGetValue(trackId, out TrackHistory history) &&
                    history.SeenCount >= 3 &&
                    Time.unscaledTime >= history.NextSurfaceAt &&
                    IsSurfaceCandidate(label, kind) &&
                    TryDepthSurface(entity, delta, out List<Vector3> surface))
                {
                    history.NextSurfaceAt = Time.unscaledTime + 0.9f;
                    EmitAutomaticSurface(
                        trackId,
                        string.IsNullOrWhiteSpace(label) ? kind : label,
                        kind,
                        surface,
                        confidence,
                        string.IsNullOrWhiteSpace(delta.SourceFrameId)
                            ? "depth:xreal-live"
                            : "frame:" + delta.SourceFrameId);
                }
            }
        }

        private void UpdateTrack(
            string trackId,
            string label,
            string kind,
            Vector3 position,
            float confidence,
            string frameId)
        {
            float now = Time.unscaledTime;
            if (!_tracks.TryGetValue(trackId, out TrackHistory history))
            {
                history = new TrackHistory();
                _tracks[trackId] = history;
            }
            Vector3 previous = history.Position;
            float elapsed = now - history.At;
            bool hadPrevious = history.HasValue && elapsed > 0.04f && elapsed < 3f;
            history.Previous = previous;
            history.Position = position;
            history.At = now;
            history.HasValue = true;
            history.Label = label;
            history.Kind = kind;
            history.SeenCount++;

            string evidence = string.IsNullOrWhiteSpace(frameId)
                ? "depth:xreal-live"
                : "frame:" + frameId;
            if (_features.IsActive(AugmentedRealityFeatureRegistry.WorldLabels))
                EmitWorldMarker(trackId, label, kind, position, confidence, evidence);
            if (
                _features.IsActive(
                    AugmentedRealityFeatureRegistry.AutomaticWorldFx) &&
                history.SeenCount >= 3 &&
                now >= history.NextWorldFxAt)
            {
                history.NextWorldFxAt = now + 0.65f;
                EmitAutomaticWorldEffect(
                    trackId, label, kind, position, confidence, evidence);
            }

            if (!hadPrevious) return;
            Vector3 delta = position - previous;
            float speed = delta.magnitude / elapsed;
            if (speed < 0.08f || speed > 6f) return;

            if (_features.IsActive(AugmentedRealityFeatureRegistry.EventVision))
                EmitEventMotion(trackId, label, previous, position, confidence, evidence);
            if (
                _features.IsActive(AugmentedRealityFeatureRegistry.TrajectoryForecast) &&
                string.Equals(kind, "person", StringComparison.OrdinalIgnoreCase))
            {
                EmitTrajectory(
                    trackId, label, position, delta / elapsed, confidence, evidence);
            }
        }

        private void EmitAutomaticWorldEffect(
            string trackId,
            string label,
            string kind,
            Vector3 position,
            float confidence,
            string evidence)
        {
            if (
                _intents == null ||
                _camera == null ||
                confidence < 0.70f ||
                string.IsNullOrWhiteSpace(trackId))
                return;
            float distance = Vector3.Distance(_camera.transform.position, position);
            if (distance < 0.35f || distance > 24f) return;
            WorldMapStore.WorldDynamicBinding binding =
                ResolveDynamicBinding(label, kind, confidence);
            string template = binding?.templateId;
            if (string.IsNullOrWhiteSpace(template) &&
                !TryWorldTemplate(label, kind, distance, out template))
                return;
            if (binding != null)
            {
                string prefix = "xreal-auto-world:" + binding.bindingId + ":";
                var expired = new List<string>();
                foreach (var pair in _dynamicWorldExpiry)
                    if (pair.Value <= Time.unscaledTime) expired.Add(pair.Key);
                foreach (string expiredId in expired)
                    _dynamicWorldExpiry.Remove(expiredId);
                int activeForRule = 0;
                foreach (string activeId in _dynamicWorldExpiry.Keys)
                    if (activeId.StartsWith(prefix, StringComparison.Ordinal))
                        activeForRule++;
                string candidateId = prefix + trackId;
                if (activeForRule >= binding.maxInstances &&
                    !_automaticWorldIntentIds.Contains(candidateId))
                    return;
                position = DynamicPosition(position, binding);
            }

            string id = binding == null
                ? "xreal-auto-world:" + trackId
                : "xreal-auto-world:" + binding.bindingId + ":" + trackId;
            float quality = Mathf.Clamp(confidence, 0.72f, 0.94f);
            WorldMapStore.WorldAsset asset =
                binding == null || string.IsNullOrWhiteSpace(binding.assetId)
                    ? null
                    : _worldMap?.FindAsset(binding.assetId);
            _automaticWorldIntentIds.Add(id);
            if (binding != null)
                _dynamicWorldExpiry[id] =
                    Time.unscaledTime + binding.ttlMs / 1000f;
            _intents.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = id,
                Producer = "ultralive",
                TargetTrackId = trackId,
                Component = "world_hologram",
                Anchor = new Dictionary<string, object>
                {
                    { "coordinate_space", "tracking_local" },
                    { "position", Point(position) },
                },
                Content = SpatialContent(quality, true,
                    new Dictionary<string, object>
                    {
                        { "anchor_quality", quality },
                        { "marker_id", "auto-" + trackId },
                        { "template_id", template },
                        { "preset_id", binding?.presetId ?? string.Empty },
                        { "archetype_id", binding?.archetypeId ?? template },
                        { "style_id", binding?.styleId ?? "freeguy" },
                        { "animation_id", binding?.animationId ?? "soft_pulse" },
                        { "accent_hex", binding?.accentHex ?? string.Empty },
                        { "secondary_hex", binding?.secondaryHex ?? string.Empty },
                        { "asset_id", asset?.assetId ?? string.Empty },
                        { "asset_mime", asset?.mimeType ?? string.Empty },
                        { "asset_sha256", asset?.sha256 ?? string.Empty },
                        { "asset_base64", asset?.base64Data ?? string.Empty },
                        {
                            "asset_file_path",
                            asset?.localFilePath ?? string.Empty
                        },
                        { "scale", Point(binding?.scale.Value ?? Vector3.one) },
                        { "label", binding == null
                            ? (string.IsNullOrWhiteSpace(label)
                                ? "MONDE AUGMENTÉ"
                                : label)
                            : binding.label },
                        { "subtitle", binding?.subtitle ??
                            AutoWorldSubtitle(template) },
                        { "kind", NormaliseMarkerKind(kind) },
                        { "distance_m", distance },
                        { "persistence", "ephemeral" },
                        { "memory_write", false },
                    }),
                TruthLevel = "observed",
                Confidence = quality,
                Priority = template == "poi_beacon" ? 0.48 : 0.38,
                TtlMs = binding?.ttlMs ?? 950,
                EvidenceRefs = Evidence(evidence),
            });
        }

        private WorldMapStore.WorldDynamicBinding ResolveDynamicBinding(
            string label,
            string kind,
            float confidence)
        {
            if (_worldMap?.DynamicBindings == null) return null;
            string cleanLabel = (label ?? string.Empty).Trim().ToLowerInvariant();
            string cleanKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            WorldMapStore.WorldDynamicBinding best = null;
            int bestScore = -1;
            foreach (WorldMapStore.WorldDynamicBinding binding in
                _worldMap.DynamicBindings)
            {
                if (binding == null || !binding.enabled ||
                    confidence < binding.minConfidence)
                    continue;
                string wantedLabel =
                    (binding.targetLabel ?? string.Empty).Trim().ToLowerInvariant();
                string wantedKind =
                    (binding.targetKind ?? string.Empty).Trim().ToLowerInvariant();
                bool labelMatch = string.IsNullOrEmpty(wantedLabel) ||
                    cleanLabel.IndexOf(wantedLabel, StringComparison.Ordinal) >= 0;
                bool kindMatch = string.IsNullOrEmpty(wantedKind) ||
                    string.Equals(cleanKind, wantedKind, StringComparison.Ordinal) ||
                    (
                        wantedKind == "object" &&
                        cleanKind != "person"
                    ) ||
                    cleanLabel.IndexOf(wantedKind, StringComparison.Ordinal) >= 0;
                if (!labelMatch || !kindMatch) continue;
                int score = wantedLabel.Length * 2 + wantedKind.Length;
                if (score <= bestScore) continue;
                best = binding;
                bestScore = score;
            }
            return best;
        }

        private Vector3 DynamicPosition(
            Vector3 position,
            WorldMapStore.WorldDynamicBinding binding)
        {
            Vector3 offset = binding.offset.Value;
            switch (binding.attachment)
            {
                case "above": offset.y += .35f; break;
                case "below": offset.y -= .25f; break;
                case "left": offset.x -= .25f; break;
                case "right": offset.x += .25f; break;
                case "front": offset.z += .2f; break;
                case "rear": offset.z -= .2f; break;
            }
            if (_camera == null) return position + offset;
            Vector3 forward = Vector3.ProjectOnPlane(
                _camera.transform.forward, Vector3.up).normalized;
            return position +
                _camera.transform.right * offset.x +
                Vector3.up * offset.y +
                forward * offset.z;
        }

        private static bool TryWorldTemplate(
            string label,
            string kind,
            float distance,
            out string template)
        {
            string value = ((label ?? string.Empty) + " " +
                (kind ?? string.Empty)).Trim().ToLowerInvariant();
            if (
                ContainsAny(value, "car", "vehicle", "voiture", "truck",
                    "camion", "bus", "motorcycle", "moto", "bicycle", "vélo"))
            {
                template = "vehicle_fx";
                return true;
            }
            if (
                ContainsAny(value, "store", "storefront", "shop", "boutique",
                    "window", "vitrine", "restaurant", "café", "cafe"))
            {
                template = "holo_billboard";
                return true;
            }
            if (
                ContainsAny(value, "sign", "logo", "panel", "panneau",
                    "enseigne", "poster", "affiche"))
            {
                template = "neon_sign";
                return true;
            }
            if (
                ContainsAny(value, "building", "bâtiment", "batiment",
                    "monument", "tower", "gare", "station"))
            {
                template = "poi_beacon";
                return true;
            }
            // Keep ordinary objects readable rather than turning every detector
            // box into a billboard. Nearby stable objects receive only a compact
            // neon annotation; people never receive an automatic decoration.
            if (
                distance <= 8f &&
                !ContainsAny(value, "person", "people", "homme", "femme",
                    "face", "visage"))
            {
                template = "annotation";
                return true;
            }
            template = null;
            return false;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (string needle in needles)
                if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static string AutoWorldSubtitle(string template)
        {
            switch (template)
            {
                case "vehicle_fx": return "MOTION FX // LIVE";
                case "holo_billboard": return "HOLO DISPLAY // LIVE";
                case "poi_beacon": return "POINT D'INTÉRÊT // LIVE";
                case "annotation": return "OBJET // LIVE";
                default: return "NEON OVERLAY // LIVE";
            }
        }

        private void WithdrawAutomaticWorldEffects()
        {
            if (_broker != null)
                foreach (string id in _automaticWorldIntentIds)
                    _broker.Withdraw(id);
            _automaticWorldIntentIds.Clear();
            _dynamicWorldExpiry.Clear();
        }

        private static bool IsSurfaceCandidate(string label, string kind)
        {
            string value = ((label ?? string.Empty) + " " +
                (kind ?? string.Empty)).ToLowerInvariant();
            return ContainsAny(
                value,
                "store",
                "storefront",
                "shop",
                "boutique",
                "window",
                "vitrine",
                "sign",
                "panneau",
                "enseigne",
                "building",
                "bâtiment",
                "batiment");
        }

        private bool TryDepthSurface(
            Dictionary<string, object> entity,
            SceneDelta delta,
            out List<Vector3> points)
        {
            points = null;
            if (
                entity == null ||
                delta?.FrameWidth == null ||
                delta.FrameHeight == null ||
                !entity.TryGetValue("bbox", out object raw) ||
                raw == null)
                return false;
            JArray box;
            try { box = raw as JArray ?? JArray.FromObject(raw); }
            catch { return false; }
            if (box.Count < 4) return false;
            float width = delta.FrameWidth.Value;
            float height = delta.FrameHeight.Value;
            float x1 = Mathf.Clamp((float)box[0], 0f, width);
            float y1 = Mathf.Clamp((float)box[1], 0f, height);
            float x2 = Mathf.Clamp((float)box[2], 0f, width);
            float y2 = Mathf.Clamp((float)box[3], 0f, height);
            if (x2 - x1 < 8f || y2 - y1 < 8f) return false;
            // Stay inside the detector box: edges frequently include unrelated
            // geometry and would turn one façade into a folded polygon.
            float insetX = (x2 - x1) * 0.12f;
            float insetY = (y2 - y1) * 0.12f;
            x1 += insetX;
            x2 -= insetX;
            y1 += insetY;
            y2 -= insetY;
            Vector2[] viewports =
            {
                new Vector2(x1 / width, 1f - y1 / height),
                new Vector2(x1 / width, 1f - y2 / height),
                new Vector2(x2 / width, 1f - y2 / height),
                new Vector2(x2 / width, 1f - y1 / height),
            };
            var hits = new RaycastHit[4];
            for (int i = 0; i < viewports.Length; i++)
                if (!TryDepthHit(viewports[i], out hits[i])) return false;
            Collider collider = hits[0].collider;
            Vector3 normal = hits[0].normal;
            for (int i = 1; i < hits.Length; i++)
            {
                if (
                    hits[i].collider != collider ||
                    Vector3.Dot(normal, hits[i].normal) < 0.86f)
                    return false;
            }
            var result = new List<Vector3>(4);
            foreach (RaycastHit hit in hits) result.Add(hit.point);
            points = result;
            return true;
        }

        private void EmitAutomaticSurface(
            string trackId,
            string label,
            string kind,
            List<Vector3> points,
            float confidence,
            string evidence)
        {
            if (_intents == null || points == null || points.Count != 4) return;
            string id = "xreal-auto-surface:" + trackId;
            float quality = Mathf.Clamp(confidence, 0.76f, 0.93f);
            var encoded = new List<Dictionary<string, object>>(points.Count);
            foreach (Vector3 point in points) encoded.Add(Point(point));
            _automaticWorldIntentIds.Add(id);
            _intents.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = id,
                Producer = "ultralive",
                TargetTrackId = trackId,
                Component = "world_surface",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(quality, true,
                    new Dictionary<string, object>
                    {
                        { "surface_id", "auto-" + trackId },
                        { "surface_kind", NormaliseSurfaceKind(label, kind) },
                        { "surface_quality", quality },
                        { "surface_points", encoded },
                        { "convex", true },
                        { "label", label ?? string.Empty },
                        { "persistence", "ephemeral" },
                        { "memory_write", false },
                    }),
                TruthLevel = "observed",
                Confidence = quality,
                Priority = 0.34,
                TtlMs = 1200,
                EvidenceRefs = Evidence(evidence),
            });
        }

        private static string NormaliseSurfaceKind(string label, string kind)
        {
            string value = ((label ?? string.Empty) + " " +
                (kind ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(value, "store", "shop", "boutique", "vitrine"))
                return "storefront";
            if (ContainsAny(value, "sign", "panneau", "enseigne"))
                return "sign";
            return "building";
        }

        private void EmitWorldMarker(
            string trackId,
            string label,
            string kind,
            Vector3 position,
            float confidence,
            string evidence)
        {
            if (_intents == null || string.IsNullOrWhiteSpace(label)) return;
            float quality = Mathf.Clamp(confidence, 0.72f, 0.94f);
            _intents.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "xreal-world-" + trackId,
                Producer = "ultralive",
                TargetTrackId = trackId,
                Component = "world_marker",
                Anchor = new Dictionary<string, object>
                {
                    { "coordinate_space", "tracking_local" },
                    { "position", Point(position) },
                },
                Content = SpatialContent(quality, true, new Dictionary<string, object>
                {
                    { "marker_id", trackId },
                    { "label", label },
                    { "subtitle", "VISIONRT + XREAL DEPTH" },
                    { "kind", NormaliseMarkerKind(kind) },
                    {
                        "distance_m",
                        _camera == null ? 0d : Vector3.Distance(
                            _camera.transform.position, position)
                    },
                    { "anchor_quality", quality },
                }),
                TruthLevel = "observed",
                Confidence = quality,
                Priority = 0.42,
                TtlMs = 1250,
                EvidenceRefs = Evidence(evidence),
            });
        }

        private void EmitEventMotion(
            string trackId,
            string label,
            Vector3 previous,
            Vector3 current,
            float confidence,
            string evidence)
        {
            List<Dictionary<string, object>> points =
                Interpolate(previous, current, 6);
            EmitPath(
                "xreal-motion-" + trackId,
                "event_motion",
                string.IsNullOrWhiteSpace(label) ? "MOUVEMENT" : label,
                0.55f,
                confidence,
                new List<Dictionary<string, object>>
                {
                    Path("motion-" + trackId, 1f, confidence, points),
                },
                evidence,
                new Dictionary<string, object>
                {
                    { "rgb_motion_valid", true },
                    { "head_motion_compensated", true },
                });
        }

        private void EmitTrajectory(
            string trackId,
            string label,
            Vector3 origin,
            Vector3 velocity,
            float confidence,
            string evidence)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            float speed = Mathf.Clamp(horizontal.magnitude, 0.15f, 2.2f);
            Vector3 direction = horizontal.sqrMagnitude > 0.001f
                ? horizontal.normalized
                : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            var primary = new List<Dictionary<string, object>>();
            var alternative = new List<Dictionary<string, object>>();
            for (int i = 0; i <= 5; i++)
            {
                float t = i * 0.4f;
                primary.Add(Point(origin + direction * speed * t));
                alternative.Add(Point(
                    origin +
                    direction * speed * t * 0.92f +
                    side * 0.16f * t * t));
            }
            EmitPath(
                "xreal-forecast-" + trackId,
                "trajectory_forecast",
                string.IsNullOrWhiteSpace(label) ? "PERSONNE" : label,
                2f,
                confidence,
                new List<Dictionary<string, object>>
                {
                    Path("primary-" + trackId, 0.72f, confidence, primary),
                    Path("alternate-" + trackId, 0.28f, confidence * 0.9f, alternative),
                },
                evidence,
                null);
        }

        private void EmitPath(
            string id,
            string mode,
            string label,
            float horizon,
            float quality,
            List<Dictionary<string, object>> paths,
            string evidence,
            Dictionary<string, object> extra)
        {
            if (_intents == null) return;
            var additions = new Dictionary<string, object>
            {
                { "mode", mode },
                { "label", label ?? string.Empty },
                { "horizon_s", horizon },
                { "paths", paths },
            };
            if (extra != null)
                foreach (KeyValuePair<string, object> item in extra)
                    additions[item.Key] = item.Value;
            _intents.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = id,
                Producer = "ultralive",
                Component = "world_path",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(
                    Mathf.Clamp(quality, 0.66f, 0.94f), true, additions),
                TruthLevel = "probable",
                Confidence = Mathf.Clamp01(quality),
                Priority = 0.48,
                TtlMs = 900,
                EvidenceRefs = Evidence(evidence),
            });
        }

        /// <summary>
        /// Capture one endpoint for the explicit AR ruler. Two valid Depth hits
        /// emit a measurement and reset the pair.
        /// </summary>
        public bool CaptureMeasurementPoint(Vector2 viewport)
        {
            if (
                _features == null ||
                !_features.IsActive(AugmentedRealityFeatureRegistry.ArMeasurement) ||
                !TryDepthHit(viewport, out RaycastHit hit))
                return false;
            if (!_measureStart.HasValue)
            {
                _measureStart = hit.point;
                return true;
            }
            Vector3 start = _measureStart.Value;
            _measureStart = null;
            float distance = Vector3.Distance(start, hit.point);
            if (distance < 0.01f || distance > 20f) return false;
            const float uncertainty = 0.02f;
            _intents?.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "xreal-measure-" + DateTime.UtcNow.Ticks,
                Producer = "ultralive",
                Component = "world_measure",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(0.84f, true, new Dictionary<string, object>
                {
                    { "intrinsics_valid", true },
                    { "start", Point(start) },
                    { "end", Point(hit.point) },
                    { "distance_m", distance },
                    { "uncertainty_m", uncertainty },
                    { "label", "XREAL DEPTH" },
                }),
                TruthLevel = "observed",
                Confidence = 0.84,
                Priority = 0.38,
                TtlMs = 15000,
                EvidenceRefs = Evidence("depth:xreal-measure"),
            });
            return true;
        }

        public bool TryPlaceKeyboard(Vector2 viewport)
        {
            if (
                _features == null ||
                !_features.IsActive(AugmentedRealityFeatureRegistry.SpatialKeyboard) ||
                !TryDepthHit(viewport, out RaycastHit hit) ||
                Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) < 0.65f)
                return false;
            Vector3 forward = Vector3.ProjectOnPlane(
                _camera.transform.forward, hit.normal).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(hit.normal, forward).normalized;
            const float width = 0.70f;
            const float height = 0.25f;
            Vector3 origin =
                hit.point - right * (width * 0.5f) - forward * (height * 0.5f);
            _keyboard = new KeyboardPlacement
            {
                Origin = origin,
                Right = right,
                Forward = forward,
                Normal = hit.normal,
                Width = width,
                Height = height,
            };
            _intents?.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "xreal-spatial-keyboard",
                Producer = "ultralive",
                Component = "world_keyboard",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(0.82f, true, new Dictionary<string, object>
                {
                    { "explicit_activation", true },
                    { "hand_tracking_valid", true },
                    { "origin", Point(origin) },
                    { "right", Point(right) },
                    { "forward", Point(forward) },
                    { "width_m", width },
                    { "height_m", height },
                }),
                TruthLevel = "observed",
                Confidence = 0.82,
                Priority = 0.32,
                TtlMs = 600000,
                EvidenceRefs = Evidence("depth:xreal-keyboard-plane"),
            });
            return true;
        }

        public bool PressKeyboard(Vector2 viewport, bool contactConfirmed)
        {
            if (_keyboard == null || _camera == null) return false;
            Ray ray = _camera.ViewportPointToRay(viewport);
            var plane = new Plane(_keyboard.Normal, _keyboard.Origin);
            if (!plane.Raycast(ray, out float enter) || enter < 0f || enter > 10f)
                return false;
            Vector3 point = ray.GetPoint(enter);
            foreach (Components.WorldKeyboardPlane keyboard in
                FindObjectsByType<Components.WorldKeyboardPlane>(
                    FindObjectsSortMode.None))
            {
                if (keyboard.TryPressWorld(point, contactConfirmed, out _))
                    return true;
            }
            return false;
        }

        private bool TryDepthHit(Vector2 viewport, out RaycastHit selected)
        {
            selected = default;
            if (
                !DepthReady ||
                _camera == null ||
                viewport.x < 0f || viewport.x > 1f ||
                viewport.y < 0f || viewport.y > 1f)
                return false;
            Ray ray = _camera.ViewportPointToRay(viewport);
            RaycastHit[] hits = Physics.RaycastAll(
                ray, 20f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (
                    hit.collider == null ||
                    hit.collider.GetComponentInParent<XrealSpatialMeshTag>() == null ||
                    hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                selected = hit;
                found = true;
            }
            return found;
        }

        private bool PoseTracked()
        {
            if (!_creatorMode)
                return _pose != null && _pose.Latest.IsTracking;
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesAtXRNode(XRNode.CenterEye, devices);
            foreach (InputDevice device in devices)
            {
                if (
                    device.isValid &&
                    device.TryGetFeatureValue(
                        CommonUsages.isTracked,
                        out bool tracked) &&
                    tracked)
                    return true;
            }
            return false;
        }

        private bool HasReadableDepthMesh()
        {
            foreach (XrealSpatialMeshTag tag in
                FindObjectsByType<XrealSpatialMeshTag>(FindObjectsSortMode.None))
            {
                MeshCollider collider = tag.GetComponent<MeshCollider>();
                if (
                    collider != null &&
                    collider.enabled &&
                    collider.sharedMesh != null &&
                    collider.sharedMesh.vertexCount >= 3)
                    return true;
            }
            return false;
        }

        private void EnsureSpatialManagers()
        {
            _managersRequested = true;
            if (!CompiledForXreal) return;
            try
            {
                // Direct type roots are intentional: reflection-only lookup let
                // IL2CPP strip AR Foundation managers from the XREAL APK.
                Type arSessionType = typeof(ARSession);
                Type meshManagerType = typeof(ARMeshManager);
                Type anchorManagerType = typeof(ARAnchorManager);
                Type originType = typeof(XROrigin);
                if (arSessionType == null || meshManagerType == null ||
                    anchorManagerType == null || originType == null)
                    throw new InvalidOperationException(
                        "AR Foundation/XROrigin unavailable in XREAL product build");

                if (FindBehaviour(arSessionType) == null)
                {
                    _arSession = new GameObject("AR Session (XREAL product)");
                    _arSession.AddComponent(arSessionType);
                }
                Component origin = FindBehaviour(originType);
                if (origin == null)
                    throw new InvalidOperationException("XREAL XR Origin missing");
                // AR Foundation 6 requires ARMeshManager to be on a descendant
                // of XROrigin, not on the XROrigin GameObject itself. Parenting
                // the host before AddComponent also prevents OnEnable from
                // throwing during construction on device.
                const string managerRootName =
                    "MLOmega XREAL Spatial Managers";
                Transform managerRoot =
                    origin.transform.Find(managerRootName);
                if (managerRoot == null)
                {
                    var host = new GameObject(managerRootName);
                    managerRoot = host.transform;
                    managerRoot.SetParent(origin.transform, false);
                }
                _meshManager = managerRoot.GetComponent(meshManagerType) ??
                    managerRoot.gameObject.AddComponent(meshManagerType);
                _anchorManager = managerRoot.GetComponent(anchorManagerType) ??
                    managerRoot.gameObject.AddComponent(anchorManagerType);
                _meshPrefab = BuildDepthMeshPrefab();
                // AR Foundation 6 changed ARMeshManager.meshPrefab from a
                // GameObject slot to a MeshFilter slot. Passing the host object
                // through reflection caused a retry/error loop on the real
                // One Pro even after the hierarchy itself was corrected.
                SetMember(
                    _meshManager,
                    "meshPrefab",
                    _meshPrefab.GetComponent<MeshFilter>());
                SetMember(_meshManager, "density", 0.35f);
                SetMember(_meshManager, "normals", true);
                LastProviderError = string.Empty;
                ConfigurePersistentAnchors();
            }
            catch (Exception ex)
            {
                _managersRequested = false;
                _nextManagerRetryAt = Time.unscaledTime + 3f;
                LastProviderError =
                    "xreal_spatial_init:" + ex.GetType().Name + ":" + ex.Message;
                Debug.LogWarning("[XrealSpatialProvider] " + LastProviderError);
                SetLocalCapabilities(false);
            }
        }

        private GameObject BuildDepthMeshPrefab()
        {
            var prefab = new GameObject("MLOmega XREAL Depth Mesh");
            prefab.transform.SetParent(transform, false);
            prefab.AddComponent<MeshFilter>();
            MeshRenderer renderer = prefab.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            prefab.AddComponent<MeshCollider>();
            prefab.AddComponent<XrealSpatialMeshTag>();
            return prefab;
        }

        private void SetManagersEnabled(bool enabled)
        {
            bool sameManagers =
                ReferenceEquals(_managerStateMesh, _meshManager) &&
                ReferenceEquals(_managerStateSession, _arSession);
            if (
                _managerStateKnown &&
                sameManagers &&
                _managerStateEnabled == enabled)
                return;

            // ARMeshManager disables itself when the active XREAL loader exposes
            // no mesh subsystem. Re-enabling it every Update caused a warning
            // and subsystem lookup on every eye frame. Apply a requested state
            // once per manager instance/state transition, like the official
            // template does, and let AR Foundation keep an unsupported manager
            // disabled.
            if (_meshManager is Behaviour mesh && mesh != null)
                mesh.enabled = enabled;
            if (_arSession != null && _arSession.activeSelf != enabled)
                _arSession.SetActive(enabled);

            _managerStateKnown = true;
            _managerStateEnabled = enabled;
            _managerStateMesh = _meshManager;
            _managerStateSession = _arSession;
        }

        private void UpdateMeshRendering()
        {
            bool occlusion = _features != null &&
                _features.IsActive(AugmentedRealityFeatureRegistry.DepthOcclusion) &&
                _depthOcclusionMaterial != null;
            bool styling = _features != null &&
                _features.IsActive(AugmentedRealityFeatureRegistry.WorldStyling) &&
                _freeGuyMeshMaterial != null;
            foreach (XrealSpatialMeshTag tag in
                FindObjectsByType<XrealSpatialMeshTag>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                if (tag == null) continue;
                MeshRenderer renderer = tag.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                renderer.enabled = occlusion || styling;
                if (occlusion && styling)
                    renderer.sharedMaterials = new[]
                    {
                        _depthOcclusionMaterial,
                        _freeGuyMeshMaterial,
                    };
                else if (occlusion)
                    renderer.sharedMaterial = _depthOcclusionMaterial;
                else if (styling)
                    renderer.sharedMaterial = _freeGuyMeshMaterial;
            }
        }

        private bool AnchorProviderReady()
        {
#if XREAL_SDK_PRESENT
            return _anchorManager is ARAnchorManager manager &&
                manager.subsystem != null;
#else
            return false;
#endif
        }

        private void ConfigurePersistentAnchors()
        {
#if XREAL_SDK_PRESENT
            if (!(_anchorManager is ARAnchorManager manager)) return;
            _anchorMappingDirectory = System.IO.Path.Combine(
                Application.persistentDataPath, "xreal-anchor-maps");
            manager.SetAndCreateAnchorMappingDirectory(
                _anchorMappingDirectory);
#endif
        }

        private bool HandProviderReady()
        {
#if XREAL_SDK_PRESENT && XR_HANDS
            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (XRHandSubsystem subsystem in subsystems)
                if (subsystem != null && subsystem.running) return true;
#endif
            return false;
        }

        private async void LoadPersistentAnchors()
        {
            _anchorsLoadStarted = true;
#if XREAL_SDK_PRESENT
            if (!(_anchorManager is ARAnchorManager manager) ||
                manager.subsystem == null)
            {
                _anchorsLoadStarted = false;
                return;
            }
            try
            {
                var idsResult = await manager.TryGetSavedAnchorIdsAsync(
                    Allocator.Temp);
                if (!idsResult.status.IsSuccess())
                {
                    LastProviderError = "xreal_anchor_list:" +
                        idsResult.status.statusCode;
                    return;
                }
                var ids = idsResult.value;
                try
                {
                    var candidates =
                        new Dictionary<string, SerializableGuid>(
                            StringComparer.Ordinal);
                    foreach (SerializableGuid id in ids)
                        candidates[id.ToString()] = id;
                    if (_worldMap != null)
                    {
                        foreach (WorldMapStore.WorldContent content in
                            _worldMap.Contents)
                        {
                            if (
                                content != null &&
                                TryParsePersistentGuid(
                                    content.anchorGuid,
                                    out SerializableGuid imported))
                                candidates[imported.ToString()] = imported;
                        }
                    }
                    _worldAnchorBatchExpected = 0;
                    foreach (KeyValuePair<string, SerializableGuid> pair in
                        candidates)
                        if (_worldMap?.FindByAnchor(pair.Key) != null)
                            _worldAnchorBatchExpected++;
                    _worldAnchorBatchCompleted = 0;
                    _loadedWorldAnchors.Clear();
                    foreach (KeyValuePair<string, SerializableGuid> pair in
                        candidates)
                    {
                        SerializableGuid id = pair.Value;
                        WorldMapStore.WorldContent worldContent =
                            _worldMap?.FindByAnchor(id.ToString());
                        var load = await manager.TryLoadAnchorAsync(id);
                        if (load.status.IsSuccess() && load.value != null)
                            StartCoroutine(EmitAnchorWhenTracked(
                                load.value,
                                id.ToString(),
                                "restored",
                                worldContent));
                        else if (worldContent != null)
                        {
                            _worldMap?.MarkUnresolved(id.ToString());
                            CompleteWorldAnchorLoad();
                        }
                    }
                    if (_worldAnchorBatchExpected == 0)
                        _loadedWorldAnchors.Clear();
                }
                finally
                {
                    if (ids.IsCreated) ids.Dispose();
                }
            }
            catch (Exception ex)
            {
                LastProviderError =
                    "xreal_anchor_load:" + ex.GetType().Name + ":" + ex.Message;
                _anchorsLoadStarted = false;
            }
#endif
        }

        public void EnableCreatorMode()
        {
            _creatorMode = true;
            _nextManagerRetryAt = 0f;
        }

        public void BeginCreatorSpatialMapping()
        {
            if (!_creatorMode) return;
            _creatorSpatialRequested = true;
            _nextManagerRetryAt = 0f;
        }

        public bool CreateCreatorMap(string displayName)
        {
            if (!_creatorMode || _worldMapDrafts == null) return false;
            _worldMap = _worldMapDrafts.Create(displayName);
            CreatorOperationCompleted?.Invoke(
                _worldMap.WorldMapId, true, "map_created");
            return true;
        }

        public bool SwitchCreatorMap(string mapId)
        {
            if (!_creatorMode || _worldMapDrafts == null) return false;
            WorldMapStore selected = _worldMapDrafts.Open(mapId);
            if (selected == null) return false;
            _worldMap = selected;
            CreatorOperationCompleted?.Invoke(mapId, true, "map_selected");
            return true;
        }

        public bool DeleteCreatorMap(string mapId)
        {
            if (!_creatorMode || _worldMapDrafts == null) return false;
            bool deleted = _worldMapDrafts.Delete(mapId);
            if (deleted && _worldMap?.WorldMapId == mapId)
                _worldMap = new WorldMapStore(
                    System.IO.Path.Combine(
                        Application.persistentDataPath, "xreal-world-maps"),
                    CalibrationId);
            CreatorOperationCompleted?.Invoke(
                mapId, deleted, deleted ? "map_deleted" : "map_not_empty");
            return deleted;
        }

        public bool SaveCreatorDynamicBinding(
            WorldCreatorCatalog.Entry preset,
            string targetLabel,
            string targetKind,
            string attachment,
            string label,
            string subtitle,
            Vector3 scale,
            string assetId)
        {
            if (!_creatorMode || _worldMap == null || preset == null)
                return false;
            WorldMapStore.WorldDynamicBinding binding =
                _worldMap.UpsertDynamicBinding(
                    null,
                    preset,
                    targetLabel,
                    targetKind,
                    attachment,
                    label,
                    subtitle,
                    assetId,
                    Vector3.zero,
                    scale);
            bool saved = binding != null;
            CreatorOperationCompleted?.Invoke(
                binding?.bindingId ?? string.Empty,
                saved,
                saved ? "dynamic_saved" : "dynamic_invalid");
            return saved;
        }

        public bool RemoveCreatorDynamicBinding(string bindingId)
        {
            if (!_creatorMode || _worldMap == null) return false;
            bool removed = _worldMap.RemoveDynamicBinding(bindingId);
            CreatorOperationCompleted?.Invoke(
                bindingId,
                removed,
                removed ? "dynamic_removed" : "dynamic_missing");
            return removed;
        }

        public bool TryCreatorPlacement(
            Vector2 viewport,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (
                !_creatorMode ||
                !DepthReady ||
                !TryDepthHit(viewport, out RaycastHit hit))
                return false;
            position = hit.point;
            rotation = CreatorSurfaceRotation(hit, 0f);
            return true;
        }

        public bool PersistCreatorContent(
            Vector2 viewport,
            WorldCreatorCatalog.Entry preset,
            string label,
            string subtitle,
            Vector3 scale,
            float yawDegrees,
            string assetId,
            string motionPath,
            float motionRadiusM,
            float motionSpeed,
            float motionHeightM)
        {
            if (
                !_creatorMode ||
                preset == null ||
                !CreatorReady ||
                !TryDepthHit(viewport, out RaycastHit hit) ||
                !AnchorProviderReady())
                return false;
            Quaternion rotation =
                CreatorSurfaceRotation(hit, yawDegrees);
            SaveCreatorContentAt(
                hit.point,
                rotation,
                preset,
                label,
                subtitle,
                new Vector3(
                    Mathf.Clamp(
                        scale.x, 0.1f, WorldMapStore.MaxWorldScale),
                    Mathf.Clamp(
                        scale.y, 0.1f, WorldMapStore.MaxWorldScale),
                    Mathf.Clamp(
                        scale.z, 0.1f, WorldMapStore.MaxWorldScale)),
                assetId,
                motionPath,
                motionRadiusM,
                motionSpeed,
                motionHeightM);
            return true;
        }

        private Quaternion CreatorSurfaceRotation(
            RaycastHit hit,
            float angleDegrees)
        {
            Vector3 normal = hit.normal.sqrMagnitude < .5f
                ? Vector3.up
                : hit.normal.normalized;
            bool floorLike = Mathf.Abs(
                Vector3.Dot(normal, Vector3.up)) >= .65f;
            Vector3 forward;
            if (floorLike)
            {
                forward = _camera == null
                    ? Vector3.forward
                    : Vector3.ProjectOnPlane(
                        _camera.transform.forward,
                        Vector3.up);
                if (forward.sqrMagnitude < .001f)
                    forward = Vector3.forward;
            }
            else
            {
                // Wall-mounted signs face away from the supporting surface
                // while retaining a true world vertical.
                forward = -normal;
            }
            Quaternion basis = Quaternion.LookRotation(
                forward.normalized,
                Vector3.up);
            float bounded =
                Mathf.Repeat(angleDegrees + 180f, 360f) - 180f;
            return floorLike
                ? Quaternion.AngleAxis(bounded, Vector3.up) * basis
                : basis * Quaternion.AngleAxis(
                    bounded,
                    Vector3.forward);
        }

        private async void SaveCreatorContentAt(
            Vector3 position,
            Quaternion rotation,
            WorldCreatorCatalog.Entry preset,
            string label,
            string subtitle,
            Vector3 scale,
            string assetId,
            string motionPath,
            float motionRadiusM,
            float motionSpeed,
            float motionHeightM)
        {
#if XREAL_SDK_PRESENT
            if (!(_anchorManager is ARAnchorManager manager)) return;
            var go = new GameObject("MLOmega Atelier Anchor");
            go.transform.SetPositionAndRotation(position, rotation);
            ARAnchor anchor = go.AddComponent<ARAnchor>();
            string contentId = "atelier-" + Guid.NewGuid().ToString("N");
            try
            {
                for (int i = 0;
                    i < 90 && anchor != null &&
                    anchor.trackingState != TrackingState.Tracking;
                    i++)
                    await Awaitable.NextFrameAsync();
                if (
                    anchor == null ||
                    anchor.trackingState != TrackingState.Tracking)
                    throw new InvalidOperationException(
                        "creator anchor did not reach Tracking");
                var saved = await manager.TrySaveAnchorAsync(anchor);
                if (!saved.status.IsSuccess())
                    throw new InvalidOperationException(
                        "creator save status=" + saved.status.statusCode);
                string anchorGuid = saved.value.ToString();
                WorldMapStore.WorldContent content = _worldMap.Upsert(
                    contentId,
                    anchorGuid,
                    preset.templateId,
                    string.IsNullOrWhiteSpace(label) ? preset.label : label,
                    string.IsNullOrWhiteSpace(subtitle)
                        ? preset.subtitle
                        : subtitle,
                    string.Empty,
                    "manual",
                    "atelier:xreal-depth:" + preset.presetId,
                    position,
                    rotation,
                    scale,
                    0.94f,
                    motionPath: motionPath,
                    motionRadiusM: motionRadiusM,
                    motionSpeed: motionSpeed,
                    motionHeightM: motionHeightM);
                _worldMap.ApplyVisualPreset(content.worldContentId, preset);
                if (!string.IsNullOrWhiteSpace(assetId))
                    _worldMap.AssignAsset(content.worldContentId, assetId);
                CreatorOperationCompleted?.Invoke(contentId, true, "saved");
            }
            catch (Exception ex)
            {
                LastProviderError =
                    "xreal_creator_save:" + ex.GetType().Name + ":" + ex.Message;
                CreatorOperationCompleted?.Invoke(
                    contentId, false, LastProviderError);
                if (go != null) Destroy(go);
            }
#endif
        }

        public bool RemoveCreatorContent(string worldContentId)
        {
            if (!_creatorMode || _worldMap == null) return false;
            WorldMapStore.WorldContent content =
                _worldMap.FindById(worldContentId);
            if (content == null) return false;
            EraseCreatorContent(content);
            return true;
        }

        public bool PrepareCreatorExport(out string error)
        {
            error = string.Empty;
            if (!_creatorMode || _worldMap == null)
            {
                error = "creator_map_unavailable";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_anchorMappingDirectory))
            {
                error = "anchor_mapping_directory_unavailable";
                return false;
            }
            var distinctAnchors = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (WorldMapStore.WorldContent content in _worldMap.Contents)
                if (
                    content != null &&
                    !string.IsNullOrWhiteSpace(content.anchorGuid))
                    distinctAnchors.Add(content.anchorGuid);
            if (
                distinctAnchors.Count == 0 &&
                (_worldMap.DynamicBindings?.Count ?? 0) == 0)
            {
                error = "world_map_empty";
                return false;
            }
            if (distinctAnchors.Count == 1)
            {
                error = "anchor_geometry_baseline_requires_two";
                return false;
            }
            return _worldMap.CaptureAnchorMappings(
                _anchorMappingDirectory,
                out error);
        }

        private async void EraseCreatorContent(
            WorldMapStore.WorldContent content)
        {
#if XREAL_SDK_PRESENT
            bool erased = false;
            string error = "anchor_guid_invalid";
            try
            {
                if (
                    _anchorManager is ARAnchorManager manager &&
                    TryParsePersistentGuid(
                        content.anchorGuid,
                        out SerializableGuid guid))
                {
                    XRResultStatus result =
                        await manager.TryEraseAnchorAsync(guid);
                    erased = result.IsSuccess();
                    error = erased
                        ? string.Empty
                        : "erase status=" + result.statusCode;
                }
                if (erased) _worldMap.Remove(content.worldContentId);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ":" + ex.Message;
            }
            CreatorOperationCompleted?.Invoke(
                content.worldContentId, erased, error);
#endif
        }

        public bool ImportAnchoredWorld() =>
            !_creatorMode &&
            _worldMapExchange != null &&
            _worldMapExchange.BeginImport();

        public bool SetWorldMapActive(string mapId, bool active)
        {
            if (_creatorMode || _worldMapLibrary == null ||
                !_worldMapLibrary.SetActive(mapId, active))
                return false;
            return RebuildActiveWorldMaps();
        }

        public bool RemoveInstalledWorldMap(string mapId)
        {
            if (_creatorMode || _worldMapLibrary == null ||
                !_worldMapLibrary.Remove(mapId))
                return false;
            return RebuildActiveWorldMaps();
        }

        private void OnWorldMapImported(string packagePath)
        {
            string error = string.Empty;
            if (
                _worldMapLibrary == null ||
                !_worldMapLibrary.InstallPackage(
                    packagePath,
                    true,
                    out _,
                    out error))
            {
                LastProviderError = "world_map_import:" + error;
                return;
            }
            if (!RebuildActiveWorldMaps()) return;
            LastProviderError = string.Empty;
        }

        private bool RebuildActiveWorldMaps()
        {
            string error = string.Empty;
            if (
                _worldMapLibrary == null ||
                !_worldMapLibrary.TryComposeActive(
                    out WorldMapStore.MapDocument composition,
                    out error) ||
                _worldMap == null ||
                !_worldMap.ReplaceDocument(composition))
            {
                LastProviderError =
                    "world_map_compose:" +
                    (string.IsNullOrWhiteSpace(error) ? "replace_failed" : error);
                return false;
            }
            if (
                composition.anchorMappings.Count > 0 &&
                string.IsNullOrWhiteSpace(_anchorMappingDirectory) ||
                composition.anchorMappings.Count > 0 &&
                !_worldMap.InstallAnchorMappings(
                    _anchorMappingDirectory, out error))
            {
                LastProviderError =
                    "world_map_anchor_install:" + error;
                return false;
            }
            // The native files are now in the SDK directory. Do not keep a
            // second 24 MiB base64 copy in the production APK's durable map.
            _worldMap.ReleaseEmbeddedAnchorMappings();
            _emittedAnchorIds.Clear();
#if XREAL_SDK_PRESENT
            _loadedWorldAnchors.Clear();
            _worldAnchorBatchExpected = 0;
            _worldAnchorBatchCompleted = 0;
#endif
            _anchorsLoadStarted = false;
            LastProviderError = string.Empty;
            return true;
        }

#if XREAL_SDK_PRESENT
        private static bool TryParsePersistentGuid(
            string value,
            out SerializableGuid guid)
        {
            guid = SerializableGuid.empty;
            string[] parts = (value ?? string.Empty).Split('-');
            if (
                parts.Length == 2 &&
                ulong.TryParse(
                    parts[0],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out ulong low) &&
                ulong.TryParse(
                    parts[1],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out ulong high))
            {
                guid = new SerializableGuid(low, high);
                return guid != SerializableGuid.empty;
            }
            if (Guid.TryParse(value, out Guid systemGuid))
            {
                guid = new SerializableGuid(systemGuid);
                return guid != SerializableGuid.empty;
            }
            return false;
        }
#endif

        public bool PersistAnchorAtViewport(Vector2 viewport)
        {
            if (
                !DepthReady ||
                _features == null ||
                !_features.IsActive(
                    AugmentedRealityFeatureRegistry.PersistentAnchors) ||
                !TryDepthHit(viewport, out RaycastHit hit) ||
                !AnchorProviderReady())
                return false;
            SaveAnchorAt(hit.point, Quaternion.LookRotation(
                Vector3.ProjectOnPlane(
                    _camera != null ? _camera.transform.forward : Vector3.forward,
                    hit.normal).normalized,
                hit.normal));
            return true;
        }

        private async void SaveAnchorAt(Vector3 position, Quaternion rotation)
        {
#if XREAL_SDK_PRESENT
            if (!(_anchorManager is ARAnchorManager manager)) return;
            var go = new GameObject("MLOmega Persistent Anchor");
            go.transform.SetPositionAndRotation(position, rotation);
            ARAnchor anchor = go.AddComponent<ARAnchor>();
            try
            {
                for (int i = 0;
                    i < 90 && anchor != null &&
                    anchor.trackingState != TrackingState.Tracking;
                    i++)
                    await Awaitable.NextFrameAsync();
                if (anchor == null ||
                    anchor.trackingState != TrackingState.Tracking)
                    throw new InvalidOperationException(
                        "anchor did not reach Tracking");
                var saved = await manager.TrySaveAnchorAsync(anchor);
                if (!saved.status.IsSuccess())
                    throw new InvalidOperationException(
                        "save status=" + saved.status.statusCode);
                EmitPersistentAnchor(
                    anchor.transform.position,
                    saved.value.ToString(),
                    "saved");
            }
            catch (Exception ex)
            {
                LastProviderError =
                    "xreal_anchor_save:" + ex.GetType().Name + ":" + ex.Message;
                if (go != null) Destroy(go);
            }
#endif
        }

#if XREAL_SDK_PRESENT
        private IEnumerator EmitAnchorWhenTracked(
            ARAnchor anchor,
            string persistentId,
            string state,
            WorldMapStore.WorldContent content)
        {
            float deadline = Time.unscaledTime + 8f;
            while (anchor != null &&
                anchor.trackingState != TrackingState.Tracking &&
                Time.unscaledTime < deadline)
                yield return null;
            if (anchor != null &&
                anchor.trackingState == TrackingState.Tracking)
            {
                if (content != null)
                {
                    _loadedWorldAnchors[persistentId] =
                        new LoadedWorldAnchor(anchor, content);
                    CompleteWorldAnchorLoad();
                }
                else
                    EmitPersistentAnchor(
                        anchor.transform.position, persistentId, state);
            }
            else
            {
                _worldMap?.MarkUnresolved(persistentId);
                LastProviderError =
                    "xreal_anchor_restore:not_tracking:" + persistentId;
                if (content != null) CompleteWorldAnchorLoad();
            }
        }

        private void CompleteWorldAnchorLoad()
        {
            _worldAnchorBatchCompleted++;
            if (
                _worldAnchorBatchExpected <= 0 ||
                _worldAnchorBatchCompleted < _worldAnchorBatchExpected)
                return;
            ValidateAndEmitWorldAnchorBatch();
        }

        private void ValidateAndEmitWorldAnchorBatch()
        {
            var byMap =
                new Dictionary<string, List<KeyValuePair<string, LoadedWorldAnchor>>>(
                    StringComparer.Ordinal);
            foreach (KeyValuePair<string, LoadedWorldAnchor> pair in
                _loadedWorldAnchors)
            {
                string mapId = string.IsNullOrWhiteSpace(
                    pair.Value.Content.sourceMapId)
                        ? _worldMap?.WorldMapId ?? "legacy"
                        : pair.Value.Content.sourceMapId;
                if (!byMap.TryGetValue(mapId, out var group))
                {
                    group = new List<KeyValuePair<string, LoadedWorldAnchor>>();
                    byMap[mapId] = group;
                }
                group.Add(pair);
            }
            bool emittedAny = false;
            var errors = new List<string>();
            foreach (KeyValuePair<
                string,
                List<KeyValuePair<string, LoadedWorldAnchor>>> mapGroup in byMap)
            {
                List<KeyValuePair<string, LoadedWorldAnchor>> anchors =
                    mapGroup.Value;
                if (anchors.Count < 2)
                {
                    errors.Add(mapGroup.Key + ":insufficient_tracked_baseline");
                    foreach (var pair in anchors)
                        _worldMap?.MarkUnresolved(pair.Key);
                    continue;
                }
                var samples =
                    new List<WorldAnchorGeometryGuard.Sample>(anchors.Count);
                foreach (var pair in anchors)
                {
                    LoadedWorldAnchor loaded = pair.Value;
                    samples.Add(new WorldAnchorGeometryGuard.Sample(
                        pair.Key,
                        loaded.Content.localPosition.Value,
                        Quaternion.Euler(loaded.Content.localEuler.Value),
                        loaded.Anchor.transform.position,
                        loaded.Anchor.transform.rotation));
                }
                if (!WorldAnchorGeometryGuard.TryValidate(
                        samples,
                        out string geometryError))
                {
                    errors.Add(mapGroup.Key + ":" + geometryError);
                    foreach (var pair in anchors)
                        _worldMap?.MarkUnresolved(pair.Key);
                    continue;
                }
                foreach (var pair in anchors)
                {
                    LoadedWorldAnchor loaded = pair.Value;
                    if (
                        loaded.Anchor == null ||
                        loaded.Anchor.trackingState != TrackingState.Tracking)
                        continue;
                    EmitPersistentWorldContent(
                        loaded.Anchor.transform.position,
                        loaded.Anchor.transform.rotation,
                        pair.Key,
                        loaded.Content);
                    emittedAny = true;
                }
            }
            LastProviderError = errors.Count == 0
                ? string.Empty
                : "xreal_anchor_geometry:" + string.Join("|", errors);
            if (!emittedAny && errors.Count > 0)
                Debug.LogWarning("[XREAL] " + LastProviderError);
        }

        private void RejectLoadedWorldAnchors(string error)
        {
            LastProviderError = error;
            foreach (KeyValuePair<string, LoadedWorldAnchor> pair in
                _loadedWorldAnchors)
                _worldMap?.MarkUnresolved(pair.Key);
            _loadedWorldAnchors.Clear();
        }

        private sealed class LoadedWorldAnchor
        {
            public readonly ARAnchor Anchor;
            public readonly WorldMapStore.WorldContent Content;

            public LoadedWorldAnchor(
                ARAnchor anchor,
                WorldMapStore.WorldContent content)
            {
                Anchor = anchor;
                Content = content;
            }
        }
#endif

        private void EmitPersistentWorldContent(
            Vector3 position,
            Quaternion rotation,
            string persistentId,
            WorldMapStore.WorldContent content)
        {
            if (
                _intents == null ||
                content == null ||
                string.IsNullOrWhiteSpace(content.worldContentId) ||
                !_emittedAnchorIds.Add(persistentId))
                return;
            float quality = Mathf.Clamp(content.quality, 0.72f, 0.94f);
            WorldMapStore.WorldAsset asset =
                string.IsNullOrWhiteSpace(content.assetId)
                    ? null
                    : _worldMap?.FindAsset(content.assetId);
            _intents.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "xreal-world-content:" + content.worldContentId,
                Producer = "xreal-world-map",
                TargetTrackId = content.targetTrackId,
                Component = "world_hologram",
                Anchor = new Dictionary<string, object>
                {
                    { "coordinate_space", "tracking_local" },
                    { "position", Point(position) },
                },
                Content = SpatialContent(quality, true,
                    new Dictionary<string, object>
                    {
                        { "anchor_quality", quality },
                         { "marker_id", content.worldContentId },
                         { "template_id", content.templateId },
                         { "preset_id", content.presetId },
                         { "archetype_id", content.archetypeId },
                         { "style_id", content.styleId },
                         { "animation_id", content.animationId },
                         { "accent_hex", content.accentHex },
                         { "secondary_hex", content.secondaryHex },
                         {
                             "scale",
                             Point(content.localScale.Value)
                         },
                         {
                             "local_euler",
                             Point(rotation.eulerAngles)
                         },
                         { "asset_id", asset?.assetId ?? string.Empty },
                         { "asset_mime", asset?.mimeType ?? string.Empty },
                         { "asset_sha256", asset?.sha256 ?? string.Empty },
                         { "asset_base64", asset?.base64Data ?? string.Empty },
                         {
                             "asset_file_path",
                             asset?.localFilePath ?? string.Empty
                         },
                         {
                             "motion_path",
                             string.IsNullOrWhiteSpace(content.motionPath)
                                 ? "static"
                                 : content.motionPath
                         },
                         {
                             "motion_radius_m",
                             content.motionRadiusM <= 0f
                                 ? 1.5f
                                 : content.motionRadiusM
                         },
                         {
                             "motion_speed",
                             content.motionSpeed <= 0f
                                 ? .8f
                                 : content.motionSpeed
                         },
                         { "motion_height_m", content.motionHeightM },
                         { "label", content.label },
                        { "subtitle", content.subtitle },
                        { "kind", "place" },
                        { "persistence", "xreal_anchor" },
                        { "world_map_id", _worldMap?.WorldMapId ?? string.Empty },
                        { "memory_write", false },
                    }),
                TruthLevel = "observed",
                Confidence = quality,
                Priority = 0.56,
                TtlMs = 86400000,
                EvidenceRefs = new List<string>
                {
                    "xreal:persistent-anchor:" + persistentId,
                    "world-map:" + (_worldMap?.WorldMapId ?? "unknown"),
                    "pose:xreal-head",
                    "depth:xreal-mesh",
                },
            });
        }

        private void EmitPersistentAnchor(
            Vector3 position,
            string persistentId,
            string state)
        {
            if (
                _intents == null ||
                string.IsNullOrWhiteSpace(persistentId) ||
                !_emittedAnchorIds.Add(persistentId))
                return;
            _intents.Emit(new UIIntent
            {
                UiIntentId = "xreal-anchor:" + persistentId,
                Producer = "ultralive",
                Component = "world_marker",
                Anchor = new Dictionary<string, object>
                {
                    { "coordinate_space", "tracking_local" },
                    { "position", Point(position) },
                },
                Content = SpatialContent(0.88f, true,
                    new Dictionary<string, object>
                    {
                        { "anchor_quality", 0.88f },
                        { "marker_id", "anchor-" + persistentId },
                        { "label", "ANCRE MÉMOIRE" },
                        { "subtitle", state == "saved"
                            ? "Enregistrée dans le monde"
                            : "Restaurée après relance" },
                        { "kind", "memory" },
                        { "distance_m", _camera == null
                            ? 0f
                            : Vector3.Distance(
                                _camera.transform.position, position) },
                    }),
                TruthLevel = "observed",
                Confidence = 0.88,
                Priority = 0.52,
                TtlMs = 86400000,
                EvidenceRefs = Evidence(
                    "xreal:persistent-anchor:" + persistentId),
            });
        }

        public bool SetBallisticTarget(Vector2 viewport)
        {
            if (
                !DepthReady ||
                _features == null ||
                !_features.IsActive(
                    AugmentedRealityFeatureRegistry.BallisticPreview) ||
                !TryDepthHit(viewport, out RaycastHit hit) ||
                !HandProviderReady())
                return false;
            _ballisticTarget = hit.point;
            _lastHandAt = 0f;
            EmitBallisticTarget(hit.point);
            return true;
        }

        private void EmitBallisticTarget(Vector3 target)
        {
            _intents?.Emit(new UIIntent
            {
                UiIntentId = "xreal-ballistic-target",
                Producer = "ultralive",
                Component = "world_marker",
                Anchor = new Dictionary<string, object>
                {
                    { "coordinate_space", "tracking_local" },
                    { "position", Point(target) },
                },
                Content = SpatialContent(0.86f, true,
                    new Dictionary<string, object>
                    {
                        { "anchor_quality", 0.86f },
                        { "marker_id", "ballistic-target" },
                        { "label", "CIBLE LUDIQUE" },
                        { "subtitle", "Bouge la main : trajectoire calculée" },
                        { "kind", "destination" },
                        { "distance_m", _camera == null
                            ? 0f
                            : Vector3.Distance(
                                _camera.transform.position, target) },
                    }),
                TruthLevel = "observed",
                Confidence = 0.86,
                Priority = 0.62,
                TtlMs = 120000,
                EvidenceRefs = Evidence("depth:xreal-ballistic-target"),
            });
        }

        private void UpdateBallisticPreview()
        {
            if (!_ballisticTarget.HasValue ||
                !TryGetTrackedHandTip(out Vector3 hand))
            {
                _lastHandAt = 0f;
                return;
            }
            float now = Time.unscaledTime;
            if (_lastHandAt <= 0f)
            {
                _lastHandPosition = hand;
                _lastHandAt = now;
                return;
            }
            float dt = now - _lastHandAt;
            if (dt < 0.025f || dt > 0.25f)
            {
                _lastHandPosition = hand;
                _lastHandAt = now;
                return;
            }
            Vector3 measuredVelocity = (hand - _lastHandPosition) / dt;
            _lastHandPosition = hand;
            _lastHandAt = now;
            if (
                now < _nextBallisticAt ||
                measuredVelocity.magnitude < 0.25f)
                return;
            _nextBallisticAt = now + 0.10f;
            EmitBallisticPaths(hand, measuredVelocity, _ballisticTarget.Value);
        }

        private bool TryGetTrackedHandTip(out Vector3 world)
        {
            world = default;
#if XREAL_SDK_PRESENT && XR_HANDS
            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (XRHandSubsystem subsystem in subsystems)
            {
                if (subsystem == null || !subsystem.running) continue;
                foreach (XRHand hand in new[]
                    { subsystem.rightHand, subsystem.leftHand })
                {
                    if (!hand.isTracked) continue;
                    XRHandJoint joint = hand.GetJoint(XRHandJointID.IndexTip);
                    if (!joint.TryGetPose(out UnityEngine.Pose pose)) continue;
                    XROrigin origin = FindAnyObjectByType<XROrigin>();
                    world = origin != null && origin.TrackablesParent != null
                        ? origin.TrackablesParent.TransformPoint(pose.position)
                        : pose.position;
                    return true;
                }
            }
#endif
            return false;
        }

        private void EmitBallisticPaths(
            Vector3 origin,
            Vector3 measuredVelocity,
            Vector3 target)
        {
            float flight = Mathf.Clamp(
                Vector3.Distance(origin, target) / 4f, 0.35f, 1.35f);
            Vector3 gravity = Physics.gravity;
            Vector3 idealVelocity =
                (target - origin - 0.5f * gravity * flight * flight) / flight;
            var paths = new List<Dictionary<string, object>>
            {
                Path("ideal", 0.92f, 0.88f,
                    ForecastProjectile(origin, idealVelocity, flight, 18)),
                Path("main_actuelle", 0.62f, 0.82f,
                    ForecastProjectile(origin, measuredVelocity, flight, 18)),
            };
            _intents?.Emit(new UIIntent
            {
                UiIntentId = "xreal-ballistic-preview",
                Producer = "ultralive",
                Component = "world_path",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(0.84f, true,
                    new Dictionary<string, object>
                    {
                        { "mode", "ballistic_preview" },
                        { "label", "PAPIER → CIBLE" },
                        { "horizon_s", flight },
                        { "paths", paths },
                        { "hand_pose_valid", true },
                        { "target_kind", "inanimate" },
                        { "safety_class", "recreational" },
                        { "weapon", false },
                    }),
                TruthLevel = "observed",
                Confidence = 0.84,
                Priority = 0.7,
                TtlMs = 800,
                EvidenceRefs = Evidence("xreal:xrhands-index-tip"),
            });
        }

        private static List<Dictionary<string, object>> ForecastProjectile(
            Vector3 origin,
            Vector3 velocity,
            float horizon,
            int steps)
        {
            var result = new List<Dictionary<string, object>>();
            int bounded = Mathf.Clamp(steps, 4, 32);
            for (int i = 0; i < bounded; i++)
            {
                float t = horizon * i / (bounded - 1f);
                result.Add(Point(
                    origin + velocity * t + 0.5f * Physics.gravity * t * t));
            }
            return result;
        }

        /// <summary>
        /// Start an honest direct-bearing AR guide without leaving the XREAL app.
        /// Android Geocoder resolves the spoken destination; GPS + compass are
        /// mandatory. This deliberately says CAP DIRECT, never street turn-by-turn.
        /// </summary>
        public bool StartNavigation(string destination)
        {
            if (_features == null || string.IsNullOrWhiteSpace(destination))
                return false;
            string clean = destination.Trim();
            if (
                _features.IsActive(AugmentedRealityFeatureRegistry.IndoorNavigation) &&
                _indoorMap != null &&
                _indoorMap.TryRoute(clean, out IndoorLiveMapStore.RouteResult indoor))
            {
                EmitIndoorNavigation(indoor);
                return true;
            }
            if (
                _navigationStarting ||
                !_features.IsEffective(
                    AugmentedRealityFeatureRegistry.StreetNavigation))
                return false;
#if UNITY_ANDROID && !UNITY_EDITOR
            _navigationStarting = true;
            ResolveNavigationAsync(clean);
            return true;
#else
            return false;
#endif
        }

        public bool NameCurrentIndoorPlace(string label)
        {
            bool ok =
                _features != null &&
                _features.IsActive(AugmentedRealityFeatureRegistry.IndoorNavigation) &&
                _indoorMap != null &&
                _indoorMap.NameCurrent(label);
            if (ok)
                EmitIndoorStatus(
                    "LIEU INTÉRIEUR MÉMORISÉ",
                    "Ce point est maintenant nommé « " + label.Trim() + " ».",
                    "indoor_place_named");
            return ok;
        }

        private void EmitIndoorNavigation(IndoorLiveMapStore.RouteResult route)
        {
            if (_intents == null || route == null ||
                route.TrackingLocalPoints.Count < 2)
                return;
            var points = new List<Dictionary<string, object>>();
            foreach (Vector3 point in route.TrackingLocalPoints)
                points.Add(Point(point));
            _intents.Emit(new UIIntent
            {
                UiIntentId = "xreal-indoor-navigation-" + HashId(route.MapId),
                Producer = "ultralive",
                Component = "world_navigation",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(
                    route.Quality,
                    DepthReady,
                    new Dictionary<string, object>
                    {
                        { "route_id", route.MapId + ":" +
                            HashId(route.Destination) },
                        { "destination", route.Destination },
                        { "eta", "trajet appris" },
                        { "distance_m", route.DistanceM },
                        { "map_quality", route.Quality },
                        { "route_quality", route.Quality },
                        { "route_points", points },
                        { "navigation_mode", "indoor_live_fingerprint_graph" },
                        { "turn_by_turn", true },
                        { "map_node_count", _indoorMap.NodeCount },
                        { "radio_relocalisation_quality",
                            _indoorMap.LastRelocalisationQuality },
                    }),
                TruthLevel = "observed",
                Confidence = route.Quality,
                Priority = 0.9,
                TtlMs = 12000,
                EvidenceRefs = Evidence(
                    "xreal:indoor-map:" + route.MapId),
            });
        }

        private void EmitIndoorStatus(string title, string text, string kind)
        {
            _intents?.Emit(new UIIntent
            {
                UiIntentId = "xreal-" + kind + "-" + DateTime.UtcNow.Ticks,
                Producer = "ultralive",
                Component = "context_card",
                Anchor = new Dictionary<string, object>
                {
                    { "type", "head_locked" },
                    { "side", "left" },
                },
                Content = new Dictionary<string, object>
                {
                    { "kind", kind },
                    { "title", title },
                    { "text", text },
                    { "memory_write", false },
                },
                TruthLevel = "observed",
                Confidence = 1.0,
                Priority = 0.72,
                TtlMs = 5000,
                EvidenceRefs = Evidence("xreal:indoor-map-local"),
            });
        }

        private bool NavigationSensorsReady()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PoseTracked() &&
                Input.location.status == LocationServiceStatus.Running &&
                Input.location.lastData.horizontalAccuracy > 0f &&
                Input.location.lastData.horizontalAccuracy <= 20f &&
                Input.compass.enabled &&
                Input.compass.headingAccuracy >= 0f;
#else
            return false;
#endif
        }

        private async void EnsureWorldTextLocationAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_worldTextLocationStarting) return;
            _worldTextLocationStarting = true;
            try
            {
                if (!FineLocationPermissionReady(request: true)) return;
                if (Input.location.status == LocationServiceStatus.Stopped)
                    Input.location.Start(15f, 5f);
                for (int i = 0;
                    i < 300 &&
                    Input.location.status == LocationServiceStatus.Initializing;
                    i++)
                    await Awaitable.NextFrameAsync();
                PublishWorldTextLocation();
            }
            finally
            {
                _worldTextLocationStarting = false;
            }
#endif
        }

        private void PublishWorldTextLocation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (
                _transport == null ||
                _features == null ||
                !(
                    _features.IsActive(AugmentedRealityFeatureRegistry.WorldText) ||
                    _features.IsActive(AugmentedRealityFeatureRegistry.WeatherContext) ||
                    _features.IsActive(AugmentedRealityFeatureRegistry.Planetarium)
                ))
                return;
            if (Input.location.status == LocationServiceStatus.Stopped)
            {
                EnsureWorldTextLocationAsync();
                return;
            }
            if (Input.location.status != LocationServiceStatus.Running) return;
            LocationInfo info = Input.location.lastData;
            if (
                info.horizontalAccuracy <= 0f ||
                info.horizontalAccuracy > 50f ||
                double.IsNaN(info.latitude) ||
                double.IsNaN(info.longitude))
                return;
            _transport.SendContractMessage(ContractJson.Serialize(new
            {
                type = "device_location",
                latitude = info.latitude,
                longitude = info.longitude,
                altitude_m = info.altitude,
                horizontal_accuracy_m = info.horizontalAccuracy,
                captured_at_utc = DateTime.UtcNow.ToString("O"),
                purpose = "augmented_context",
                tracking_position = new
                {
                    x = _camera != null ? _camera.transform.position.x : 0f,
                    y = _camera != null ? _camera.transform.position.y : 0f,
                    z = _camera != null ? _camera.transform.position.z : 0f,
                },
                true_heading_deg = Input.compass.trueHeading,
                heading_accuracy_deg = Input.compass.headingAccuracy,
                north_calibrated =
                    Input.compass.enabled &&
                    Input.compass.headingAccuracy >= 0f &&
                    Input.compass.headingAccuracy <= 30f,
                world_north_yaw_deg = _camera != null
                    ? _camera.transform.eulerAngles.y - Input.compass.trueHeading
                    : 0f,
                calibration_id = CalibrationId,
            }));
#endif
        }

        private void TickNavigation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            LocationInfo current = Input.location.lastData;
            if (
                _navigationDestination != null &&
                _navigationDestination.RoutePoints.Count >= 2)
            {
                EmitRouteNavigation();
                if (
                    DistanceToRouteMeters(current) > 25d &&
                    Time.unscaledTime >= _nextRouteRequestAt)
                    BeginRouteRequest(
                        current.latitude,
                        current.longitude,
                        _navigationDestination.Latitude,
                        _navigationDestination.Longitude);
            }
            else
            {
                EmitDirectNavigation();
                if (
                    _navigationDestination != null &&
                    Time.unscaledTime >= _nextRouteRequestAt)
                    BeginRouteRequest(
                        current.latitude,
                        current.longitude,
                        _navigationDestination.Latitude,
                        _navigationDestination.Longitude);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private async void ResolveNavigationAsync(string destination)
        {
            try
            {
                if (!RadioPermissionReady(request: true))
                    throw new InvalidOperationException(
                        "precise location permission is required");
                Input.compass.enabled = true;
                if (Input.location.status == LocationServiceStatus.Stopped)
                    Input.location.Start(5f, 1f);
                for (int i = 0;
                    i < 300 &&
                    Input.location.status == LocationServiceStatus.Initializing;
                    i++)
                    await Awaitable.NextFrameAsync();
                if (Input.location.status != LocationServiceStatus.Running)
                    throw new InvalidOperationException(
                        "Android location service unavailable");
                for (int i = 0;
                    i < 120 && Input.compass.headingAccuracy < 0f;
                    i++)
                    await Awaitable.NextFrameAsync();
                if (!NavigationSensorsReady())
                    throw new InvalidOperationException(
                        "GPS/compass accuracy is below product threshold");

                await Awaitable.BackgroundThreadAsync();
                double[] coordinate = ResolveWithAndroidGeocoder(destination);
                await Awaitable.MainThreadAsync();
                if (coordinate == null)
                    throw new InvalidOperationException(
                        "destination not resolved by Android Geocoder");
                _navigationDestination = new GeoDestination
                {
                    Name = destination,
                    Latitude = coordinate[0],
                    Longitude = coordinate[1],
                };
                LocationInfo origin = Input.location.lastData;
                float worldNorthYaw =
                    _camera.transform.eulerAngles.y - Input.compass.trueHeading;
                Vector3 groundOrigin =
                    _camera.transform.position + Vector3.down * 1.25f;
                _worldMap?.SetGeoOrigin(
                    origin.latitude,
                    origin.longitude,
                    origin.altitude,
                    origin.horizontalAccuracy,
                    worldNorthYaw,
                    groundOrigin,
                    force: true);
                _features.SetLocalCapability(
                    AugmentedRealityFeatureRegistry.StreetNavigation, true);
                BeginRouteRequest(
                    origin.latitude,
                    origin.longitude,
                    coordinate[0],
                    coordinate[1]);
                EmitDirectNavigation();
            }
            catch (Exception ex)
            {
                await Awaitable.MainThreadAsync();
                _navigationDestination = null;
                _features?.SetLocalCapability(
                    AugmentedRealityFeatureRegistry.StreetNavigation, false);
                LastProviderError =
                    "xreal_navigation:" + ex.GetType().Name + ":" + ex.Message;
                Debug.LogWarning("[XrealSpatialProvider] " + LastProviderError);
                EmitNavigationUnavailable(destination, ex.Message);
            }
            finally
            {
                _navigationStarting = false;
            }
        }

        private void BeginRouteRequest(
            double originLatitude,
            double originLongitude,
            double destinationLatitude,
            double destinationLongitude)
        {
            if (_routeRequestInFlight) return;
            _routeRequestInFlight = true;
            _nextRouteRequestAt = Time.unscaledTime + 20f;
            StartCoroutine(RequestRoutePolyline(
                originLatitude,
                originLongitude,
                destinationLatitude,
                destinationLongitude));
        }

        private IEnumerator RequestRoutePolyline(
            double originLatitude,
            double originLongitude,
            double destinationLatitude,
            double destinationLongitude)
        {
            if (
                _pairing == null ||
                string.IsNullOrWhiteSpace(_pairing.ActiveBaseUrl) ||
                !_pairing.TryGetActiveSession(
                    out string sessionId, out string token))
            {
                _routeRequestInFlight = false;
                yield break;
            }
            string json = ContractJson.Serialize(new
            {
                session_id = sessionId,
                token,
                origin_latitude = originLatitude,
                origin_longitude = originLongitude,
                destination_latitude = destinationLatitude,
                destination_longitude = destinationLongitude,
            });
            byte[] body = Encoding.UTF8.GetBytes(json);
            using var request = new UnityWebRequest(
                _pairing.ActiveBaseUrl.TrimEnd('/') + "/navigation/route",
                UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            request.SetRequestHeader(
                "Content-Type", "application/json; charset=utf-8");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                LastProviderError =
                    "xreal_route_provider:" + request.responseCode + ":" +
                    (request.error ?? "unavailable");
                _routeRequestInFlight = false;
                yield break;
            }
            try
            {
                JObject payload = JObject.Parse(request.downloadHandler.text);
                JArray points = payload["points"] as JArray;
                if (points == null || points.Count < 2 || points.Count > 512)
                    throw new InvalidDataException(
                        "route points cardinality is invalid");
                var decoded = new List<GeoPoint>(points.Count);
                foreach (JToken tokenPoint in points)
                {
                    if (!(tokenPoint is JArray pair) || pair.Count < 2)
                        throw new InvalidDataException("route point is invalid");
                    double latitude = pair[0].Value<double>();
                    double longitude = pair[1].Value<double>();
                    if (
                        double.IsNaN(latitude) || double.IsInfinity(latitude) ||
                        double.IsNaN(longitude) || double.IsInfinity(longitude) ||
                        latitude < -90d || latitude > 90d ||
                        longitude < -180d || longitude > 180d)
                        throw new InvalidDataException(
                            "route point is non-finite");
                    decoded.Add(new GeoPoint
                    {
                        Latitude = latitude,
                        Longitude = longitude,
                    });
                }
                if (_navigationDestination != null)
                {
                    _navigationDestination.RoutePoints.Clear();
                    _navigationDestination.RoutePoints.AddRange(decoded);
                    _navigationDestination.RouteDistanceM =
                        Math.Max(0d, payload.Value<double?>("distance_m") ?? 0d);
                    _navigationDestination.DurationS =
                        Math.Max(0d, payload.Value<double?>("duration_s") ?? 0d);
                    _navigationDestination.Provider =
                        (payload.Value<string>("provider") ?? "route-provider")
                        .Trim();
                    _broker?.Withdraw("xreal-direct-navigation");
                    EmitRouteNavigation();
                }
            }
            catch (Exception ex)
            {
                LastProviderError =
                    "xreal_route_decode:" + ex.GetType().Name + ":" + ex.Message;
                // Honest direct-bearing navigation remains visible.
            }
            _routeRequestInFlight = false;
        }

        private static double[] ResolveWithAndroidGeocoder(string destination)
        {
            using var unity = new AndroidJavaClass(
                "com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                unity.GetStatic<AndroidJavaObject>("currentActivity");
            using var geocoder = new AndroidJavaObject(
                "android.location.Geocoder", activity);
            using AndroidJavaObject results =
                geocoder.Call<AndroidJavaObject>(
                    "getFromLocationName", destination, 1);
            if (results == null || results.Call<int>("size") < 1) return null;
            using AndroidJavaObject address =
                results.Call<AndroidJavaObject>("get", 0);
            if (address == null) return null;
            return new[]
            {
                address.Call<double>("getLatitude"),
                address.Call<double>("getLongitude"),
            };
        }

        private void EmitDirectNavigation()
        {
            if (
                _navigationDestination == null ||
                _intents == null ||
                _camera == null ||
                !NavigationSensorsReady())
                return;
            LocationInfo origin = Input.location.lastData;
            double distance = HaversineMeters(
                origin.latitude,
                origin.longitude,
                _navigationDestination.Latitude,
                _navigationDestination.Longitude);
            double bearing = BearingDegrees(
                origin.latitude,
                origin.longitude,
                _navigationDestination.Latitude,
                _navigationDestination.Longitude);
            float worldNorthYaw =
                _camera.transform.eulerAngles.y - Input.compass.trueHeading;
            Vector3 direction = Quaternion.Euler(
                0f, worldNorthYaw + (float)bearing, 0f) * Vector3.forward;
            direction.y = 0f;
            direction.Normalize();
            Vector3 start = _camera.transform.position +
                Vector3.down * 1.25f + direction * 0.8f;
            float visibleLength = Mathf.Clamp((float)distance, 2f, 18f);
            var points = new List<Dictionary<string, object>>();
            for (int i = 0; i < 12; i++)
                points.Add(Point(
                    start + direction * (visibleLength * i / 11f)));
            float sensorQuality = Mathf.Clamp01(
                1f - (origin.horizontalAccuracy - 3f) / 30f);
            float quality = Mathf.Clamp(sensorQuality, 0.7f, 0.92f);
            _intents.Emit(new UIIntent
            {
                UiIntentId = "xreal-direct-navigation",
                Producer = "ultralive",
                Component = "world_navigation",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(quality, DepthReady,
                    new Dictionary<string, object>
                    {
                        { "route_id", "direct-gps-" +
                            HashId(_navigationDestination.Name) },
                        { "destination", "CAP DIRECT — " +
                            _navigationDestination.Name },
                        { "eta", "GPS direct" },
                        { "distance_m", distance },
                        { "map_quality", quality },
                        { "route_quality", quality },
                        { "route_points", points },
                        { "navigation_mode", "direct_bearing" },
                        { "turn_by_turn", false },
                        { "gps_accuracy_m", origin.horizontalAccuracy },
                        { "heading_accuracy_deg",
                            Input.compass.headingAccuracy },
                    }),
                TruthLevel = "observed",
                Confidence = quality,
                Priority = 0.84,
                TtlMs = 3000,
                EvidenceRefs = new List<string>
                {
                    "android:gps",
                    "android:compass",
                    "android:geocoder:" +
                        HashId(_navigationDestination.Name),
                    "pose:xreal-head",
                },
            });
        }

        private void EmitRouteNavigation()
        {
            if (
                _navigationDestination == null ||
                _navigationDestination.RoutePoints.Count < 2 ||
                _worldMap == null ||
                _intents == null ||
                !NavigationSensorsReady())
                return;
            LocationInfo current = Input.location.lastData;
            List<Dictionary<string, object>> points =
                BuildVisibleRoutePoints(current);
            if (points.Count < 2)
            {
                EmitDirectNavigation();
                return;
            }
            double directRemaining = HaversineMeters(
                current.latitude,
                current.longitude,
                _navigationDestination.Latitude,
                _navigationDestination.Longitude);
            double distance = Math.Max(
                directRemaining,
                Math.Min(
                    _navigationDestination.RouteDistanceM,
                    _navigationDestination.RouteDistanceM *
                    directRemaining /
                    Math.Max(1d, HaversineMeters(
                        _navigationDestination.RoutePoints[0].Latitude,
                        _navigationDestination.RoutePoints[0].Longitude,
                        _navigationDestination.Latitude,
                        _navigationDestination.Longitude))));
            float sensorQuality = Mathf.Clamp01(
                1f - (current.horizontalAccuracy - 3f) / 30f);
            float quality = Mathf.Clamp(sensorQuality, 0.7f, 0.92f);
            string eta = _navigationDestination.DurationS > 0d
                ? Math.Max(1, (int)Math.Ceiling(
                    _navigationDestination.DurationS / 60d)) + " min"
                : "itinéraire";
            _intents.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "xreal-route-navigation",
                Producer = "ultralive",
                Component = "world_navigation",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(quality, DepthReady,
                    new Dictionary<string, object>
                    {
                        { "route_id", "route-" +
                            HashId(_navigationDestination.Name) },
                        { "destination", _navigationDestination.Name },
                        { "eta", eta },
                        { "distance_m", distance },
                        { "map_quality", quality },
                        { "route_quality", quality },
                        { "route_points", points },
                        { "navigation_mode", "road_polyline" },
                        { "turn_by_turn", true },
                        { "gps_accuracy_m", current.horizontalAccuracy },
                        { "heading_accuracy_deg",
                            Input.compass.headingAccuracy },
                    }),
                TruthLevel = "observed",
                Confidence = quality,
                Priority = 0.9,
                TtlMs = 3000,
                EvidenceRefs = new List<string>
                {
                    "android:gps",
                    "android:compass",
                    "route:" + (_navigationDestination.Provider ?? "provider"),
                    "pose:xreal-head",
                },
            });
        }

        private List<Dictionary<string, object>> BuildVisibleRoutePoints(
            LocationInfo current)
        {
            var output = new List<Dictionary<string, object>>();
            List<GeoPoint> route = _navigationDestination.RoutePoints;
            int nearest = 0;
            double nearestDistance = double.MaxValue;
            for (int i = 0; i < route.Count; i++)
            {
                double distance = HaversineMeters(
                    current.latitude,
                    current.longitude,
                    route[i].Latitude,
                    route[i].Longitude);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }
            Vector3 localCurrent =
                _camera.transform.position + Vector3.down * 1.25f;
            output.Add(Point(localCurrent));
            Vector3 previous = localCurrent;
            float visibleDistance = 0f;
            for (int i = nearest; i < route.Count && output.Count < 128; i++)
            {
                if (!_worldMap.TryGeoToLocal(
                        route[i].Latitude,
                        route[i].Longitude,
                        _worldMap.Document.originAltitudeM,
                        out Vector3 local))
                    continue;
                local.y = localCurrent.y;
                float segment = Vector3.Distance(previous, local);
                if (segment < 0.55f) continue;
                int pieces = Mathf.Max(1, Mathf.CeilToInt(segment / 12f));
                for (int piece = 1;
                    piece <= pieces && output.Count < 128;
                    piece++)
                    output.Add(Point(Vector3.Lerp(
                        previous, local, piece / (float)pieces)));
                visibleDistance += segment;
                previous = local;
                if (visibleDistance >= 150f) break;
            }
            return output;
        }

        private double DistanceToRouteMeters(LocationInfo current)
        {
            if (
                _navigationDestination == null ||
                _navigationDestination.RoutePoints.Count == 0)
                return double.MaxValue;
            double nearest = double.MaxValue;
            foreach (GeoPoint point in _navigationDestination.RoutePoints)
                nearest = Math.Min(
                    nearest,
                    HaversineMeters(
                        current.latitude,
                        current.longitude,
                        point.Latitude,
                        point.Longitude));
            return nearest;
        }

        private void EmitNavigationUnavailable(string destination, string detail)
        {
            if (_intents == null) return;
            _intents.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "xreal-navigation-unavailable-" + DateTime.UtcNow.Ticks,
                Producer = "ultralive",
                Component = "context_card",
                Anchor = new Dictionary<string, object>
                {
                    { "type", "head_locked" },
                    { "side", "left" },
                },
                Content = new Dictionary<string, object>
                {
                    { "kind", "navigation_unavailable" },
                    { "title", "NAVIGATION AR INDISPONIBLE" },
                    { "text", "GPS, boussole ou destination non qualifiés. " +
                        "Ouvre Maps pour « " + destination + " »." },
                    { "source", string.IsNullOrWhiteSpace(detail)
                        ? "XREAL spatial provider"
                        : detail },
                },
                TruthLevel = "observed",
                Confidence = 1.0,
                Priority = 0.9,
                TtlMs = 8000,
                EvidenceRefs = Evidence("xreal:navigation-failed"),
            });
        }

        private static double HaversineMeters(
            double lat1, double lon1, double lat2, double lon2)
        {
            const double radius = 6371000.0;
            double p1 = lat1 * Math.PI / 180.0;
            double p2 = lat2 * Math.PI / 180.0;
            double dp = (lat2 - lat1) * Math.PI / 180.0;
            double dl = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) +
                Math.Cos(p1) * Math.Cos(p2) *
                Math.Sin(dl / 2) * Math.Sin(dl / 2);
            return radius * 2.0 * Math.Atan2(
                Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }

        private static double BearingDegrees(
            double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180.0;
            double p2 = lat2 * Math.PI / 180.0;
            double dl = (lon2 - lon1) * Math.PI / 180.0;
            double y = Math.Sin(dl) * Math.Cos(p2);
            double x = Math.Cos(p1) * Math.Sin(p2) -
                Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
            return (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;
        }
#endif

        private void SetLocalCapabilities(bool depthReady)
        {
            if (_features == null) return;
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.WorldLabels, depthReady);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.WorldStyling,
                depthReady && _freeGuyMeshMaterial != null);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.TrajectoryForecast, depthReady);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.EventVision, depthReady);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.ArMeasurement, depthReady);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.SpatialKeyboard, depthReady);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.DepthOcclusion,
                depthReady && _depthOcclusionMaterial != null);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.PersistentAnchors,
                depthReady && AnchorProviderReady());
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.BallisticPreview,
                depthReady && HandProviderReady());
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.AutomaticWorldFx,
                depthReady);
#if UNITY_ANDROID && !UNITY_EDITOR
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.RadioField,
                depthReady && RadioPermissionReady(
                    request: _features.IsSelected(
                        AugmentedRealityFeatureRegistry.RadioField)));
#else
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.RadioField,
                false);
#endif
            // Street navigation still needs a calibrated route provider; it is
            // advertised only by StartNavigation once origin + route are proven.
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.StreetNavigation,
                _navigationDestination != null && NavigationSensorsReady());
#if UNITY_ANDROID && !UNITY_EDITOR
            bool trackedPoseReady = isActiveAndEnabled && PoseTracked();
            bool indoorReady =
                trackedPoseReady &&
                _indoorMap != null &&
                IndoorFingerprintPermissionReady(
                    request: _features.IsSelected(
                        AugmentedRealityFeatureRegistry.IndoorNavigation));
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.IndoorNavigation,
                indoorReady);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.Planetarium,
                trackedPoseReady);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.WeatherContext,
                FineLocationPermissionReady(
                    request: _features.IsSelected(
                        AugmentedRealityFeatureRegistry.WeatherContext)));
#else
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.IndoorNavigation, false);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.Planetarium, false);
            _features.SetLocalCapability(
                AugmentedRealityFeatureRegistry.WeatherContext, false);
#endif
        }

        private void SampleIndoorFingerprint()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (
                _camera == null ||
                _indoorMap == null ||
                !PoseTracked() ||
                !IndoorFingerprintPermissionReady(request: true) ||
                !EnsureIndoorFingerprintBridge())
                return;
            Input.compass.enabled = true;
            float northYaw =
                Input.compass.headingAccuracy >= 0f &&
                Input.compass.headingAccuracy <= 30f
                    ? _camera.transform.eulerAngles.y - Input.compass.trueHeading
                    : 0f;
            try
            {
                string fingerprint = _indoorFingerprint.Call<string>("snapshotJson");
                if (_indoorMap.Observe(
                    _camera.transform.position,
                    northYaw,
                    fingerprint,
                    out string state))
                {
                    LastProviderError = state == "map_full"
                        ? "indoor_map_full"
                        : string.Empty;
                }
            }
            catch (Exception ex)
            {
                LastProviderError =
                    "indoor_fingerprint:" + ex.GetType().Name;
                StopIndoorFingerprint();
            }
#endif
        }

        private bool EnsureIndoorFingerprintBridge()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_indoorFingerprint != null) return true;
            try
            {
                using var player =
                    new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject context =
                    activity.Call<AndroidJavaObject>("getApplicationContext");
                _indoorFingerprint = new AndroidJavaObject(
                    "com.mlomega.xr.livetransport.IndoorFingerprintBridge",
                    context);
                return _indoorFingerprint.Call<bool>("start");
            }
            catch (Exception ex)
            {
                LastProviderError =
                    "indoor_bridge:" + ex.GetType().Name;
                StopIndoorFingerprint();
                return false;
            }
#else
            return false;
#endif
        }

        private void StopIndoorFingerprint()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_indoorFingerprint == null) return;
            try { _indoorFingerprint.Call("stop"); }
            catch (Exception) { }
            _indoorFingerprint.Dispose();
            _indoorFingerprint = null;
#endif
        }

        private void SampleCurrentWifi()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!PoseTracked() || !RadioPermissionReady(request: false)) return;
            try
            {
                using var player =
                    new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject context =
                    activity.Call<AndroidJavaObject>("getApplicationContext");
                using AndroidJavaObject wifi =
                    context.Call<AndroidJavaObject>("getSystemService", "wifi");
                using AndroidJavaObject info =
                    wifi.Call<AndroidJavaObject>("getConnectionInfo");
                string bssid = info?.Call<string>("getBSSID");
                int rssi = info?.Call<int>("getRssi") ?? -127;
                if (
                    string.IsNullOrWhiteSpace(bssid) ||
                    bssid == "02:00:00:00:00:00" ||
                    rssi < -120 || rssi > -10)
                    return;
                Vector3 position = _pose.Latest.Position;
                if (
                    _radioSamples.Count > 0 &&
                    Vector3.Distance(
                        _radioSamples[_radioSamples.Count - 1].Position,
                        position) < 0.35f)
                    return;
                _radioSamples.Add(new RadioSample
                {
                    Position = position,
                    Rssi = rssi,
                    Id = "radio-" + HashId(bssid),
                });
                while (_radioSamples.Count > 24) _radioSamples.RemoveAt(0);
                if (_radioSamples.Count >= 2) EmitRadioField();
            }
            catch (Exception ex)
            {
                LastProviderError = "radio_permission_or_api:" + ex.GetType().Name;
                _features?.SetLocalCapability(
                    AugmentedRealityFeatureRegistry.RadioField, false);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool FineLocationPermissionReady(bool request)
        {
            const string permission = "android.permission.ACCESS_FINE_LOCATION";
            bool ready =
                UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission);
            if (!ready && request)
                UnityEngine.Android.Permission.RequestUserPermission(permission);
            return ready;
        }

        private static bool RadioPermissionReady(bool request)
        {
            const string location = "android.permission.ACCESS_FINE_LOCATION";
            const string nearby = "android.permission.NEARBY_WIFI_DEVICES";
            bool locationReady =
                UnityEngine.Android.Permission.HasUserAuthorizedPermission(location);
            bool nearbyReady =
                UnityEngine.Android.Permission.HasUserAuthorizedPermission(nearby);
            if (request)
            {
                if (!locationReady)
                    UnityEngine.Android.Permission.RequestUserPermission(location);
                if (!nearbyReady)
                    UnityEngine.Android.Permission.RequestUserPermission(nearby);
            }
            // Android versions before 13 do not expose NEARBY_WIFI_DEVICES.
            int sdk = 0;
            try
            {
                using var version =
                    new AndroidJavaClass("android.os.Build$VERSION");
                sdk = version.GetStatic<int>("SDK_INT");
            }
            catch
            {
                return false;
            }
            return locationReady && (sdk < 33 || nearbyReady);
        }

        private static bool IndoorFingerprintPermissionReady(bool request)
        {
            const string location = "android.permission.ACCESS_FINE_LOCATION";
            const string bluetooth = "android.permission.BLUETOOTH_SCAN";
            bool locationReady =
                UnityEngine.Android.Permission.HasUserAuthorizedPermission(location);
            int sdk;
            try
            {
                using var version =
                    new AndroidJavaClass("android.os.Build$VERSION");
                sdk = version.GetStatic<int>("SDK_INT");
            }
            catch
            {
                return false;
            }
            bool bluetoothReady =
                sdk < 31 ||
                UnityEngine.Android.Permission.HasUserAuthorizedPermission(bluetooth);
            if (request)
            {
                if (!locationReady)
                    UnityEngine.Android.Permission.RequestUserPermission(location);
                if (!bluetoothReady)
                    UnityEngine.Android.Permission.RequestUserPermission(bluetooth);
            }
            return locationReady && bluetoothReady;
        }
#endif

        private void EmitRadioField()
        {
            var samples = new List<Dictionary<string, object>>();
            foreach (RadioSample sample in _radioSamples)
            {
                samples.Add(new Dictionary<string, object>
                {
                    { "position", Point(sample.Position) },
                    { "rssi_dbm", sample.Rssi },
                    { "source", "wifi" },
                    { "network_id", sample.Id },
                });
            }
            _intents?.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "xreal-radio-field",
                Producer = "ultralive",
                Component = "world_radio",
                Anchor = TrackingAnchor(),
                Content = SpatialContent(0.72f, false, new Dictionary<string, object>
                {
                    { "pseudonymized", true },
                    { "samples", samples },
                }),
                TruthLevel = "observed",
                Confidence = 0.72,
                Priority = 0.56,
                TtlMs = 12000,
                EvidenceRefs = Evidence("android:wifi-rssi"),
            });
        }

        private static Dictionary<string, object> TrackingAnchor() =>
            new Dictionary<string, object>
            {
                { "coordinate_space", "tracking_local" },
            };

        private static Dictionary<string, object> SpatialContent(
            float quality,
            bool depth,
            Dictionary<string, object> additions)
        {
            var content = new Dictionary<string, object>
            {
                { "pose_valid", true },
                { "depth_valid", depth },
                { "calibration_id", CalibrationId },
                { "spatial_quality", quality },
            };
            foreach (KeyValuePair<string, object> item in additions)
                content[item.Key] = item.Value;
            return content;
        }

        private static Dictionary<string, object> Point(Vector3 p) =>
            new Dictionary<string, object>
            {
                { "x", p.x },
                { "y", p.y },
                { "z", p.z },
            };

        private static List<Dictionary<string, object>> Interpolate(
            Vector3 a, Vector3 b, int count)
        {
            var points = new List<Dictionary<string, object>>();
            for (int i = 0; i < count; i++)
                points.Add(Point(Vector3.Lerp(a, b, i / (count - 1f))));
            return points;
        }

        private static Dictionary<string, object> Path(
            string id,
            float probability,
            float quality,
            List<Dictionary<string, object>> points) =>
            new Dictionary<string, object>
            {
                { "path_id", id },
                { "probability", probability },
                { "quality", Mathf.Clamp01(quality) },
                { "points", points },
            };

        private static List<string> Evidence(string value) =>
            new List<string>
            {
                string.IsNullOrWhiteSpace(value) ? "xreal:live" : value,
                "pose:xreal-head",
                "depth:xreal-mesh",
            };

        private static bool TryViewportPoint(
            Dictionary<string, object> entity,
            SceneDelta delta,
            out Vector2 viewport)
        {
            viewport = default;
            if (!entity.TryGetValue("bbox", out object raw) || raw == null)
                return false;
            Newtonsoft.Json.Linq.JArray box;
            try
            {
                box = raw as Newtonsoft.Json.Linq.JArray ??
                    Newtonsoft.Json.Linq.JArray.FromObject(raw);
            }
            catch
            {
                return false;
            }
            if (box.Count < 4) return false;
            float x1 = (float)box[0];
            float y1 = (float)box[1];
            float x2 = (float)box[2];
            float y2 = (float)box[3];
            if (x2 <= x1 || y2 <= y1) return false;
            float width = delta.FrameWidth.Value;
            float height = delta.FrameHeight.Value;
            // Detector pixels use a top-left origin. Unity viewport rays use
            // bottom-left; feet/bottom-center are more stable for people while
            // the centre remains appropriate for compact objects.
            string kind = ReadString(entity, "kind");
            float y = string.Equals(kind, "person", StringComparison.OrdinalIgnoreCase)
                ? y2
                : (y1 + y2) * 0.5f;
            viewport = new Vector2(
                Mathf.Clamp01(((x1 + x2) * 0.5f) / width),
                Mathf.Clamp01(1f - y / height));
            return true;
        }

        private static string ReadString(
            Dictionary<string, object> values, string key) =>
            values != null &&
            values.TryGetValue(key, out object raw) &&
            raw != null
                ? raw.ToString()
                : string.Empty;

        private static double ReadNumber(
            Dictionary<string, object> values,
            string key,
            double fallback)
        {
            if (
                values == null ||
                !values.TryGetValue(key, out object raw) ||
                raw == null)
                return fallback;
            return double.TryParse(
                raw.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double value)
                ? value
                : fallback;
        }

        private static string NormaliseMarkerKind(string kind)
        {
            string value = (kind ?? string.Empty).Trim().ToLowerInvariant();
            return value == "person" ? "person" :
                value == "sign" ? "sign" :
                value == "storefront" ? "storefront" :
                "object";
        }

        private static string HashId(string value)
        {
            using SHA256 sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
            var text = new StringBuilder(12);
            for (int i = 0; i < 6; i++) text.Append(bytes[i].ToString("x2"));
            return text.ToString();
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static Component FindBehaviour(Type type)
        {
            foreach (MonoBehaviour behaviour in
                FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour != null && type.IsInstanceOfType(behaviour))
                    return behaviour;
            }
            return null;
        }

        private static void SetMember(object target, string name, object value)
        {
            if (target == null) return;
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }
            FieldInfo field = type.GetField(
                "m_" + char.ToUpperInvariant(name[0]) + name.Substring(1),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }

        private sealed class TrackHistory
        {
            public bool HasValue;
            public Vector3 Previous;
            public Vector3 Position;
            public float At;
            public string Label;
            public string Kind;
            public int SeenCount;
            public float NextWorldFxAt;
            public float NextSurfaceAt;
        }

        private sealed class KeyboardPlacement
        {
            public Vector3 Origin;
            public Vector3 Right;
            public Vector3 Forward;
            public Vector3 Normal;
            public float Width;
            public float Height;
        }

        private sealed class RadioSample
        {
            public Vector3 Position;
            public float Rssi;
            public string Id;
        }

        private sealed class GeoDestination
        {
            public string Name;
            public double Latitude;
            public double Longitude;
            public double RouteDistanceM;
            public double DurationS;
            public string Provider;
            public readonly List<GeoPoint> RoutePoints =
                new List<GeoPoint>();
        }

        private sealed class GeoPoint
        {
            public double Latitude;
            public double Longitude;
        }
    }

    /// <summary>Marker copied by ARMeshManager onto every generated mesh.</summary>
    public sealed class XrealSpatialMeshTag : MonoBehaviour { }
}
