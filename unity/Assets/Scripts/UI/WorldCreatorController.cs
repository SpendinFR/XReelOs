using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using MLOmega.XR.UI.Components;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Isolated Atelier UI. The phone is the dense editing surface while the
    /// glasses show the spatial preview. It never pairs, records or opens Memory.
    /// </summary>
    public sealed partial class WorldCreatorController : MonoBehaviour
    {
        private static readonly string[] Categories =
        {
            "cinematic", "urban", "commerce", "home",
            "navigation", "mobility", "information",
        };

        [SerializeField] private MonoBehaviour _spatialBehaviour;
        [SerializeField] private Camera _camera;
        [SerializeField] private WorldMapDocumentExchange _exchange;
        [SerializeField] private bool _osOnlyMode;

        private IWorldCreatorSpatialProvider Spatial =>
            _spatialBehaviour as IWorldCreatorSpatialProvider;
        private List<WorldCreatorCatalog.Entry> _visible =
            new List<WorldCreatorCatalog.Entry>();
        private WorldCreatorCatalog.Entry _selected;
        private WorldHologram _preview;
        private UIComponentContext _previewContext;
        private string _category = "cinematic";
        private string _label = "HOLOGRAMME";
        private string _subtitle = "ATELIER // MONDE AUGMENTÉ";
        private string _status = "INITIALISATION DU MESH…";
        private string _lastCreatedId;
        private string _pendingAssetId;
        private bool _dynamicMode;
        private string _dynamicTargetLabel = string.Empty;
        private int _dynamicKindIndex;
        private int _attachmentIndex;
        private int _motionIndex;
        private int _managedIndex;
        private int _mapIndex;
        private int _page;
        private float _uniformScale = 1f;
        private float _yaw;
        private float _nextPreviewAt;
        private Vector3 _previewPosition;
        private Quaternion _previewRotation;
        private bool _hasPreviewPose;
        private Vector2 _scroll;
        private GUIStyle _title;
        private GUIStyle _panel;
        private GUIStyle _button;
        private GUIStyle _selectedButton;
        private GUIStyle _labelStyle;
        private GUIStyle _field;
        private Canvas _spatialDeck;
        private RectTransform _spatialDeckRect;
        private bool _deckPoseInitialized;
        private TextMeshProUGUI _deckStatus;
        private TMP_InputField _deckLabel;
        private TMP_InputField _deckSubtitle;
        private readonly List<Button> _deckPresetButtons =
            new List<Button>();
        private readonly List<TextMeshProUGUI> _deckPresetLabels =
            new List<TextMeshProUGUI>();
        private readonly List<Button> _deckCategoryButtons =
            new List<Button>();
        private readonly List<Graphic> _deckHitGraphics =
            new List<Graphic>();
        private readonly List<GameObject> _deckExpandedRoots =
            new List<GameObject>();
        private TextMeshProUGUI _deckPage;
        private TextMeshProUGUI _deckScale;
        private TextMeshProUGUI _deckAsset;
        private TextMeshProUGUI _deckCommitLabel;
        private TextMeshProUGUI _deckModeLabel;
        private TextMeshProUGUI _deckKindLabel;
        private TextMeshProUGUI _deckAttachmentLabel;
        private TextMeshProUGUI _deckManagedLabel;
        private TextMeshProUGUI _deckMapLabel;
        private TextMeshProUGUI _deckMotionLabel;
        private TMP_InputField _deckTarget;
        private Image _deckMoveHandle;
        private Image _deckResizeHandle;
        private Image _deckResizeHandleRight;
        private Image _deckDepthHandle;
        private Image _deckTiltHandle;
        private TextMeshProUGUI _deckCloseHandle;
        private Button _deckBlockButton;
        private Image _deckWindowRim;
        private Image _deckWindowSurface;
        private Image _deckHeaderSurface;
        private Button _deckPortraitButton;
        private Button _deckLandscapeButton;
        private readonly Dictionary<RectTransform, Vector2>
            _deckCanonicalPositions = new Dictionary<RectTransform, Vector2>();
        private readonly Dictionary<RectTransform, Vector3>
            _deckCanonicalScales = new Dictionary<RectTransform, Vector3>();
        private bool _deckMinimized;
        private Canvas _gestureToastCanvas;
        private CanvasGroup _gestureToastGroup;
        private RectTransform _gestureToastRect;
        private Image _gestureToastPanel;
        private TextMeshProUGUI _gestureToastLabel;
        private float _gestureToastShownAt = -1f;
        private float _gestureToastHideAt = -1f;
        private Canvas _settingsDeck;
        private RectTransform _settingsDeckRect;
        private TextMeshProUGUI _settingsGestureLabel;
        private TextMeshProUGUI _settingsRayLabel;
        private TextMeshProUGUI _settingsWindowModeLabel;
        private TextMeshProUGUI _settingsAudioLabel;
        private TextMeshProUGUI _settingsDeviceLabel;
        private TextMeshProUGUI _settingsTrackingLabel;
        private TextMeshProUGUI _settingsLensLabel;
        private TextMeshProUGUI _settingsTemperatureLabel;
        private Image _settingsDevicePill;
        private Image _settingsAudioPill;
        private Image _settingsTrackingPill;
        private Image _settingsLensPill;
        private Image _settingsBatteryRing;
        private Image _settingsTemperatureRing;
        private Image _settingsTrackingRing;
        private Image _settingsAudioRing;
        private Image _settingsVolumeControlRing;
        private Image _settingsBrightnessRing;
        private Image _settingsEcRing;
        private bool _lensControlValidated;
        private string _lensControlState = "ERR|not_probed";
        private Image _settingsMoveHandle;
        private Image _settingsResizeHandle;
        private Image _settingsResizeHandleRight;
        private Image _settingsDepthHandle;
        private Image _settingsTiltHandle;
        private Image _settingsFreeResizeHandle;
        private TextMeshProUGUI _settingsCloseHandle;
        private Button _settingsBlockButton;
        private TextMeshProUGUI _settingsTitleLabel;
        private TextMeshProUGUI _settingsFollowLabel;
        private Image _settingsWindowRim;
        private Image _settingsWindowSurface;
        private Image _settingsHeaderSurface;
        private Image _settingsControlsSurface;
        private Image _settingsSectionDivider;
        private Button _settingsPortraitButton;
        private Button _settingsLandscapeButton;
        private RectTransform _settingsBrightnessControl;
        private RectTransform _settingsEcControl;
        private RectTransform _settingsVolumeControl;
        private Button _settingsWindowModeButton;
        private Button _settingsGesturesButton;
        private Button _settingsRayButton;
        private Button _settingsVolumeDownButton;
        private Button _settingsVolumeUpButton;
        private Button _settingsRecenterButton;
        private Button _settingsCloseAllButton;
        private readonly Button[] _settingsLensButtons = new Button[4];
        private readonly List<Graphic> _settingsHitGraphics =
            new List<Graphic>();
        private Canvas _windowDock;
        private CanvasGroup _windowDockGroup;
        private RectTransform _windowDockRect;
        private readonly List<Graphic> _windowDockHitGraphics =
            new List<Graphic>();
        private float _windowDockShownAt = -1f;
        private IWorldCreatorInteractionSettings _interactionSettings;
        private DeckManipulationMode _deckHoverMode;
        private DeckManipulationMode _deckManipulationMode;
        private Vector2 _deckManipulationStartHand;
        private Vector3 _deckManipulationStartPosition;
        private Vector3 _deckManipulationStartCameraPosition;
        private Quaternion _deckManipulationStartCameraRotation;
        private Quaternion _deckManipulationStartRotation;
        private Vector3 _deckManipulationStartDirection;
        private float _deckManipulationStartDistance;
        private float _deckManipulationStartScale;
        private float _deckManipulationStartZoom;
        private float _deckManipulationStartTilt;
        private float _deckManipulationStartTurn;
        private Vector3 _deckManipulationTargetPosition;
        private Quaternion _deckManipulationTargetRotation;
        private float _deckManipulationTargetScale;
        private float _deckManipulationTargetTilt;
        private float _deckManipulationTargetTurn;
        private TiltGestureAxis _tiltGestureAxis;
        private Vector2 _deckManipulationStartSize;
        private Vector2 _deckManipulationTargetSize;
        private bool _deckManipulationSmoothing;
        private bool _deckManipulationUsesSize;
        private bool _deckManipulationUsesCrop;
        private bool _headFollowWindows;
        private bool _manualFrozenWindows;
        private float _nextSettingsTelemetryAt;
        private RectTransform _activeManipulationRect;
        private DeckWindowKind _hoverWindow;
        private DeckWindowKind _activeWindow;
        private DeckWindowKind _lastWindow = DeckWindowKind.Workspace;
        private DeckWindowKind _deckAffordanceRevealWindow;
        private float _deckAffordanceRevealUntil = -1f;
        private static Material _deckDepthMaterial;
        private static Material _deckPrimaryDepthMaterial;
        private static Sprite _visionCircleSprite;
        private static Sprite _visionRingSprite;
        private static Sprite _visionRoundedSprite;
        private static Sprite _visionCornerArcSprite;
        private static Sprite _visionSpeakerSprite;
        private static Sprite _visionTopRoundedSprite;
        private static readonly Color VisionGlass =
            new Color(.15f, .16f, .18f, .72f);
        private static readonly Color VisionGlassHover =
            new Color(.38f, .40f, .44f, .92f);
        private static readonly Color VisionPressed =
            new Color(.93f, .95f, .98f, .98f);
        private static readonly Color VisionInk =
            new Color(.055f, .06f, .075f, 1f);
        private static readonly Color VisionText =
            new Color(.96f, .97f, 1f, .98f);
        private static readonly Color VisionSecondary =
            new Color(.76f, .78f, .84f, .90f);
        private const string DeckLayoutPrefix =
            "mlomega.atelier.deck_layout.v1.";
        private const string SettingsLayoutPrefix =
            "mlomega.atelier.settings_layout.v1.";
        private const string WindowModePreference =
            "mlomega.atelier.window_mode.v1";
        private const string WindowDockDepthPreference =
            "mlomega.xr.lab.window_dock_depth.v1";
        private const string WorkspaceOrientationPreference =
            "mlomega.atelier.workspace_landscape.v1";
        private static readonly string[] DynamicKinds =
        {
            "object", "vehicle", "storefront", "sign", "building", "person",
        };
        private static readonly string[] Attachments =
        {
            "above", "center", "front", "rear", "left", "right", "below",
        };
        private static readonly string[] MotionPaths =
        {
            "static", "orbit", "patrol", "figure8", "vertical",
        };

        private enum DeckManipulationMode
        {
            None = 0,
            Move = 1,
            ResizeLeft = 2,
            ResizeRight = 3,
            Depth = 4,
            Minimize = 5,
            Tilt = 6,
            ResizeFree = 7,
            CropLeft = 8,
            CropRight = 9,
            CropBottom = 10,
            CropTop = 11,
        }

        private enum DeckWindowKind
        {
            None = 0,
            Workspace = 1,
            Settings = 2,
            External = 3,
        }

        private enum TiltGestureAxis
        {
            Undecided = 0,
            Vertical = 1,
            Horizontal = 2,
        }

        private enum VisionIconKind
        {
            Phone,
            Temperature,
            Tracking,
            Audio,
            Glasses,
            Depth,
            Window,
            Hand,
            Eye,
            VolumeMinus,
            VolumePlus,
            Recenter,
            Close,
            Brightness,
            BrightnessMinus,
            BrightnessPlus,
            ElectrochromicMinus,
            ElectrochromicPlus,
            Workspace,
            Settings,
            Portrait,
            Landscape,
            Ultrawide,
            Tilt,
            Power,
            Vr,
            Keyboard,
            Record,
            Lock,
        }

        public bool IsDeckManipulating =>
            _deckManipulationMode != DeckManipulationMode.None;

        public bool IsLabWorkspaceVisible => !_osOnlyMode && !_deckMinimized;

        public float WindowDockDepth => Mathf.Clamp(
            PlayerPrefs.GetFloat(WindowDockDepthPreference, 1.08f), .62f, 2.2f);

        public void SetWindowDockDepth(float depth)
        {
            depth = Mathf.Clamp(depth, .62f, 2.2f);
            PlayerPrefs.SetFloat(WindowDockDepthPreference, depth);
            PlayerPrefs.Save();
            if (_windowDockRect == null || _camera == null ||
                !_windowDockRect.gameObject.activeInHierarchy) return;
            Vector3 direction = _windowDockRect.position - _camera.transform.position;
            if (direction.sqrMagnitude < .001f)
                direction = _camera.transform.forward;
            _windowDockRect.position =
                _camera.transform.position + direction.normalized * depth;
        }

        public void RestoreLabWorkspaceForSession(bool visible)
        {
            // Session restore deliberately excludes Settings. Its controls are
            // transient system UI, not user content.
            if (_settingsDeck != null) _settingsDeck.gameObject.SetActive(false);
            if (_osOnlyMode)
            {
                _deckMinimized = true;
                if (visible) OpenWindowDockFromTwoPalms();
                return;
            }
            SetDeckMinimized(!visible);
        }

        // The pointer may sleep only when every Atelier surface is absent. A
        // closed workspace must not disable the independent settings or dock.
        public bool IsDeckClosed =>
            _deckMinimized &&
            (_settingsDeck == null || !_settingsDeck.gameObject.activeSelf) &&
            (_windowDock == null || !_windowDock.gameObject.activeSelf) &&
            (_quickMenu == null || !_quickMenu.gameObject.activeSelf) &&
            !HasVisibleExternalSpatialWindows();

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
            if (!_osOnlyMode && _exchange == null)
                _exchange =
                    GetComponent<WorldMapDocumentExchange>() ??
                    gameObject.AddComponent<WorldMapDocumentExchange>();
            ResolveInteractionSettings();
            _deckMinimized = _osOnlyMode;
            int windowMode = PlayerPrefs.GetInt(WindowModePreference, 0);
            _headFollowWindows = windowMode == 1;
            _manualFrozenWindows = windowMode == 2;
            _autoJoinWindowBlock = PlayerPrefs.GetInt(
                AutoJoinWindowBlockPreference, 0) == 1;
            if (_spatialBehaviour == null)
            {
                foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(
                    FindObjectsSortMode.None))
                {
                    if (behaviour is IWorldCreatorSpatialProvider)
                    {
                        _spatialBehaviour = behaviour;
                        break;
                    }
                }
            }
            if (!_osOnlyMode && Spatial != null)
            {
                Spatial.CreatorOperationCompleted += OnCreatorOperation;
                Spatial.EnableCreatorMode();
            }
            if (_osOnlyMode)
            {
                BuildOsShell();
                return;
            }
            _exchange.Exported += path =>
                _status = "MONDE EXPORTÉ // " + path;
            _exchange.ImageImported += OnImageImported;
            _exchange.GlbImported += OnGlbImported;
            _exchange.Failed += error =>
                _status = "ERREUR DOCUMENT // " + error;
            SelectCategory(_category);
            BuildSpatialDeck();
        }

        private void OnDestroy()
        {
            if (Spatial != null)
                Spatial.CreatorOperationCompleted -= OnCreatorOperation;
            if (_exchange != null)
            {
                _exchange.ImageImported -= OnImageImported;
                _exchange.GlbImported -= OnGlbImported;
            }
            if (_spatialDeck != null)
                Destroy(_spatialDeck.gameObject);
            if (_gestureToastCanvas != null)
                Destroy(_gestureToastCanvas.gameObject);
            if (_settingsDeck != null)
                Destroy(_settingsDeck.gameObject);
            if (_windowDock != null)
                Destroy(_windowDock.gameObject);
            if (_quickMenu != null)
                Destroy(_quickMenu.gameObject);
            if (_headOnlyPassiveTab != null)
                Destroy(_headOnlyPassiveTab.gameObject);
        }

        private void Update()
        {
            UpdateGestureToast();
            UpdateWindowDockAnimation();
            UpdateQuickMenu();
            SmoothDeckManipulation();
            UpdateSpatialTrackingFallback();
            UpdateWindowFollowMode();
            UpdateSettingsTelemetry();
            if (_osOnlyMode) return;
            if (
                Spatial == null ||
                Time.unscaledTime < _nextPreviewAt)
                return;
            _nextPreviewAt = Time.unscaledTime + 0.12f;
            _hasPreviewPose = Spatial.TryCreatorPlacement(
                new Vector2(0.5f, 0.5f),
                out _previewPosition,
                out _previewRotation);
            if (
                _hasPreviewPose &&
                _camera != null &&
                Vector3.Distance(
                    _camera.transform.position,
                    _previewPosition) < 0.55f)
            {
                // XREAL depth meshes can briefly expose a triangle at the XR
                // origin while they settle. Rendering the selected preset on
                // that hit makes its LineRenderers cross the stereo near plane
                // and cover both eyes. Never preview an unsafe placement.
                _hasPreviewPose = false;
            }
            if (_hasPreviewPose)
            {
                EnsurePreview();
                RefreshPreview();
                _status = Spatial.CreatorReady
                    ? "ANCRAGE PRÊT // VISE UNE SURFACE"
                    : "MESH TROUVÉ // ANCRE EN ATTENTE";
            }
            else if (_preview != null)
            {
                _preview.gameObject.SetActive(false);
            }
            RefreshSpatialDeck();
        }

        private void OnGUI()
        {
#if !UNITY_EDITOR
            return;
#endif
            EnsureStyles();
            float scale = Mathf.Clamp(Screen.dpi / 220f, 0.85f, 1.45f);
            float width = Mathf.Min(560f * scale, Screen.width * 0.48f);
            GUILayout.BeginArea(
                new Rect(18f, 18f, width, Screen.height - 36f),
                _panel);
            GUILayout.Label("MLOMEGA // WORLD ATELIER", _title);
            GUILayout.Label(
                "FREEGUY × BLADE RUNNER  •  " +
                WorldCreatorCatalog.Entries.Count +
                " PRESETS PROCÉDURAUX",
                _labelStyle);

            GUILayout.BeginHorizontal();
            foreach (string category in Categories)
            {
                if (GUILayout.Button(
                        category.ToUpperInvariant(),
                        _category == category ? _selectedButton : _button,
                        GUILayout.Height(38f)))
                    SelectCategory(category);
            }
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(
                _scroll,
                GUILayout.Height(Mathf.Min(310f, Screen.height * 0.34f)));
            int start = _page * 12;
            int end = Mathf.Min(start + 12, _visible.Count);
            for (int i = start; i < end; i += 2)
            {
                GUILayout.BeginHorizontal();
                DrawPresetButton(_visible[i]);
                if (i + 1 < end) DrawPresetButton(_visible[i + 1]);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", _button)) _page = Mathf.Max(0, _page - 1);
            GUILayout.Label(
                $"{_page + 1}/{Mathf.Max(1, Mathf.CeilToInt(_visible.Count / 12f))}",
                _labelStyle,
                GUILayout.Width(72f));
            if (GUILayout.Button("▶", _button))
                _page = Mathf.Min(
                    Mathf.Max(0, Mathf.CeilToInt(_visible.Count / 12f) - 1),
                    _page + 1);
            GUILayout.EndHorizontal();

            GUILayout.Label("TEXTE LIBRE", _labelStyle);
            _label = GUILayout.TextField(_label, 120, _field);
            _subtitle = GUILayout.TextField(_subtitle, 240, _field);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("TAILLE −", _button))
                _uniformScale = Mathf.Max(.1f, _uniformScale / 1.25f);
            GUILayout.Label(_uniformScale.ToString("0.0×"), _labelStyle);
            if (GUILayout.Button("TAILLE +", _button))
                _uniformScale = Mathf.Min(
                    WorldMapStore.MaxWorldScale,
                    _uniformScale * 1.25f);
            if (GUILayout.Button("↺ 15°", _button)) _yaw -= 15f;
            if (GUILayout.Button("↻ 15°", _button)) _yaw += 15f;
            GUILayout.EndHorizontal();

            GUI.enabled =
                Spatial != null &&
                Spatial.CreatorReady &&
                _selected != null &&
                _hasPreviewPose;
            if (GUILayout.Button(
                    "ANCRER DANS LE MONDE",
                    _selectedButton,
                    GUILayout.Height(58f)))
            {
                Vector3 scale3 =
                    _selected.defaultScale * _uniformScale;
                if (Spatial.PersistCreatorContent(
                        new Vector2(.5f, .5f),
                        _selected,
                        _label,
                        _subtitle,
                        scale3,
                        _yaw,
                        _pendingAssetId,
                        MotionPaths[_motionIndex],
                        MotionRadius(),
                        .8f,
                        MotionHeight()))
                    _status = "SAUVEGARDE ANCRE NATIVE…";
            }
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    string.IsNullOrEmpty(_pendingAssetId)
                        ? "IMPORTER LOGO PNG/JPEG"
                        : "LOGO PRÊT ✓",
                    string.IsNullOrEmpty(_pendingAssetId)
                        ? _button
                        : _selectedButton))
                _exchange.BeginImageImport();
            if (!string.IsNullOrEmpty(_pendingAssetId) &&
                GUILayout.Button("RETIRER LOGO", _button))
                _pendingAssetId = string.Empty;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(_lastCreatedId);
            if (GUILayout.Button("ANNULER DERNIER", _button))
                Spatial?.RemoveCreatorContent(_lastCreatedId);
            GUI.enabled = Spatial?.CreatorMap != null &&
                Spatial.CreatorMap.Contents.Count > 0;
            if (GUILayout.Button("EXPORTER LE MONDE", _button))
            {
                if (Spatial.PrepareCreatorExport(out string exportError))
                    _exchange.BeginExport(
                        Spatial.CreatorMap,
                        "mlomega-" + Spatial.CreatorMap.WorldMapId);
                else
                    _status = "EXPORT REFUSÉ // " + exportError;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.Label(_status, _labelStyle);
            GUILayout.Label(
                "AUCUNE CAMÉRA, VOIX OU MÉMOIRE N'EST ARCHIVÉE",
                _labelStyle);
            GUILayout.EndArea();

            // Precision reticle in the glasses view.
            Color old = GUI.color;
            GUI.color = _hasPreviewPose
                ? new Color(.15f, 1f, .88f, .95f)
                : new Color(1f, .28f, .45f, .85f);
            GUI.Label(
                new Rect(
                    Screen.width * .5f - 16f,
                    Screen.height * .5f - 16f,
                    32f,
                    32f),
                "◎",
                _title);
            GUI.color = old;
        }

        private void SelectCategory(string category)
        {
            _category = category;
            _visible = WorldCreatorCatalog.ForCategory(category);
            _page = 0;
            if (_visible.Count > 0) SelectPreset(_visible[0]);
            RefreshSpatialDeck();
        }

        private void DrawPresetButton(WorldCreatorCatalog.Entry entry)
        {
            bool selected =
                _selected != null &&
                _selected.presetId == entry.presetId;
            if (GUILayout.Button(
                    entry.label + "\n" + entry.archetypeId.Replace("-", " "),
                    selected ? _selectedButton : _button,
                    GUILayout.Height(54f)))
                SelectPreset(entry);
        }

        private void SelectPreset(WorldCreatorCatalog.Entry entry)
        {
            _selected = entry;
            _label = entry.label;
            _subtitle = entry.subtitle;
            _uniformScale = 1f;
            if (_hasPreviewPose) RefreshPreview();
            RefreshSpatialDeck();
        }

        private void BuildSpatialDeck()
        {
            if (_spatialDeck != null || _camera == null) return;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var eventGo = new GameObject("Atelier Spatial EventSystem");
                eventGo.AddComponent<EventSystem>();
                var input = eventGo.AddComponent<InputSystemUIInputModule>();
                input.AssignDefaultActions();
            }

            var deckGo = new GameObject("Atelier Holographic Control Deck");
            _spatialDeck = deckGo.AddComponent<Canvas>();
            _spatialDeck.renderMode = RenderMode.WorldSpace;
            _spatialDeck.worldCamera = _camera;
            _spatialDeck.sortingOrder = 80;
            deckGo.AddComponent<GraphicRaycaster>();
            _spatialDeckRect = deckGo.GetComponent<RectTransform>();
            _spatialDeckRect.sizeDelta = new Vector2(920f, 1220f);
            // Optical see-through glasses must never receive a screen-sized
            // opaque slab.  Keep the editor within a comfortable ~28 degree
            // field of view and let the real world remain visible around and
            // through the controls.
            _spatialDeckRect.localScale = Vector3.one * .00062f;
            SetDeckPose();

            // One bounded optical-glass window, matching the proven Settings
            // visual language. It never covers the full eye buffer: the real
            // world remains visible around it and through its low-alpha body.
            _deckWindowRim = MakeImage(
                _spatialDeckRect,
                "Pupitre Atelier fine rim",
                Vector2.zero,
                new Vector2(884f, 1184f),
                new Color(.88f, .91f, .98f, .10f));
            _deckWindowRim.raycastTarget = false;
            _deckWindowSurface = MakeImage(
                _spatialDeckRect,
                "Pupitre Atelier optical glass",
                Vector2.zero,
                new Vector2(880f, 1180f),
                new Color(.105f, .112f, .132f, .48f));
            _deckWindowSurface.raycastTarget = false;
            _deckHeaderSurface = MakeImage(
                _spatialDeckRect,
                "Pupitre Atelier header material",
                new Vector2(0f, 505f),
                new Vector2(880f, 170f),
                new Color(.045f, .052f, .068f, .72f));
            _deckHeaderSurface.sprite = GetVisionTopRoundedSprite();
            _deckHeaderSurface.type = Image.Type.Sliced;
            _deckHeaderSurface.raycastTarget = false;
            MakeText(
                _spatialDeckRect,
                "Pupitre Atelier",
                new Vector2(0f, 475f),
                new Vector2(850f, 60f),
                34f,
                VisionText,
                FontStyles.Bold);
            MakeText(
                _spatialDeckRect,
                "VOLUMES 3D • ANCRES XREAL • AUCUNE CAPTURE MÉMOIRE",
                new Vector2(0f, 433f),
                new Vector2(850f, 34f),
                17f,
                VisionSecondary);

            for (int i = 0; i < Categories.Length; i++)
            {
                int categoryIndex = i;
                float x = -330f + (i % 4) * 220f;
                float y = 378f - (i / 4) * 52f;
                Button button = MakeButton(
                    _spatialDeckRect,
                    Categories[i].ToUpperInvariant(),
                    new Vector2(x, y),
                    new Vector2(205f, 42f),
                    () => SelectCategory(Categories[categoryIndex]));
                _deckCategoryButtons.Add(button);
            }

            for (int i = 0; i < 12; i++)
            {
                int presetIndex = i;
                float x = -290f + (i % 3) * 290f;
                float y = 252f - (i / 3) * 72f;
                Button button = MakeButton(
                    _spatialDeckRect,
                    "PRESET",
                    new Vector2(x, y),
                    new Vector2(270f, 60f),
                    () => SelectVisiblePreset(presetIndex));
                _deckPresetButtons.Add(button);
                _deckPresetLabels.Add(
                    button.GetComponentInChildren<TextMeshProUGUI>());
            }

            MakeButton(
                _spatialDeckRect,
                "◀",
                new Vector2(-170f, -18f),
                new Vector2(90f, 42f),
                () =>
                {
                    _page = Mathf.Max(0, _page - 1);
                    RefreshSpatialDeck();
                });
            _deckPage = MakeText(
                _spatialDeckRect,
                "1/1",
                new Vector2(0f, -18f),
                new Vector2(200f, 42f),
                18f,
                VisionSecondary);
            MakeButton(
                _spatialDeckRect,
                "▶",
                new Vector2(170f, -18f),
                new Vector2(90f, 42f),
                () =>
                {
                    _page = Mathf.Min(PageCount - 1, _page + 1);
                    RefreshSpatialDeck();
                });

            _deckLabel = MakeInput(
                _spatialDeckRect,
                "Titre holographique",
                new Vector2(0f, -78f),
                new Vector2(850f, 48f),
                value => _label = value);
            _deckSubtitle = MakeInput(
                _spatialDeckRect,
                "Sous-titre / annotation libre",
                new Vector2(0f, -135f),
                new Vector2(850f, 48f),
                value => _subtitle = value);

            MakeButton(
                _spatialDeckRect,
                "TAILLE −",
                new Vector2(-380f, -198f),
                new Vector2(110f, 46f),
                () =>
                {
                    _uniformScale = Mathf.Max(.1f, _uniformScale / 1.25f);
                    RefreshPreview();
                });
            _deckScale = MakeText(
                _spatialDeckRect,
                "1.0×",
                new Vector2(-260f, -198f),
                new Vector2(100f, 46f),
                18f,
                VisionText,
                FontStyles.Bold);
            MakeButton(
                _spatialDeckRect,
                "TAILLE +",
                new Vector2(-145f, -198f),
                new Vector2(110f, 46f),
                () =>
                {
                    _uniformScale = Mathf.Min(
                        WorldMapStore.MaxWorldScale,
                        _uniformScale * 1.25f);
                    RefreshPreview();
                });
            MakeButton(
                _spatialDeckRect,
                "ROTATION ↻",
                new Vector2(-10f, -198f),
                new Vector2(130f, 46f),
                () =>
                {
                    _yaw += 15f;
                    RefreshPreview();
                });
            Button motion = MakeButton(
                _spatialDeckRect,
                "MOUV: STATIC",
                new Vector2(245f, -198f),
                new Vector2(340f, 46f),
                () =>
                {
                    _motionIndex = (_motionIndex + 1) % MotionPaths.Length;
                    RefreshSpatialDeck();
                    RefreshPreview();
                });
            _deckMotionLabel =
                motion.GetComponentInChildren<TextMeshProUGUI>();

            MakeButton(
                _spatialDeckRect,
                "IMPORTER LOGO",
                new Vector2(-285f, -258f),
                new Vector2(250f, 48f),
                () => _exchange.BeginImageImport());
            _deckAsset = MakeText(
                _spatialDeckRect,
                "AUCUN LOGO",
                new Vector2(0f, -258f),
                new Vector2(230f, 48f),
                15f,
                VisionSecondary);
            MakeButton(
                _spatialDeckRect,
                "RETIRER",
                new Vector2(285f, -258f),
                new Vector2(250f, 48f),
                () =>
                {
                    _pendingAssetId = string.Empty;
                    RefreshPreview();
                });

            Button commit = MakeButton(
                _spatialDeckRect,
                "ANCRER DANS LE MONDE",
                new Vector2(0f, -332f),
                new Vector2(850f, 66f),
                AnchorFromSpatialDeck,
                true);
            _deckCommitLabel = commit.GetComponentInChildren<TextMeshProUGUI>();
            MakeButton(
                _spatialDeckRect,
                "ANNULER DERNIER",
                new Vector2(-285f, -408f),
                new Vector2(250f, 48f),
                () =>
                {
                    if (!string.IsNullOrEmpty(_lastCreatedId))
                        Spatial?.RemoveCreatorContent(_lastCreatedId);
                });
            MakeButton(
                _spatialDeckRect,
                "EXPORTER MONDE",
                new Vector2(0f, -408f),
                new Vector2(250f, 48f),
                ExportFromSpatialDeck);
            MakeButton(
                _spatialDeckRect,
                "IMPORTER GLB",
                new Vector2(-350f, -474f),
                new Vector2(170f, 44f),
                () => _exchange.BeginGlbImport());
            Button mode = MakeButton(
                _spatialDeckRect,
                "MODE ANCRÉ",
                new Vector2(-165f, -474f),
                new Vector2(170f, 44f),
                () =>
                {
                    _dynamicMode = !_dynamicMode;
                    RefreshSpatialDeck();
                });
            _deckModeLabel = mode.GetComponentInChildren<TextMeshProUGUI>();
            _deckTarget = MakeInput(
                _spatialDeckRect,
                "Cible précise (optionnel)",
                new Vector2(45f, -474f),
                new Vector2(225f, 44f),
                value => _dynamicTargetLabel = value);
            Button kind = MakeButton(
                _spatialDeckRect,
                "CIBLE: OBJECT",
                new Vector2(260f, -474f),
                new Vector2(180f, 44f),
                () =>
                {
                    _dynamicKindIndex =
                        (_dynamicKindIndex + 1) % DynamicKinds.Length;
                    RefreshSpatialDeck();
                });
            _deckKindLabel = kind.GetComponentInChildren<TextMeshProUGUI>();
            Button attachment = MakeButton(
                _spatialDeckRect,
                "POS: ABOVE",
                new Vector2(385f, -474f),
                new Vector2(110f, 44f),
                () =>
                {
                    _attachmentIndex =
                        (_attachmentIndex + 1) % Attachments.Length;
                    RefreshSpatialDeck();
                });
            _deckAttachmentLabel =
                attachment.GetComponentInChildren<TextMeshProUGUI>();

            MakeButton(
                _spatialDeckRect,
                "◀",
                new Vector2(-405f, -528f),
                new Vector2(65f, 44f),
                () => MoveManaged(-1));
            _deckManagedLabel = MakeText(
                _spatialDeckRect,
                "AUCUN ÉLÉMENT",
                new Vector2(-270f, -528f),
                new Vector2(190f, 44f),
                13f,
                VisionSecondary);
            MakeButton(
                _spatialDeckRect,
                "▶",
                new Vector2(-145f, -528f),
                new Vector2(65f, 44f),
                () => MoveManaged(1));
            MakeButton(
                _spatialDeckRect,
                "SUPPRIMER",
                new Vector2(-45f, -528f),
                new Vector2(125f, 44f),
                DeleteManaged);
            MakeButton(
                _spatialDeckRect,
                "NOUVELLE MAP",
                new Vector2(115f, -528f),
                new Vector2(175f, 44f),
                CreateMap);
            Button map = MakeButton(
                _spatialDeckRect,
                "MAP ▶",
                new Vector2(310f, -528f),
                new Vector2(200f, 44f),
                NextMap);
            _deckMapLabel = map.GetComponentInChildren<TextMeshProUGUI>();
            _deckStatus = MakeText(
                _spatialDeckRect,
                _status,
                new Vector2(0f, -586f),
                new Vector2(850f, 62f),
                17f,
                VisionText,
                FontStyles.Bold);

            // Preserve the authored portrait coordinates once. Landscape uses
            // an affine reflow of these roots: positions spread horizontally,
            // vertical rhythm compresses and controls scale uniformly, without
            // changing any button callback or hit target hierarchy.
            CaptureWorkspaceCanonicalLayout();

            // Vision-Pro-style affordances: invisible until the existing gaze
            // ray reaches their zone. They are ordinary UGUI quads (never a
            // LineRenderer, which is unsafe under XREAL single-pass stereo).
            _deckMoveHandle = MakeImage(
                _spatialDeckRect,
                "Gaze move handle",
                new Vector2(-48f, -603f),
                new Vector2(104f, 7f),
                new Color(.76f, .78f, .82f, .78f));
            _deckMoveHandle.raycastTarget = false;
            AddVisionHandleDot(_deckMoveHandle, false);
            _deckMoveHandle.gameObject.SetActive(false);
            _deckResizeHandle = MakeImage(
                _spatialDeckRect,
                "Gaze resize handle",
                new Vector2(-437f, -577f),
                Vector2.one * 48f,
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionResizeHandle(_deckResizeHandle, false);
            _deckResizeHandle.raycastTarget = false;
            _deckResizeHandle.gameObject.SetActive(false);
            _deckResizeHandleRight = MakeImage(
                _spatialDeckRect,
                "Gaze resize handle right",
                new Vector2(437f, -577f),
                Vector2.one * 48f,
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionResizeHandle(_deckResizeHandleRight, true);
            _deckResizeHandleRight.raycastTarget = false;
            _deckResizeHandleRight.gameObject.SetActive(false);
            _deckDepthHandle = MakeImage(
                _spatialDeckRect,
                "Gaze depth handle",
                new Vector2(82f, -603f),
                Vector2.one * 34f,
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionDepthHandle(_deckDepthHandle);
            _deckDepthHandle.raycastTarget = false;
            _deckDepthHandle.gameObject.SetActive(false);
            _deckTiltHandle = MakeImage(
                _spatialDeckRect,
                "Gaze tilt handle",
                new Vector2(130f, -603f),
                Vector2.one * 34f,
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionTiltHandle(_deckTiltHandle);
            _deckTiltHandle.raycastTarget = false;
            _deckTiltHandle.gameObject.SetActive(false);

            // The top-right close affordance is revealed only by gaze. It is a
            // real cross, never the small residual rectangle of the old minimize.
            _deckCloseHandle = MakeText(
                _spatialDeckRect,
                "×",
                new Vector2(438f, 592f),
                new Vector2(38f, 38f),
                24f,
                new Color(.82f, .84f, .88f, .90f));
            _deckCloseHandle.raycastTarget = false;
            _deckCloseHandle.gameObject.SetActive(false);

            _deckPortraitButton = MakeOrientationButton(
                _spatialDeckRect,
                "Pupitre Portrait",
                VisionIconKind.Portrait,
                () => SetWorkspaceOrientation(false));
            _deckLandscapeButton = MakeOrientationButton(
                _spatialDeckRect,
                "Pupitre Landscape",
                VisionIconKind.Landscape,
                () => SetWorkspaceOrientation(true));
            _deckBlockButton = MakeOrientationButton(
                _spatialDeckRect,
                "Pupitre Block",
                VisionIconKind.Lock,
                () => ToggleWindowBlock(DeckWindowKind.Workspace, null));
            InitializeWindowBlockState(DeckWindowKind.Workspace, null);
            ApplyWorkspaceOrientation(
                PlayerPrefs.GetInt(WorkspaceOrientationPreference, 0) == 1,
                false);

            _deckExpandedRoots.Clear();
            for (int i = 0; i < _spatialDeckRect.childCount; i++)
                _deckExpandedRoots.Add(
                    _spatialDeckRect.GetChild(i).gameObject);

            _deckHitGraphics.Clear();
            _spatialDeckRect.GetComponentsInChildren(
                true,
                _deckHitGraphics);
            BuildGestureToast();
            BuildSettingsDeck();
            BuildWindowDock();
            RefreshSpatialDeck();
            // Startup is the lightweight app launcher, not an already-open
            // editing window. The user explicitly chooses Pupitre or Réglages.
            SetDeckMinimized(true);
            OpenWindowDockFromTwoPalms();
        }

        private void BuildOsShell()
        {
            if (_camera == null) return;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var eventGo = new GameObject("XReel OS EventSystem");
                eventGo.AddComponent<EventSystem>();
                var input = eventGo.AddComponent<InputSystemUIInputModule>();
                input.AssignDefaultActions();
            }
            BuildGestureToast();
            BuildSettingsDeck();
            BuildWindowDock();
            OpenWindowDockFromTwoPalms();
        }

        private void CaptureWorkspaceCanonicalLayout()
        {
            _deckCanonicalPositions.Clear();
            _deckCanonicalScales.Clear();
            if (_spatialDeckRect == null) return;
            for (int i = 0; i < _spatialDeckRect.childCount; i++)
            {
                RectTransform rect =
                    _spatialDeckRect.GetChild(i) as RectTransform;
                if (
                    rect == null ||
                    rect == _deckWindowRim.rectTransform ||
                    rect == _deckWindowSurface.rectTransform ||
                    rect == _deckHeaderSurface.rectTransform)
                    continue;
                _deckCanonicalPositions[rect] = rect.anchoredPosition;
                _deckCanonicalScales[rect] = rect.localScale;
            }
        }

        private void SetWorkspaceOrientation(bool landscape)
        {
            ApplyWorkspaceOrientation(landscape, true);
        }

        private void ApplyWorkspaceOrientation(bool landscape, bool notify)
        {
            if (_spatialDeckRect == null) return;
            _spatialDeckRect.sizeDelta = landscape
                ? new Vector2(1220f, 920f)
                : new Vector2(920f, 1220f);
            PlayerPrefs.SetInt(
                WorkspaceOrientationPreference,
                landscape ? 1 : 0);
            PlayerPrefs.Save();

            foreach (KeyValuePair<RectTransform, Vector2> entry in
                     _deckCanonicalPositions)
            {
                RectTransform rect = entry.Key;
                if (rect == null) continue;
                Vector2 canonical = entry.Value;
                rect.anchoredPosition = landscape
                    ? new Vector2(canonical.x * 1.24f,
                        canonical.y * .69f + 45f)
                    : canonical;
                rect.localScale = _deckCanonicalScales[rect] *
                    (landscape ? .78f : 1f);
            }

            float halfWidth = _spatialDeckRect.sizeDelta.x * .5f;
            float halfHeight = _spatialDeckRect.sizeDelta.y * .5f;
            float surfaceLeft = -halfWidth + 20f;
            float surfaceRight = halfWidth - 20f;
            float surfaceTop = halfHeight - 20f;
            float surfaceBottom = -halfHeight + 20f;
            float surfaceWidth = surfaceRight - surfaceLeft;
            float surfaceHeight = surfaceTop - surfaceBottom;
            LayoutSurface(_deckWindowRim, Vector2.zero,
                new Vector2(surfaceWidth + 4f, surfaceHeight + 4f));
            LayoutSurface(_deckWindowSurface, Vector2.zero,
                new Vector2(surfaceWidth, surfaceHeight));
            float headerHeight = landscape ? 110f : 170f;
            LayoutSurface(
                _deckHeaderSurface,
                new Vector2(0f, surfaceTop - headerHeight * .5f),
                new Vector2(surfaceWidth, headerHeight));

            LayoutButton(
                _deckPortraitButton,
                new Vector2(surfaceLeft + 35f, surfaceTop - 27f),
                new Vector2(52f, 38f));
            LayoutButton(
                _deckLandscapeButton,
                new Vector2(surfaceLeft + 95f, surfaceTop - 27f),
                new Vector2(52f, 38f));
            SetControlCenterState(
                _deckPortraitButton,
                !landscape,
                VisionPressed);
            SetControlCenterState(
                _deckLandscapeButton,
                landscape,
                VisionPressed);

            float bottom = -halfHeight + 17f;
            LayoutHandle(_deckMoveHandle, new Vector2(-70f, bottom),
                new Vector2(104f, 5f));
            LayoutHandle(_deckDepthHandle, new Vector2(72f, bottom),
                new Vector2(52f, 34f));
            LayoutHandle(_deckTiltHandle, new Vector2(136f, bottom),
                new Vector2(52f, 34f));
            LayoutHandle(_deckResizeHandle,
                new Vector2(-halfWidth + 20f, -halfHeight + 22f),
                Vector2.one * 52f);
            LayoutHandle(_deckResizeHandleRight,
                new Vector2(halfWidth - 20f, -halfHeight + 22f),
                Vector2.one * 52f);
            LayoutRect(_deckCloseHandle,
                new Vector2(surfaceRight + 14f, surfaceTop + 14f),
                new Vector2(34f, 34f));
            LayoutButton(_deckBlockButton,
                new Vector2(surfaceRight - 36f, surfaceTop + 14f),
                new Vector2(48f, 34f));

            if (notify)
                ShowGestureToast(
                    landscape ? "PUPITRE // PAYSAGE" : "PUPITRE // PORTRAIT",
                    new Color(.55f, .78f, 1f));
        }

        private void AnchorFromSpatialDeck()
        {
            if (_dynamicMode)
            {
                if (Spatial == null || _selected == null)
                {
                    _status = "RÈGLE DYNAMIQUE INDISPONIBLE";
                    return;
                }
                Vector3 dynamicScale = _selected.defaultScale * _uniformScale;
                if (Spatial.SaveCreatorDynamicBinding(
                        _selected,
                        _dynamicTargetLabel,
                        DynamicKinds[_dynamicKindIndex],
                        Attachments[_attachmentIndex],
                        _label,
                        _subtitle,
                        dynamicScale,
                        _pendingAssetId))
                    _status = "RÈGLE DYNAMIQUE SAUVEGARDÉE";
                return;
            }
            if (
                Spatial == null ||
                !Spatial.CreatorReady ||
                _selected == null ||
                !_hasPreviewPose)
            {
                Spatial?.BeginCreatorSpatialMapping();
                _status = "ANCRAGE INDISPONIBLE // VISE UNE SURFACE MAPPÉE";
                return;
            }
            Vector3 scale = _selected.defaultScale * _uniformScale;
            if (Spatial.PersistCreatorContent(
                    new Vector2(.5f, .5f),
                    _selected,
                    _label,
                    _subtitle,
                    scale,
                    _yaw,
                    _pendingAssetId,
                    MotionPaths[_motionIndex],
                    MotionRadius(),
                    .8f,
                    MotionHeight()))
                _status = "SAUVEGARDE DE L'ANCRE NATIVE…";
        }

        private int ManagedCount =>
            (Spatial?.CreatorMap?.Contents.Count ?? 0) +
            (Spatial?.CreatorMap?.DynamicBindings.Count ?? 0);

        private void MoveManaged(int direction)
        {
            int count = ManagedCount;
            if (count <= 0)
            {
                _managedIndex = 0;
                return;
            }
            _managedIndex = (_managedIndex + direction + count) % count;
            RefreshSpatialDeck();
        }

        private void DeleteManaged()
        {
            WorldMapStore map = Spatial?.CreatorMap;
            if (map == null || ManagedCount == 0) return;
            _managedIndex = Mathf.Clamp(_managedIndex, 0, ManagedCount - 1);
            if (_managedIndex < map.Contents.Count)
                Spatial.RemoveCreatorContent(
                    map.Contents[_managedIndex].worldContentId);
            else
                Spatial.RemoveCreatorDynamicBinding(
                    map.DynamicBindings[
                        _managedIndex - map.Contents.Count].bindingId);
            _managedIndex = Mathf.Max(0, _managedIndex - 1);
        }

        private void CreateMap()
        {
            if (Spatial == null) return;
            string name = string.IsNullOrWhiteSpace(_label)
                ? "Nouveau monde"
                : _label;
            if (Spatial.CreateCreatorMap(name))
            {
                _lastCreatedId = string.Empty;
                _pendingAssetId = string.Empty;
                _managedIndex = 0;
                _status = "NOUVELLE MAP // " + name;
            }
        }

        private void NextMap()
        {
            IReadOnlyList<WorldMapSelection> maps = Spatial?.CreatorMaps;
            if (maps == null || maps.Count == 0) return;
            _mapIndex = (_mapIndex + 1) % maps.Count;
            if (Spatial.SwitchCreatorMap(maps[_mapIndex].mapId))
            {
                _lastCreatedId = string.Empty;
                _pendingAssetId = string.Empty;
                _managedIndex = 0;
                _status = "MAP ACTIVE // " + maps[_mapIndex].displayName;
            }
        }

        /// <summary>
        /// Intersect a native XR-hand ray with the actual world-space deck.
        /// The caller still drives EventSystem pointer events, so touch remains
        /// available as a parallel fallback without a second UI implementation.
        /// </summary>
        public bool TryProjectDeckPointer(
            Ray ray,
            out Vector2 screenPoint,
            out Vector3 worldPoint)
        {
            screenPoint = default;
            worldPoint = default;
            if (
                _camera == null ||
                ray.direction.sqrMagnitude < .5f)
                return false;
            bool hit = false;
            float bestDistance = float.MaxValue;
            if (!_deckMinimized)
                hit |= TryProjectWindow(
                    ray,
                    _spatialDeckRect,
                    ref bestDistance,
                    ref worldPoint);
            if (_settingsDeck != null && _settingsDeck.gameObject.activeSelf)
                hit |= TryProjectWindow(
                    ray,
                    _settingsDeckRect,
                    ref bestDistance,
                    ref worldPoint);
            if (_windowDock != null && _windowDock.gameObject.activeSelf)
                hit |= TryProjectWindow(
                    ray,
                    _windowDockRect,
                    ref bestDistance,
                    ref worldPoint);
            if (_quickMenu != null && _quickMenu.gameObject.activeSelf)
                hit |= TryProjectWindow(
                    ray,
                    _quickMenuRect,
                    ref bestDistance,
                    ref worldPoint,
                    30f,
                    80f,
                    20f);
            hit |= TryProjectExternalWindows(
                ray,
                ref bestDistance,
                ref worldPoint);
            if (!hit) return false;
            screenPoint =
                RectTransformUtility.WorldToScreenPoint(_camera, worldPoint);
            // WorldToScreenPoint uses the active XR eye target, whereas
            // Screen.width/height can still describe the S24 portrait display.
            // The RectTransform hit above already validates the deck bounds;
            // comparing these unrelated coordinate spaces rejected valid XR
            // hits while leaving the visual cursor alive.
            return true;
        }

        private static bool TryProjectWindow(
            Ray ray,
            RectTransform rect,
            ref float bestDistance,
            ref Vector3 bestPoint,
            float horizontalGutter = 0f,
            float bottomGutter = 0f,
            float topGutter = 0f)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return false;
            var plane = new Plane(rect.forward, rect.position);
            if (
                !plane.Raycast(ray, out float distance) ||
                distance < .03f ||
                distance > 4f ||
                distance >= bestDistance)
                return false;
            Vector3 point = ray.GetPoint(distance);
            Vector3 local = rect.InverseTransformPoint(point);
            Rect bounds = rect.rect;
            bounds.xMin -= horizontalGutter;
            bounds.xMax += horizontalGutter;
            bounds.yMin -= bottomGutter;
            bounds.yMax += topGutter;
            if (!bounds.Contains(new Vector2(local.x, local.y))) return false;
            bestDistance = distance;
            bestPoint = point;
            return true;
        }

        /// <summary>
        /// Resolve an interactive UGUI target directly in the world-space
        /// deck's local coordinates. XREAL's eye render target and the S24
        /// display do not share a screen coordinate system, so GraphicRaycaster
        /// can legitimately return no hit even after the 3D ray has intersected
        /// the correct control. This fallback never guesses a target: the
        /// world point must be inside the actual raycastable Graphic rect and
        /// that graphic must have a real click handler in its parent chain.
        /// </summary>
        public bool TryResolveDeckTarget(
            Vector3 worldPoint,
            out GameObject target)
        {
            target = null;
            float smallestArea = float.MaxValue;
            ResolveTargetInGraphics(
                _deckHitGraphics,
                worldPoint,
                ref target,
                ref smallestArea);
            ResolveTargetInGraphics(
                _settingsHitGraphics,
                worldPoint,
                ref target,
                ref smallestArea);
            ResolveTargetInGraphics(
                _windowDockHitGraphics,
                worldPoint,
                ref target,
                ref smallestArea);
            ResolveTargetInGraphics(
                _quickMenuHitGraphics,
                worldPoint,
                ref target,
                ref smallestArea);
            ResolveExternalWindowTargets(
                worldPoint,
                ref target,
                ref smallestArea);
            return target != null;
        }

        private static void ResolveTargetInGraphics(
            List<Graphic> graphics,
            Vector3 worldPoint,
            ref GameObject target,
            ref float smallestArea)
        {
            for (int i = 0; i < graphics.Count; i++)
            {
                Graphic graphic = graphics[i];
                if (
                    graphic == null ||
                    !graphic.isActiveAndEnabled ||
                    !graphic.raycastTarget)
                    continue;
                var rect = graphic.rectTransform;
                Vector3 local = rect.InverseTransformPoint(worldPoint);
                if (!rect.rect.Contains(new Vector2(local.x, local.y)))
                    continue;
                GameObject handler =
                    ExecuteEvents.GetEventHandler<IPointerClickHandler>(
                        graphic.gameObject);
                if (handler == null) continue;
                float area = Mathf.Abs(rect.rect.width * rect.rect.height);
                if (area >= smallestArea) continue;
                smallestArea = area;
                target = handler;
            }
        }

        /// <summary>
        /// Reveal only the manipulation affordance currently targeted by the
        /// already-working gaze ray. No hand coordinates are used for aiming.
        /// </summary>
        public void UpdateDeckManipulationHover(
            Vector3 worldPoint,
            bool deckHit)
        {
            if (_deckManipulationMode != DeckManipulationMode.None) return;
            _deckHoverMode = deckHit
                ? ClassifyDeckManipulationHandle(worldPoint, out _hoverWindow)
                : DeckManipulationMode.None;
            if (!deckHit)
            {
                _hoverWindow = DeckWindowKind.None;
                _hoverExternalWindow = null;
            }
            SetDeckHandleVisuals(_deckHoverMode, _hoverWindow);
        }

        public bool IsDeckManipulationHandle(Vector3 worldPoint)
        {
            return ClassifyDeckManipulationHandle(
                worldPoint,
                out _) != DeckManipulationMode.None;
        }

        /// <summary>
        /// Claim a physical hand pinch only when gaze was on a bottom window
        /// handle. Normal buttons remain clicks.
        /// </summary>
        public bool TryBeginDeckManipulation(
            Vector3 gazeWorldPoint,
            Vector2 handAnchor,
            float zoomFactor)
        {
            if (
                _camera == null ||
                handAnchor.x < 0f ||
                handAnchor.y < 0f)
                return false;
            DeckManipulationMode mode =
                ClassifyDeckManipulationHandle(
                    gazeWorldPoint,
                    out DeckWindowKind window);
            if (mode == DeckManipulationMode.None) return false;
            if (window == DeckWindowKind.External)
                _activeExternalWindow = _hoverExternalWindow;
            if (mode == DeckManipulationMode.Minimize)
            {
                CloseWindow(window);
                return true;
            }
            _activeWindow = window;
            _lastWindow = window;
            _activeManipulationRect = RectForWindow(window);
            if (_activeManipulationRect == null) return false;
            _deckManipulationMode = mode;
            _deckManipulationStartHand = handAnchor;
            _deckManipulationStartPosition = _activeManipulationRect.position;
            _deckManipulationStartCameraPosition =
                _camera.transform.position;
            _deckManipulationStartCameraRotation =
                _camera.transform.rotation;
            _deckManipulationStartRotation =
                _activeManipulationRect.rotation;
            _deckManipulationStartDistance = Mathf.Clamp(
                Vector3.Distance(
                    _camera.transform.position,
                    _deckManipulationStartPosition),
                .45f,
                2.8f);
            _deckManipulationStartDirection =
                (_deckManipulationStartPosition -
                 _deckManipulationStartCameraPosition)
                .normalized;
            _deckManipulationStartScale = _activeManipulationRect.localScale.x;
            _deckManipulationStartZoom = Mathf.Max(.1f, zoomFactor);
            string layoutPrefix = LayoutPrefixForWindow(window);
            _deckManipulationStartTilt = PlayerPrefs.GetFloat(
                layoutPrefix + "tilt",
                0f);
            _deckManipulationStartTurn = PlayerPrefs.GetFloat(
                layoutPrefix + "turn",
                0f);
            _deckManipulationTargetPosition = _deckManipulationStartPosition;
            _deckManipulationTargetRotation = _deckManipulationStartRotation;
            _deckManipulationTargetScale = _deckManipulationStartScale;
            _deckManipulationTargetTilt = _deckManipulationStartTilt;
            _deckManipulationTargetTurn = _deckManipulationStartTurn;
            _deckManipulationStartSize = _activeManipulationRect.sizeDelta;
            _deckManipulationTargetSize = _deckManipulationStartSize;
            _deckManipulationUsesSize =
                (window == DeckWindowKind.Settings &&
                 mode == DeckManipulationMode.ResizeFree) ||
                (window == DeckWindowKind.External &&
                 mode == DeckManipulationMode.ResizeFree);
            _deckManipulationUsesCrop =
                window == DeckWindowKind.External && IsExternalCropMode(mode);
            if (_deckManipulationUsesCrop)
                BeginExternalCropManipulation(_activeExternalWindow);
            _deckManipulationSmoothing = true;
            _tiltGestureAxis = TiltGestureAxis.Undecided;
            BeginWindowBlockManipulation(window, _activeExternalWindow, mode);
            SetDeckHandleVisuals(mode, window);
            if (mode == DeckManipulationMode.Depth)
                ShowGestureToast(
                    "PROFONDEUR // GAUCHE RAPPROCHE • DROITE ELOIGNE",
                    new Color(.72f, .36f, 1f));
            else if (mode == DeckManipulationMode.Tilt)
                ShowGestureToast(
                    "INCLINAISON // HAUT-BAS + GAUCHE-DROITE",
                    new Color(.55f, .78f, 1f));
            else if (mode == DeckManipulationMode.ResizeFree)
                ShowGestureToast(
                    "FORMAT LIBRE // GAUCHE-DROITE LARGEUR + HAUT-BAS HAUTEUR",
                    new Color(.55f, .78f, 1f));
            return true;
        }

        /// <summary>
        /// Hand motion manipulates the gaze-selected handle. X/Y move in the
        /// viewing plane; pinch aperture provides a bounded monocular depth
        /// adjustment. The dedicated depth handle instead maps horizontal hand
        /// travel to one stable distance axis. Settings resize responsively;
        /// the workspace keeps its established aspect ratio.
        /// </summary>
        public void UpdateDeckManipulation(
            Vector2 handAnchor,
            float zoomFactor)
        {
            if (
                _deckManipulationMode == DeckManipulationMode.None ||
                _activeManipulationRect == null ||
                _camera == null ||
                handAnchor.x < 0f ||
                handAnchor.y < 0f)
                return;
            Vector2 delta = handAnchor - _deckManipulationStartHand;
            delta.x = Mathf.Clamp(delta.x, -.75f, .75f);
            delta.y = Mathf.Clamp(delta.y, -.65f, .65f);
            if (_deckManipulationMode == DeckManipulationMode.Move)
            {
                // While held, transport the original world pose by the current
                // head rotation. This keeps the exact grab offset (no jump), lets
                // the user carry the deck through a full turn, and stops updating
                // the instant the pinch ends so the released deck stays anchored.
                // Head rotation contributes only while this handle is actively
                // held. This is the comfortable Vision-style carry validated on
                // hardware; EndDeckManipulation freezes the last pose exactly.
                Quaternion headDelta =
                    _camera.transform.rotation *
                    Quaternion.Inverse(_deckManipulationStartCameraRotation);
                Vector3 carriedDirection =
                    (headDelta * _deckManipulationStartDirection).normalized;
                float span = _deckManipulationStartDistance * 1.15f;
                Vector3 planar =
                    _camera.transform.right * (delta.x * span) +
                    _camera.transform.up * (-delta.y * span * .8f);
                float depth = Mathf.Clamp(
                    _deckManipulationStartDistance -
                    (zoomFactor - _deckManipulationStartZoom) * .16f,
                    .45f,
                    2.8f);
                _deckManipulationTargetPosition =
                    _camera.transform.position +
                    carriedDirection * depth +
                    planar;
                // Moving owns X/Y only. Keep the window upright and preserve
                // only the explicit tilt selected with its dedicated handle.
                _deckManipulationTargetRotation = BuildWindowRotation(
                    carriedDirection,
                    _deckManipulationStartTilt,
                    _deckManipulationStartTurn);
            }
            else if (_deckManipulationMode == DeckManipulationMode.Depth)
            {
                float depth = Mathf.Clamp(
                    _deckManipulationStartDistance + delta.x * 2.15f,
                    .45f,
                    2.8f);
                _deckManipulationTargetPosition =
                    _deckManipulationStartCameraPosition +
                    _deckManipulationStartDirection * depth;
                _deckManipulationTargetRotation =
                    _deckManipulationStartRotation;
            }
            else if (_deckManipulationMode == DeckManipulationMode.Tilt)
            {
                // Choose one dominant axis per pinch. Eye-camera hand anchors
                // always contain a little orthogonal drift; without this lock,
                // a lateral turn also pitches the panel. A fresh pinch can then
                // adjust the other axis independently.
                if (
                    _tiltGestureAxis == TiltGestureAxis.Undecided &&
                    Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) >= .018f)
                    _tiltGestureAxis = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                        ? TiltGestureAxis.Horizontal
                        : TiltGestureAxis.Vertical;
                if (_tiltGestureAxis == TiltGestureAxis.Horizontal)
                {
                    _deckManipulationTargetTilt = _deckManipulationStartTilt;
                    _deckManipulationTargetTurn = Mathf.Clamp(
                        _deckManipulationStartTurn + delta.x * 108f,
                        -48f,
                        48f);
                }
                else if (_tiltGestureAxis == TiltGestureAxis.Vertical)
                {
                    _deckManipulationTargetTilt = Mathf.Clamp(
                        _deckManipulationStartTilt - delta.y * 94f,
                        -38f,
                        38f);
                    _deckManipulationTargetTurn = _deckManipulationStartTurn;
                }
                Vector3 direction =
                    (_deckManipulationStartPosition -
                     _deckManipulationStartCameraPosition).normalized;
                _deckManipulationTargetRotation = BuildWindowRotation(
                    direction,
                    _deckManipulationTargetTilt,
                    _deckManipulationTargetTurn);
            }
            else if (_deckManipulationUsesCrop)
            {
                UpdateExternalCropManipulation(
                    _activeExternalWindow,
                    _deckManipulationMode,
                    delta);
            }
            else if (
                _deckManipulationMode == DeckManipulationMode.ResizeFree &&
                (_activeWindow == DeckWindowKind.External ||
                 _activeWindow == DeckWindowKind.Settings))
            {
                float width = Mathf.Clamp(
                    _deckManipulationStartSize.x * (1f + delta.x * 1.8f),
                    _activeWindow == DeckWindowKind.Settings ? 620f : 360f,
                    _activeWindow == DeckWindowKind.Settings ? 1120f : 2600f);
                float height = Mathf.Clamp(
                    _deckManipulationStartSize.y * (1f + delta.y * 1.8f),
                    _activeWindow == DeckWindowKind.Settings ? 760f : 260f,
                    _activeWindow == DeckWindowKind.Settings ? 1120f : 1200f);
                _deckManipulationTargetSize = new Vector2(width, height);
            }
            else if (IsResizeMode(_deckManipulationMode))
            {
                float outwardX =
                    _deckManipulationMode == DeckManipulationMode.ResizeLeft
                        ? -delta.x
                        : delta.x;
                // Both corner handles are the easy, aspect-preserving resize on
                // every Atelier window. H/W remains the single explicit control
                // for changing width and height independently.
                float gesture = outwardX + delta.y;
                float factor = Mathf.Exp(gesture * 1.55f);
                _deckManipulationTargetScale = Mathf.Clamp(
                    _deckManipulationStartScale * factor,
                    .00015f,
                    .01000f);
            }
            _deckManipulationSmoothing = true;
        }

        public void EndDeckManipulation()
        {
            DeckWindowKind completedWindow = _activeWindow;
            if (
                _deckManipulationMode != DeckManipulationMode.None &&
                _activeManipulationRect != null)
            {
                SaveWindowLayout(
                    _activeWindow,
                    _deckManipulationTargetPosition,
                    _deckManipulationTargetScale);
                string prefix = LayoutPrefixForWindow(_activeWindow);
                PlayerPrefs.SetFloat(prefix + "tilt", _deckManipulationTargetTilt);
                PlayerPrefs.SetFloat(prefix + "turn", _deckManipulationTargetTurn);
                if (_activeWindow == DeckWindowKind.Settings)
                    SaveSettingsSize(_deckManipulationTargetSize);
                else if (
                    _activeWindow == DeckWindowKind.External &&
                    _deckManipulationUsesSize)
                {
                    ApplyExternalWindowSize(_deckManipulationTargetSize, true);
                    SaveExternalWindowSize(_activeExternalWindow);
                    SaveExternalCrop(_activeExternalWindow);
                }
                else if (
                    _activeWindow == DeckWindowKind.External &&
                    _deckManipulationUsesCrop)
                    CompleteExternalCropManipulation(_activeExternalWindow);

                // Never let interpolation continue after release: that was the
                // visible backwards jump. The saved pose and displayed pose are
                // now byte-for-byte the same release target.
                _activeManipulationRect.SetPositionAndRotation(
                    _deckManipulationTargetPosition,
                    _deckManipulationTargetRotation);
                _activeManipulationRect.localScale =
                    Vector3.one * _deckManipulationTargetScale;
                if (_activeWindow == DeckWindowKind.Settings)
                {
                    _activeManipulationRect.sizeDelta =
                        _deckManipulationTargetSize;
                    LayoutSettingsDeck();
                }
                // When low-light spatial fallback is active it owns a parallel
                // pose snapshot. Synchronise that snapshot with the exact
                // release pose; otherwise the next Update restores the previous
                // smoothed frame and visibly pushes the window backwards.
                CommitManualPlacementToTrackingFallback();
                PlayerPrefs.Save();
            }
            _deckManipulationSmoothing = false;
            CompleteWindowBlockManipulation();
            _deckManipulationMode = DeckManipulationMode.None;
            _deckManipulationUsesSize = false;
            _deckManipulationUsesCrop = false;
            _tiltGestureAxis = TiltGestureAxis.Undecided;
            _deckHoverMode = DeckManipulationMode.None;
            RevealDeckAffordances(completedWindow);
            SetDeckHandleVisuals(
                DeckManipulationMode.None,
                DeckWindowKind.None);
        }

        private DeckManipulationMode ClassifyDeckManipulationHandle(
            Vector3 worldPoint,
            out DeckWindowKind window)
        {
            window = DeckWindowKind.None;
            _hoverExternalWindow = null;
            if (_manualFrozenWindows) return DeckManipulationMode.None;
            if (
                !_deckMinimized &&
                IsPointInsideExternalHandle(_deckCloseHandle, worldPoint))
            {
                window = DeckWindowKind.Workspace;
                return DeckManipulationMode.Minimize;
            }
            DeckManipulationMode mode = ClassifyWindowHandle(
                _spatialDeckRect,
                !_deckMinimized,
                worldPoint);
            if (mode != DeckManipulationMode.None)
            {
                window = DeckWindowKind.Workspace;
                return mode;
            }
            mode = ClassifyWindowHandle(
                _settingsDeckRect,
                _settingsDeck != null && _settingsDeck.gameObject.activeSelf,
                worldPoint);
            if (
                _settingsDeck != null &&
                _settingsDeck.gameObject.activeSelf &&
                IsPointInsideExternalHandle(_settingsCloseHandle, worldPoint))
                mode = DeckManipulationMode.Minimize;
            if (
                _settingsDeck != null &&
                _settingsDeck.gameObject.activeSelf &&
                IsPointInsideExternalHandle(_settingsFreeResizeHandle, worldPoint))
                mode = DeckManipulationMode.ResizeFree;
            if (mode != DeckManipulationMode.None)
            {
                window = DeckWindowKind.Settings;
                return mode;
            }
            mode = ClassifyExternalWindowHandle(
                worldPoint,
                out _hoverExternalWindow);
            if (mode != DeckManipulationMode.None)
                window = DeckWindowKind.External;
            return mode;
        }

        private static DeckManipulationMode ClassifyWindowHandle(
            RectTransform windowRect,
            bool active,
            Vector3 worldPoint)
        {
            if (!active || windowRect == null) return DeckManipulationMode.None;
            Vector3 local3 = windowRect.InverseTransformPoint(worldPoint);
            if (Mathf.Abs(local3.z) > 45f) return DeckManipulationMode.None;
            Vector2 local = new Vector2(local3.x, local3.y);
            Rect rect = windowRect.rect;
            if (!rect.Contains(local)) return DeckManipulationMode.None;
            float edge = Mathf.Min(95f, rect.width * .20f);
            // Keep manipulation strictly inside the bottom rim. The former
            // 85 px band overlapped the portrait LUM/EC row, so a valid pinch
            // could be claimed before the Button received it.
            float bottom = Mathf.Min(40f, rect.height * .10f);
            if (local.y <= rect.yMin + bottom)
            {
                if (local.x <= rect.xMin + edge)
                    return DeckManipulationMode.ResizeLeft;
                if (local.x >= rect.xMax - edge)
                    return DeckManipulationMode.ResizeRight;
                // Dedicated central zones keep translation, depth and tilt
                // independent. This prevents an ordinary move from changing
                // the panel angle.
                if (local.x < -20f) return DeckManipulationMode.Move;
                if (local.x < 65f) return DeckManipulationMode.Depth;
                return DeckManipulationMode.Tilt;
            }
            return DeckManipulationMode.None;
        }

        private static bool IsResizeMode(DeckManipulationMode mode) =>
            mode == DeckManipulationMode.ResizeLeft ||
            mode == DeckManipulationMode.ResizeRight;

        private void SetDeckHandleVisuals(
            DeckManipulationMode mode,
            DeckWindowKind window)
        {
            if (_manualFrozenWindows)
            {
                Graphic[] frozenHandles =
                {
                    _deckMoveHandle,
                    _deckResizeHandle,
                    _deckResizeHandleRight,
                    _deckDepthHandle,
                    _deckTiltHandle,
                    _deckCloseHandle,
                    _settingsMoveHandle,
                    _settingsResizeHandle,
                    _settingsResizeHandleRight,
                    _settingsDepthHandle,
                    _settingsTiltHandle,
                    _settingsFreeResizeHandle,
                    _settingsCloseHandle,
                };
                for (int i = 0; i < frozenHandles.Length; i++)
                    if (frozenHandles[i] != null)
                        frozenHandles[i].gameObject.SetActive(false);
                SetExternalWindowHandleVisuals(
                    DeckManipulationMode.None,
                    DeckWindowKind.None);
                return;
            }
            bool revealWorkspace =
                Time.unscaledTime < _deckAffordanceRevealUntil &&
                _deckAffordanceRevealWindow == DeckWindowKind.Workspace;
            bool revealSettings =
                Time.unscaledTime < _deckAffordanceRevealUntil &&
                _deckAffordanceRevealWindow == DeckWindowKind.Settings;
            SetVisionHandle(
                _deckMoveHandle,
                revealWorkspace,
                mode,
                window,
                DeckManipulationMode.Move,
                DeckWindowKind.Workspace);
            SetVisionHandle(
                _deckResizeHandle,
                revealWorkspace,
                mode,
                window,
                DeckManipulationMode.ResizeLeft,
                DeckWindowKind.Workspace);
            SetVisionHandle(
                _deckResizeHandleRight,
                revealWorkspace,
                mode,
                window,
                DeckManipulationMode.ResizeRight,
                DeckWindowKind.Workspace);
            SetVisionHandle(
                _deckDepthHandle,
                revealWorkspace,
                mode,
                window,
                DeckManipulationMode.Depth,
                DeckWindowKind.Workspace);
            SetVisionHandle(
                _deckTiltHandle,
                revealWorkspace,
                mode,
                window,
                DeckManipulationMode.Tilt,
                DeckWindowKind.Workspace);
            SetVisionHandle(
                _deckCloseHandle,
                revealWorkspace,
                mode,
                window,
                DeckManipulationMode.Minimize,
                DeckWindowKind.Workspace);
            SetVisionHandle(
                _settingsMoveHandle,
                revealSettings,
                mode,
                window,
                DeckManipulationMode.Move,
                DeckWindowKind.Settings);
            SetVisionHandle(
                _settingsResizeHandle,
                revealSettings,
                mode,
                window,
                DeckManipulationMode.ResizeLeft,
                DeckWindowKind.Settings);
            SetVisionHandle(
                _settingsResizeHandleRight,
                revealSettings,
                mode,
                window,
                DeckManipulationMode.ResizeRight,
                DeckWindowKind.Settings);
            SetVisionHandle(
                _settingsDepthHandle,
                revealSettings,
                mode,
                window,
                DeckManipulationMode.Depth,
                DeckWindowKind.Settings);
            SetVisionHandle(
                _settingsTiltHandle,
                revealSettings,
                mode,
                window,
                DeckManipulationMode.Tilt,
                DeckWindowKind.Settings);
            SetVisionHandle(
                _settingsFreeResizeHandle,
                revealSettings,
                mode,
                window,
                DeckManipulationMode.ResizeFree,
                DeckWindowKind.Settings);
            SetVisionHandle(
                _settingsCloseHandle,
                revealSettings,
                mode,
                window,
                DeckManipulationMode.Minimize,
                DeckWindowKind.Settings);
            SetExternalWindowHandleVisuals(mode, window);
        }

        private void SetVisionHandle(
            Graphic handle,
            bool reveal,
            DeckManipulationMode hoverMode,
            DeckWindowKind hoverWindow,
            DeckManipulationMode ownMode,
            DeckWindowKind ownWindow)
        {
            if (handle == null) return;
            bool targeted = hoverWindow == ownWindow && hoverMode == ownMode;
            bool engaged =
                _deckManipulationMode == ownMode &&
                _activeWindow == ownWindow;
            handle.gameObject.SetActive(reveal || targeted || engaged);
            if (!handle.gameObject.activeSelf) return;
            if (ownMode == DeckManipulationMode.ResizeFree)
            {
                handle.color = engaged
                    ? new Color(.48f, .51f, .59f, .98f)
                    : (targeted
                        ? new Color(.36f, .38f, .44f, .96f)
                        : new Color(.22f, .23f, .27f, .88f));
                Graphic[] freeResizeGraphics =
                    handle.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < freeResizeGraphics.Length; i++)
                    if (freeResizeGraphics[i] != handle)
                        freeResizeGraphics[i].color = Color.white;
                return;
            }
            Color color = engaged
                ? Color.white
                : (targeted
                    ? new Color(.94f, .95f, .98f, .98f)
                    : new Color(.72f, .74f, .79f, .72f));
            Graphic[] graphics = handle.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (
                    (ownMode == DeckManipulationMode.Depth ||
                     ownMode == DeckManipulationMode.Tilt) &&
                    graphics[i] == handle)
                    graphics[i].color = new Color(
                        color.r,
                        color.g,
                        color.b,
                        engaged ? .18f : (targeted ? .14f : .06f));
                else
                    graphics[i].color = color;
            }
        }

        private void RevealDeckAffordances(
            DeckWindowKind window,
            float seconds = 4f)
        {
            if (window == DeckWindowKind.None) return;
            if (window == DeckWindowKind.External)
                _externalAffordanceWindow = _activeExternalWindow;
            _deckAffordanceRevealWindow = window;
            _deckAffordanceRevealUntil = Time.unscaledTime + seconds;
        }

        /// <summary>
        /// A held open palm recentres the active visible window. It only reopens
        /// the last window when every window is closed.
        /// </summary>
        public void OpenDeckFromPalm()
        {
            if (_windowDock != null) _windowDock.gameObject.SetActive(false);
            ExternalSpatialWindowState visibleExternal =
                IsExternalWindowVisible(_activeExternalWindow)
                    ? _activeExternalWindow
                    : LastVisibleExternalWindow();
            bool workspaceVisible =
                !_deckMinimized &&
                _spatialDeckRect != null &&
                _spatialDeckRect.gameObject.activeInHierarchy;
            bool settingsVisible =
                _settingsDeck != null && _settingsDeck.gameObject.activeSelf;

            // Prefer the last genuinely focused visible surface. A stale
            // External enum must never fall through to reopening Pupitre.
            if (
                _lastWindow == DeckWindowKind.External &&
                visibleExternal != null)
            {
                _activeExternalWindow = visibleExternal;
                _lastExternalWindow = visibleExternal;
                PlaceWindowAtCameraLocal(
                    _activeExternalWindow.Rect,
                    new Vector3(0f, .04f, 1.08f));
                RevealExternalWindowAffordances(_activeExternalWindow);
                ShowGestureToast(
                    "FENETRE RECENTREE // PAUME",
                    new Color(.35f, 1f, .94f));
                return;
            }
            DeckWindowKind target = DeckWindowKind.None;
            if (_lastWindow == DeckWindowKind.Settings && settingsVisible)
                target = DeckWindowKind.Settings;
            else if (_lastWindow == DeckWindowKind.Workspace && workspaceVisible)
                target = DeckWindowKind.Workspace;
            else if (settingsVisible)
                target = DeckWindowKind.Settings;
            else if (workspaceVisible)
                target = DeckWindowKind.Workspace;

            // No active surface: and only in that case, recall the last one.
            bool recalledClosedWindow = target == DeckWindowKind.None;
            if (target == DeckWindowKind.None)
            {
                if (_osOnlyMode)
                {
                    OpenWindowDockFromTwoPalms();
                    ShowGestureToast(
                        "XREEL OS // DOCK",
                        new Color(.55f, .78f, 1f));
                    return;
                }
                // An Android/browser window has its own provider lifecycle and
                // cannot be recreated as Pupitre. Surface the app dock instead
                // of reopening the wrong window.
                if (_lastWindow == DeckWindowKind.External)
                {
                    OpenWindowDockFromTwoPalms();
                    ShowGestureToast(
                        "APPLICATION FERMEE // CHOISIS DANS LE DOCK",
                        new Color(.55f, .78f, 1f));
                    return;
                }
                target = _lastWindow == DeckWindowKind.Settings
                    ? DeckWindowKind.Settings
                    : DeckWindowKind.Workspace;
                if (target == DeckWindowKind.Settings)
                    OpenSettingsDeck(true);
                else if (_deckMinimized)
                    SetDeckMinimized(false);
            }

            _lastWindow = target;
            RectTransform rect = RectForWindow(target);
            if (rect == null) return;
            if (!recalledClosedWindow)
            {
                if (target == DeckWindowKind.Settings)
                    rect.localScale = Vector3.one * Mathf.Clamp(
                        rect.localScale.x,
                        .00046f,
                        .00078f);
                PlaceWindowAtCameraLocal(
                    rect,
                    target == DeckWindowKind.Settings
                        ? new Vector3(0f, .06f, .96f)
                        : new Vector3(0f, .04f, 1.12f));
                // A palm recenter is deliberately temporary.  It must not
                // overwrite the manually arranged pose which close/reopen and
                // the dock are expected to recall.
                Debug.Log(
                    "[AtelierWindowMemory] temporary_recenter window=" +
                    target);
            }
            else
                Debug.Log(
                    "[AtelierWindowMemory] recalled window=" + target +
                    " local=" +
                    _camera.transform.InverseTransformPoint(rect.position));
            RevealDeckAffordances(target);
            SetDeckHandleVisuals(DeckManipulationMode.None, DeckWindowKind.None);
            _status = recalledClosedWindow
                ? (target == DeckWindowKind.Settings
                    ? "PARAMÈTRES ROUVERTS // DERNIÈRE POSITION"
                    : "PUPITRE ROUVERT // DERNIÈRE POSITION")
                : (target == DeckWindowKind.Settings
                    ? "PARAMÈTRES RECENTRÉS // PAUME"
                    : "PUPITRE RECENTRÉ // PAUME");
            ShowGestureToast(_status, new Color(.35f, 1f, .94f));
            RefreshSpatialDeck();
            RefreshSettingsDeck();
        }

        /// <summary>Visible feedback for the physical fist power toggle.</summary>
        public void SetGestureStandby(bool standby)
        {
            SetWindowsSuspendedForGestureStandby(standby);
            _status = standby
                ? "GESTES EN VEILLE // FERME LE POING POUR RÉACTIVER"
                : "GESTES ACTIFS // 25 FPS";
            ShowGestureToast(
                standby ? "GESTES EN VEILLE • 10 FPS" : "GESTES ACTIFS • 25 FPS",
                standby
                    ? new Color(1f, .66f, .24f)
                    : new Color(.25f, 1f, .9f));
            RefreshSpatialDeck();
            RefreshSettingsDeck();
        }

        private void SetDeckMinimized(bool minimized)
        {
            if (_deckMinimized == minimized) return;
            if (minimized) EndDeckManipulation();
            _deckMinimized = minimized;
            if (_spatialDeck != null) _spatialDeck.enabled = !minimized;
            for (int i = 0; i < _deckExpandedRoots.Count; i++)
            {
                GameObject root = _deckExpandedRoots[i];
                if (root != null) root.SetActive(!minimized);
            }
            if (!minimized)
            {
                _lastWindow = DeckWindowKind.Workspace;
                SetDeckHandleVisuals(
                    DeckManipulationMode.None,
                    DeckWindowKind.None);
                SetDeckPose();
            }
            else if (_gestureToastCanvas != null)
            {
                _gestureToastCanvas.gameObject.SetActive(false);
                _gestureToastHideAt = -1f;
            }
        }

        /// <summary>
        /// MediaPipe produces hand anchors slower than the 60 Hz display. Keep
        /// the latest hand-derived target, then interpolate the world-space deck
        /// every rendered frame so sparse inference never becomes visible steps.
        /// </summary>
        private void SmoothDeckManipulation()
        {
            if (!_deckManipulationSmoothing || _activeManipulationRect == null)
                return;
            float blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            _activeManipulationRect.position = Vector3.Lerp(
                _activeManipulationRect.position,
                _deckManipulationTargetPosition,
                blend);
            _activeManipulationRect.rotation = Quaternion.Slerp(
                _activeManipulationRect.rotation,
                _deckManipulationTargetRotation,
                blend);
            float scale = Mathf.Lerp(
                _activeManipulationRect.localScale.x,
                _deckManipulationTargetScale,
                blend);
            _activeManipulationRect.localScale = Vector3.one * scale;
            Vector2 size = Vector2.Lerp(
                _activeManipulationRect.sizeDelta,
                _deckManipulationTargetSize,
                blend);
            // Rebuilding every Settings row while merely moving/tilting the
            // window dirties the complete world-space Canvas each rendered frame.
            // Layout is necessary only for the explicit free-size manipulation.
            if (_activeWindow == DeckWindowKind.Settings &&
                _deckManipulationUsesSize)
            {
                _activeManipulationRect.sizeDelta = size;
                LayoutSettingsDeck();
            }
            else if (
                _activeWindow == DeckWindowKind.External &&
                _deckManipulationUsesSize)
                ApplyExternalWindowSize(size, false);
            else if (
                _activeWindow == DeckWindowKind.External &&
                _deckManipulationUsesCrop)
                SmoothExternalCropManipulation(_activeExternalWindow, size);

            ApplyWindowBlockManipulation();

            if (
                _deckManipulationMode == DeckManipulationMode.None &&
                Vector3.Distance(
                    _activeManipulationRect.position,
                    _deckManipulationTargetPosition) < .001f &&
                Quaternion.Angle(
                    _activeManipulationRect.rotation,
                    _deckManipulationTargetRotation) < .1f &&
                 Mathf.Abs(scale - _deckManipulationTargetScale) < .000002f &&
                 (_activeWindow != DeckWindowKind.Settings ||
                  Vector2.Distance(size, _deckManipulationTargetSize) < .25f) &&
                 (!_deckManipulationUsesCrop ||
                  Vector2.Distance(size, _deckManipulationTargetSize) < .25f))
            {
                _activeManipulationRect.position = _deckManipulationTargetPosition;
                _activeManipulationRect.rotation = _deckManipulationTargetRotation;
                _activeManipulationRect.localScale =
                    Vector3.one * _deckManipulationTargetScale;
                if (_activeWindow == DeckWindowKind.Settings &&
                    _deckManipulationUsesSize)
                {
                    _activeManipulationRect.sizeDelta =
                        _deckManipulationTargetSize;
                    LayoutSettingsDeck();
                }
                _deckManipulationSmoothing = false;
                CompleteWindowBlockManipulation();
            }
        }

        private void ExportFromSpatialDeck()
        {
            if (Spatial?.CreatorMap == null) return;
            if (!Spatial.PrepareCreatorExport(out string error))
            {
                _status = "EXPORT REFUSÉ // " + error;
                return;
            }
            _exchange.BeginExport(
                Spatial.CreatorMap,
                "mlomega-" + Spatial.CreatorMap.WorldMapId);
        }

        private void SelectVisiblePreset(int slot)
        {
            int index = _page * 12 + slot;
            if (index >= 0 && index < _visible.Count)
                SelectPreset(_visible[index]);
        }

        private int PageCount =>
            Mathf.Max(1, Mathf.CeilToInt(_visible.Count / 12f));

        private void RefreshSpatialDeck()
        {
            if (_spatialDeck == null) return;
            if (_deckStatus != null) _deckStatus.text = _status;
            if (_deckScale != null)
                _deckScale.text = _uniformScale.ToString("0.0×");
            if (_deckMotionLabel != null)
                _deckMotionLabel.text =
                    "MOUV: " + MotionPaths[_motionIndex].ToUpperInvariant();
            if (_deckPage != null)
                _deckPage.text = (_page + 1) + "/" + PageCount;
            if (_deckAsset != null)
                _deckAsset.text = string.IsNullOrEmpty(_pendingAssetId)
                    ? "AUCUN ASSET"
                    : (Spatial?.CreatorMap?.FindAsset(_pendingAssetId)?.kind ==
                        "glb_model"
                        ? "MODÈLE GLB PRÊT ✓"
                        : "LOGO 3D PRÊT ✓");
            if (_deckCommitLabel != null)
                _deckCommitLabel.text = _dynamicMode
                    ? "LIER AU FLUX DYNAMIQUE"
                    : "ANCRER DANS LE MONDE";
            if (_deckModeLabel != null)
                _deckModeLabel.text = _dynamicMode
                    ? "MODE DYNAMIQUE"
                    : "MODE ANCRÉ";
            if (_deckKindLabel != null)
                _deckKindLabel.text =
                    "CIBLE: " + DynamicKinds[_dynamicKindIndex].ToUpperInvariant();
            if (_deckAttachmentLabel != null)
                _deckAttachmentLabel.text =
                    "POS: " + Attachments[_attachmentIndex].ToUpperInvariant();
            if (_deckTarget != null && !_deckTarget.isFocused)
                _deckTarget.SetTextWithoutNotify(_dynamicTargetLabel);
            if (_deckManagedLabel != null)
                _deckManagedLabel.text = ManagedLabel();
            if (_deckMapLabel != null)
            {
                string mapName = Spatial?.CreatorMap?.Document.displayName ??
                    "MAP";
                _deckMapLabel.text = mapName.ToUpperInvariant() + " ▶";
            }
            if (_deckLabel != null && !_deckLabel.isFocused)
                _deckLabel.SetTextWithoutNotify(_label);
            if (_deckSubtitle != null && !_deckSubtitle.isFocused)
                _deckSubtitle.SetTextWithoutNotify(_subtitle);
            for (int i = 0; i < _deckCategoryButtons.Count; i++)
                TintButton(
                    _deckCategoryButtons[i],
                    Categories[i] == _category);
            for (int i = 0; i < _deckPresetButtons.Count; i++)
            {
                int index = _page * 12 + i;
                bool available = index < _visible.Count;
                _deckPresetButtons[i].gameObject.SetActive(available);
                if (!available) continue;
                WorldCreatorCatalog.Entry entry = _visible[index];
                _deckPresetLabels[i].text =
                    entry.label.ToUpperInvariant() + "\n<size=65%>" +
                    entry.archetypeId.Replace("-", " ") + "</size>";
                TintButton(
                    _deckPresetButtons[i],
                    _selected != null &&
                    _selected.presetId == entry.presetId);
            }
        }

        private void SetDeckPose()
        {
            _deckManipulationSmoothing = false;
            ApplyPreferredDeckPose();
            if (_spatialDeckRect != null)
            {
                _deckManipulationTargetPosition = _spatialDeckRect.position;
                _deckManipulationTargetRotation = _spatialDeckRect.rotation;
                _deckManipulationTargetScale = _spatialDeckRect.localScale.x;
            }
        }

        private void FollowSpatialDeck(bool snap)
        {
            if (_spatialDeckRect == null || _camera == null) return;
            Vector3 forward = _camera.transform.forward.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .96f
                ? _camera.transform.up
                : Vector3.up;
            Vector3 targetPosition =
                _camera.transform.position +
                forward * 1.12f -
                _camera.transform.up * .035f;
            Quaternion targetRotation = Quaternion.LookRotation(forward, up);

            if (snap || !_deckPoseInitialized)
            {
                _spatialDeckRect.SetPositionAndRotation(
                    targetPosition,
                    targetRotation);
                _deckPoseInitialized = true;
                return;
            }

            // Keep the editor deck world-stable inside a comfort dead-zone.
            // Following every sub-millimetre head-pose update made the dense UI
            // visibly swim even though the official XREAL rig itself was stable.
            float positionError = Vector3.Distance(
                _spatialDeckRect.position,
                targetPosition);
            float rotationError = Quaternion.Angle(
                _spatialDeckRect.rotation,
                targetRotation);
            if (positionError < .065f && rotationError < 4.5f)
                return;

            float blend = 1f - Mathf.Exp(-7f * Time.unscaledDeltaTime);
            _spatialDeckRect.position = Vector3.Lerp(
                _spatialDeckRect.position,
                targetPosition,
                blend);
            _spatialDeckRect.rotation = Quaternion.Slerp(
                _spatialDeckRect.rotation,
                targetRotation,
                blend);
        }

        private void ApplyPreferredDeckPose()
        {
            if (_spatialDeckRect == null || _camera == null) return;
            Vector3 local = PlayerPrefs.HasKey(DeckLayoutPrefix + "x")
                ? new Vector3(
                    PlayerPrefs.GetFloat(DeckLayoutPrefix + "x"),
                    PlayerPrefs.GetFloat(DeckLayoutPrefix + "y"),
                    PlayerPrefs.GetFloat(DeckLayoutPrefix + "z", 1.12f))
                // First opening: a comfortable upper-left placement. Once the
                // user moves/resizes it, the saved head-relative layout wins.
                : new Vector3(-.18f, .11f, 1.12f);
            Vector3 targetPosition = _camera.transform.TransformPoint(local);
            Vector3 forward = (targetPosition - _camera.transform.position).normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .96f
                ? _camera.transform.up
                : Vector3.up;
            _spatialDeckRect.SetPositionAndRotation(
                targetPosition,
                BuildWindowRotation(
                    forward,
                    PlayerPrefs.GetFloat(DeckLayoutPrefix + "tilt", 0f),
                    PlayerPrefs.GetFloat(DeckLayoutPrefix + "turn", 0f)));
            float scale = PlayerPrefs.GetFloat(
                DeckLayoutPrefix + "scale",
                .00062f);
            _spatialDeckRect.localScale = Vector3.one * Mathf.Clamp(
                scale,
                .00038f,
                .00108f);
            _deckPoseInitialized = true;
        }

        private void UpdateWindowFollowMode()
        {
            if (
                !_headFollowWindows ||
                _camera == null ||
                _deckManipulationMode != DeckManipulationMode.None)
                return;
            if (!_deckMinimized)
                FollowWindowFromSavedLayout(
                    _spatialDeckRect,
                    DeckLayoutPrefix,
                    new Vector3(-.18f, .11f, 1.12f));
            if (_settingsDeck != null && _settingsDeck.gameObject.activeSelf)
                FollowWindowFromSavedLayout(
                    _settingsDeckRect,
                    SettingsLayoutPrefix,
                    new Vector3(-.32f, .20f, .92f));
            FollowExternalWindowsFromSavedLayout();
        }

        private void FollowWindowFromSavedLayout(
            RectTransform window,
            string prefix,
            Vector3 fallbackLocal)
        {
            if (window == null || _camera == null) return;
            Vector3 local = PlayerPrefs.HasKey(prefix + "x")
                ? new Vector3(
                    PlayerPrefs.GetFloat(prefix + "x"),
                    PlayerPrefs.GetFloat(prefix + "y"),
                    PlayerPrefs.GetFloat(prefix + "z", fallbackLocal.z))
                : fallbackLocal;
            Vector3 target = _camera.transform.TransformPoint(local);
            Vector3 forward = (target - _camera.transform.position).normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .96f
                ? _camera.transform.up
                : Vector3.up;
            Quaternion rotation = BuildWindowRotation(
                forward,
                PlayerPrefs.GetFloat(prefix + "tilt", 0f),
                PlayerPrefs.GetFloat(prefix + "turn", 0f));
            float blend = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            window.position = Vector3.Lerp(window.position, target, blend);
            window.rotation = Quaternion.Slerp(window.rotation, rotation, blend);
        }

        private void SaveWindowLayout(
            DeckWindowKind window,
            Vector3 worldPosition,
            float scale)
        {
            if (_camera == null) return;
            string prefix = LayoutPrefixForWindow(window);
            Vector3 local = _camera.transform.InverseTransformPoint(worldPosition);
            PlayerPrefs.SetFloat(prefix + "x", local.x);
            PlayerPrefs.SetFloat(prefix + "y", local.y);
            PlayerPrefs.SetFloat(
                prefix + "z",
                Mathf.Clamp(local.z, .45f, 2.8f));
            PlayerPrefs.SetFloat(
                prefix + "scale",
                window == DeckWindowKind.External
                    ? Mathf.Clamp(scale, .00015f, .01000f)
                    : Mathf.Clamp(scale, .00038f, .00108f));
            PlayerPrefs.Save();
        }

        private static Quaternion BuildWindowRotation(
            Vector3 direction,
            float tiltDegrees,
            float turnDegrees)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontal.sqrMagnitude < .001f) horizontal = Vector3.forward;
            Quaternion upright = Quaternion.LookRotation(
                horizontal.normalized,
                Vector3.up);
            return upright * Quaternion.Euler(tiltDegrees, turnDegrees, 0f);
        }

        private static void SaveSettingsSize(Vector2 size)
        {
            PlayerPrefs.SetFloat(
                SettingsLayoutPrefix + "width",
                Mathf.Clamp(size.x, 500f, 1120f));
            PlayerPrefs.SetFloat(
                SettingsLayoutPrefix + "height",
                Mathf.Clamp(size.y, 520f, 1120f));
            PlayerPrefs.Save();
        }

        private void BuildSettingsDeck()
        {
            if (_settingsDeck != null || _camera == null) return;
            var go = new GameObject("Atelier Vision Control Center");
            _settingsDeck = go.AddComponent<Canvas>();
            _settingsDeck.renderMode = RenderMode.WorldSpace;
            _settingsDeck.worldCamera = _camera;
            _settingsDeck.sortingOrder = 110;
            go.AddComponent<GraphicRaycaster>();
            _settingsDeckRect = go.GetComponent<RectTransform>();
            _settingsDeckRect.sizeDelta = new Vector2(840f, 620f);
            _settingsDeckRect.localScale = Vector3.one * .00062f;

            _settingsWindowRim = MakeImage(
                _settingsDeckRect, "Vision window fine rim", Vector2.zero,
                new Vector2(818f, 598f),
                new Color(.88f, .91f, .98f, .10f));
            _settingsWindowRim.raycastTarget = false;
            _settingsWindowSurface = MakeImage(
                _settingsDeckRect, "Vision window optical glass", Vector2.zero,
                new Vector2(814f, 594f),
                new Color(.105f, .112f, .132f, .48f));
            _settingsWindowSurface.raycastTarget = false;
            _settingsHeaderSurface = MakeImage(
                _settingsDeckRect, "Vision window diagnostics material",
                new Vector2(0f, 192f), new Vector2(782f, 196f),
                new Color(.045f, .052f, .068f, .72f));
            _settingsHeaderSurface.sprite = GetVisionTopRoundedSprite();
            _settingsHeaderSurface.type = Image.Type.Sliced;
            _settingsHeaderSurface.raycastTarget = false;

            _settingsPortraitButton = MakeOrientationButton(
                _settingsDeckRect, "Portrait", VisionIconKind.Portrait,
                () => SetSettingsOrientation(false));
            _settingsLandscapeButton = MakeOrientationButton(
                _settingsDeckRect, "Landscape", VisionIconKind.Landscape,
                () => SetSettingsOrientation(true));
            _settingsBlockButton = MakeOrientationButton(
                _settingsDeckRect, "Settings Block", VisionIconKind.Lock,
                () => ToggleWindowBlock(DeckWindowKind.Settings, null));
            InitializeWindowBlockState(DeckWindowKind.Settings, null);

            _settingsTitleLabel = MakeText(
                _settingsDeckRect, "--:--", new Vector2(0f, 280f),
                new Vector2(300f, 52f), 31f, VisionText);
            _settingsTitleLabel.characterSpacing = 1.5f;

            _settingsDevicePill = MakeCircularStatusGauge(
                _settingsDeckRect, "Telephone", VisionIconKind.Phone,
                new Vector2(-145f, 190f), out _settingsDeviceLabel,
                out _settingsBatteryRing);
            _settingsLensPill = MakeCircularStatusGauge(
                _settingsDeckRect, "Temperature", VisionIconKind.Temperature,
                new Vector2(0f, 190f), out _settingsTemperatureLabel,
                out _settingsTemperatureRing);
            _settingsTrackingPill = MakeCircularStatusGauge(
                _settingsDeckRect, "Tracking", VisionIconKind.Tracking,
                new Vector2(145f, 190f), out _settingsTrackingLabel,
                out _settingsTrackingRing);
            _settingsAudioPill = MakeCircularStatusGauge(
                _settingsDeckRect, "Son", VisionIconKind.Audio,
                new Vector2(220f, 190f), out _settingsAudioLabel,
                out _settingsAudioRing);

            _settingsWindowModeButton = MakeVisionControlButton(
                _settingsDeckRect, "WINDOW MODE", VisionIconKind.Window,
                "Fenetres", new Vector2(-255f, 70f), ToggleWindowMode);
            _settingsWindowModeLabel = CaptionFor(_settingsWindowModeButton);
            _settingsGesturesButton = MakeVisionControlButton(
                _settingsDeckRect, "GESTURE MODE", VisionIconKind.Hand,
                "Gestes", new Vector2(-85f, 70f), ToggleGesturePower);
            _settingsGestureLabel = CaptionFor(_settingsGesturesButton);
            _settingsRayButton = MakeVisionControlButton(
                _settingsDeckRect, "EYE RAY", VisionIconKind.Eye,
                "Curseur", new Vector2(85f, 70f), ToggleEyeRay);
            _settingsRayLabel = CaptionFor(_settingsRayButton);
            _settingsRecenterButton = MakeVisionControlButton(
                _settingsDeckRect, "RECENTER UI", VisionIconKind.Recenter,
                "Recentrer", new Vector2(255f, 70f), RecenterAllWindows);

            _settingsBrightnessControl = MakeStepperControl(
                _settingsDeckRect, "Luminosite", VisionIconKind.Brightness,
                new Vector2(-220f, -95f),
                () => AdjustLensControl(false, -1),
                () => AdjustLensControl(false, 1),
                out _settingsLensButtons[0], out _settingsLensButtons[1],
                out _settingsBrightnessRing);
            _settingsEcControl = MakeStepperControl(
                _settingsDeckRect, "Lentilles", VisionIconKind.Glasses,
                new Vector2(0f, -95f),
                () => AdjustLensControl(true, -1),
                () => AdjustLensControl(true, 1),
                out _settingsLensButtons[2], out _settingsLensButtons[3],
                out _settingsEcRing);
            _settingsVolumeControl = MakeStepperControl(
                _settingsDeckRect, "Volume", VisionIconKind.Audio,
                new Vector2(220f, -95f),
                () => AdjustMediaVolume(-1),
                () => AdjustMediaVolume(1),
                out _settingsVolumeDownButton, out _settingsVolumeUpButton,
                out _settingsVolumeControlRing);
            _settingsLensLabel = MakeText(
                _settingsDeckRect, "LUM --/--  |  EC --/--",
                new Vector2(0f, -205f), new Vector2(370f, 22f),
                11f, VisionSecondary);
            _settingsCloseAllButton = MakeVisionControlButton(
                _settingsDeckRect, "CLOSE ALL", VisionIconKind.Close,
                "Tout fermer", new Vector2(0f, -245f), CloseAllWindows,
                58f);

            _settingsFollowLabel = MakeText(
                _settingsDeckRect, "SUIVI TETE = CHOIX MANUEL",
                new Vector2(0f, -270f), new Vector2(510f, 24f),
                11f, new Color(.62f, .82f, 1f));
            _settingsFollowLabel.gameObject.SetActive(false);

            BuildSettingsWindowHandles();
            LayoutSettingsDeck();
            _settingsHitGraphics.Clear();
            _settingsDeckRect.GetComponentsInChildren(true, _settingsHitGraphics);
            go.SetActive(false);
            ProbeLensControl();
            RefreshSettingsDeck();
        }

        private void BuildSettingsWindowHandles()
        {
            _settingsMoveHandle = MakeImage(
                _settingsDeckRect, "Settings gaze move handle",
                new Vector2(-48f, -300f), new Vector2(104f, 7f),
                new Color(.76f, .78f, .82f, .78f));
            _settingsMoveHandle.raycastTarget = false;
            AddVisionHandleDot(_settingsMoveHandle, false);
            _settingsMoveHandle.gameObject.SetActive(false);
            _settingsResizeHandle = MakeImage(
                _settingsDeckRect, "Settings gaze resize handle",
                new Vector2(-397f, -287f), Vector2.one * 48f,
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionResizeHandle(_settingsResizeHandle, false);
            _settingsResizeHandle.raycastTarget = false;
            _settingsResizeHandle.gameObject.SetActive(false);
            _settingsResizeHandleRight = MakeImage(
                _settingsDeckRect, "Settings gaze resize handle right",
                new Vector2(397f, -287f), Vector2.one * 48f,
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionResizeHandle(_settingsResizeHandleRight, true);
            _settingsResizeHandleRight.raycastTarget = false;
            _settingsResizeHandleRight.gameObject.SetActive(false);
            _settingsDepthHandle = MakeImage(
                _settingsDeckRect, "Settings gaze depth handle",
                new Vector2(82f, -300f), Vector2.one * 34f,
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionDepthHandle(_settingsDepthHandle);
            _settingsDepthHandle.raycastTarget = false;
            _settingsDepthHandle.gameObject.SetActive(false);
            _settingsTiltHandle = MakeImage(
                _settingsDeckRect, "Settings gaze tilt handle",
                new Vector2(130f, -300f), Vector2.one * 34f,
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionTiltHandle(_settingsTiltHandle);
            _settingsTiltHandle.raycastTarget = false;
            _settingsTiltHandle.gameObject.SetActive(false);
            _settingsFreeResizeHandle = MakeVisionFreeResizeHandle(
                _settingsDeckRect,
                new Vector2(-324f, -300f));
            _settingsFreeResizeHandle.gameObject.SetActive(false);
            _settingsCloseHandle = MakeText(
                _settingsDeckRect, "x", new Vector2(397f, 287f),
                new Vector2(34f, 34f), 24f,
                new Color(.82f, .84f, .88f, .90f));
            _settingsCloseHandle.raycastTarget = false;
            _settingsCloseHandle.gameObject.SetActive(false);
        }

        private static Button MakeVisionControlButton(
            Transform parent,
            string internalName,
            VisionIconKind icon,
            string caption,
            Vector2 position,
            UnityEngine.Events.UnityAction action,
            float size = 78f)
        {
            Button button = MakeButton(
                parent, internalName, position, Vector2.one * size, action);
            ConfigureControlCenterButton(button, icon, caption);
            return button;
        }

        private static Button MakeOrientationButton(
            Transform parent,
            string name,
            VisionIconKind icon,
            UnityEngine.Events.UnityAction action)
        {
            Button button = MakeButton(
                parent,
                name,
                Vector2.zero,
                new Vector2(48f, 34f),
                action);
            Transform depthPlate = parent.Find("Button depth " + name);
            if (depthPlate != null) depthPlate.gameObject.SetActive(false);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            if (text != null) text.gameObject.SetActive(false);
            Image surface = button.GetComponent<Image>();
            if (surface != null)
            {
                surface.sprite = GetVisionRoundedSprite();
                surface.type = Image.Type.Sliced;
                surface.color = new Color(.16f, .17f, .20f, .72f);
            }
            BuildVisionIcon(button.transform, icon, Vector2.zero, .62f);
            button.gameObject.AddComponent<Components.VisionGazeReveal>()
                .Configure(0f, 1f);
            VisionSpatialControlFeedback feedback =
                button.gameObject.AddComponent<VisionSpatialControlFeedback>();
            feedback.Configure(
                surface,
                new Color(.16f, .17f, .20f, .72f),
                new Color(.38f, .40f, .44f, .92f),
                VisionPressed,
                VisionText);
            return button;
        }

        private static TextMeshProUGUI CaptionFor(Button button) =>
            button == null
                ? null
                : button.transform.Find("Vision caption")
                    ?.GetComponent<TextMeshProUGUI>();

        private static Image MakeCircularStatusGauge(
            Transform parent,
            string caption,
            VisionIconKind icon,
            Vector2 position,
            out TextMeshProUGUI valueLabel,
            out Image progressRing,
            float size = 92f)
        {
            Image root = MakeImage(
                parent, "Vision gauge " + caption, position,
                Vector2.one * size, new Color(.15f, .16f, .18f, .60f));
            root.sprite = GetVisionCircleSprite();
            root.type = Image.Type.Simple;
            root.raycastTarget = false;
            Image track = AddVisionProgressRing(
                root.transform, "Vision gauge track", Vector2.zero, size + 9f);
            track.fillAmount = 1f;
            track.color = new Color(.70f, .73f, .80f, .20f);
            progressRing = AddVisionProgressRing(
                root.transform, "Vision gauge progress", Vector2.zero,
                size + 9f);
            BuildVisionIcon(root.transform, icon, new Vector2(0f, 17f), .62f);
            valueLabel = MakeText(
                root.transform, "--", new Vector2(0f, -14f),
                new Vector2(size - 14f, 28f), 18f, VisionText,
                FontStyles.Bold);
            TextMeshProUGUI captionLabel = MakeText(
                root.transform, caption, new Vector2(0f, -size * .5f - 15f),
                new Vector2(size + 46f, 22f), 11f, VisionSecondary);
            captionLabel.enableWordWrapping = false;
            return root;
        }

        private static RectTransform MakeStepperControl(
            Transform parent,
            string caption,
            VisionIconKind icon,
            Vector2 position,
            UnityEngine.Events.UnityAction minusAction,
            UnityEngine.Events.UnityAction plusAction,
            out Button minusButton,
            out Button plusButton,
            out Image progressRing)
        {
            var go = new GameObject("Vision stepper " + caption);
            go.transform.SetParent(parent, false);
            RectTransform root = go.AddComponent<RectTransform>();
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.anchoredPosition = position;
            root.sizeDelta = new Vector2(150f, 132f);

            Image orb = MakeImage(
                root, "Vision stepper orb " + caption,
                new Vector2(0f, 25f), Vector2.one * 68f,
                new Color(.16f, .17f, .20f, .68f));
            orb.sprite = GetVisionCircleSprite();
            orb.type = Image.Type.Simple;
            orb.raycastTarget = false;
            Image track = AddVisionProgressRing(
                orb.transform, "Vision stepper track", Vector2.zero, 78f);
            track.fillAmount = 1f;
            track.color = new Color(.70f, .73f, .80f, .18f);
            progressRing = AddVisionProgressRing(
                orb.transform, "Vision stepper progress", Vector2.zero, 78f);
            BuildVisionIcon(orb.transform, icon, Vector2.zero, .92f);
            TextMeshProUGUI captionLabel = MakeText(
                root, caption, new Vector2(0f, -21f),
                new Vector2(140f, 22f), 11f, VisionSecondary);
            captionLabel.enableWordWrapping = false;

            Image bar = MakeImage(
                root, "Vision stepper bar " + caption,
                new Vector2(0f, -53f), new Vector2(116f, 31f),
                new Color(.13f, .14f, .17f, .76f));
            bar.raycastTarget = false;
            Image divider = MakeImage(
                bar.transform, "Vision stepper divider", Vector2.zero,
                new Vector2(1.5f, 17f),
                new Color(.72f, .75f, .82f, .32f));
            divider.raycastTarget = false;
            minusButton = MakeStepperButton(
                bar.transform, "minus", new Vector2(-29f, 0f), false,
                minusAction);
            plusButton = MakeStepperButton(
                bar.transform, "plus", new Vector2(29f, 0f), true,
                plusAction);
            return root;
        }

        private static Button MakeStepperButton(
            Transform parent,
            string name,
            Vector2 position,
            bool plus,
            UnityEngine.Events.UnityAction action)
        {
            Image hit = MakeImage(
                parent, "Vision stepper " + name, position,
                new Vector2(54f, 29f), new Color(1f, 1f, 1f, .01f));
            hit.raycastTarget = true;
            Button button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(action);
            var collider = hit.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(54f, 31f, 14f);
            Image horizontal = MakeImage(
                hit.transform, "Vision icon stepper line", Vector2.zero,
                new Vector2(15f, 2.5f), VisionText);
            horizontal.raycastTarget = false;
            if (plus)
            {
                Image vertical = MakeImage(
                    hit.transform, "Vision icon stepper line", Vector2.zero,
                    new Vector2(2.5f, 15f), VisionText);
                vertical.raycastTarget = false;
            }
            var feedback = hit.gameObject.AddComponent<
                VisionSpatialControlFeedback>();
            feedback.Configure(
                hit,
                new Color(1f, 1f, 1f, .01f),
                new Color(.55f, .58f, .64f, .32f),
                VisionPressed,
                VisionText);
            return button;
        }

        // Kept during the hardware-validated visual migration as a readable
        // rollback reference. It is never invoked by the product.
        private void BuildSettingsDeckLegacy()
        {
            if (_settingsDeck != null || _camera == null) return;
            var go = new GameObject("Atelier Interaction Settings");
            _settingsDeck = go.AddComponent<Canvas>();
            _settingsDeck.renderMode = RenderMode.WorldSpace;
            _settingsDeck.worldCamera = _camera;
            _settingsDeck.sortingOrder = 110;
            go.AddComponent<GraphicRaycaster>();
            _settingsDeckRect = go.GetComponent<RectTransform>();
            _settingsDeckRect.sizeDelta = new Vector2(840f, 560f);
            _settingsDeckRect.localScale = Vector3.one * .00062f;

            _settingsTitleLabel = MakeText(
                _settingsDeckRect,
                "MLOMEGA CONTROL CENTER",
                new Vector2(0f, 305f),
                new Vector2(510f, 48f),
                12f,
                VisionSecondary,
                FontStyles.Bold);
            _settingsDevicePill = MakeStatusPill(
                _settingsDeckRect,
                "APPAREIL",
                "--:-- • TEL --% • XREAL // TEMP NORMALE",
                new Vector2(0f, 260f),
                new Vector2(270f, 46f),
                out _settingsDeviceLabel);
            _settingsBatteryRing = AddVisionProgressRing(
                _settingsDevicePill.transform,
                "Battery progress",
                new Vector2(-98f, 0f),
                30f);
            _settingsWindowModeButton = MakeButton(
                _settingsDeckRect,
                "FENÊTRES",
                new Vector2(0f, 202f),
                new Vector2(470f, 52f),
                ToggleWindowMode);
            _settingsWindowModeLabel = _settingsWindowModeButton
                .GetComponentInChildren<TextMeshProUGUI>();
            ConfigureControlCenterButton(
                _settingsWindowModeButton,
                "⌖",
                "FENÊTRES");
            _settingsGesturesButton = MakeButton(
                _settingsDeckRect,
                "GESTES",
                new Vector2(0f, 139f),
                new Vector2(470f, 52f),
                ToggleGesturePower);
            _settingsGestureLabel = _settingsGesturesButton
                .GetComponentInChildren<TextMeshProUGUI>();
            ConfigureControlCenterButton(
                _settingsGesturesButton,
                "✋",
                "GESTES");
            _settingsRayButton = MakeButton(
                _settingsDeckRect,
                "RAYON EYE",
                new Vector2(0f, 76f),
                new Vector2(470f, 52f),
                ToggleEyeRay);
            _settingsRayLabel = _settingsRayButton
                .GetComponentInChildren<TextMeshProUGUI>();
            ConfigureControlCenterButton(
                _settingsRayButton,
                "◉",
                "RAYON");
            _settingsVolumeDownButton = MakeButton(
                _settingsDeckRect,
                "−",
                new Vector2(-190f, 13f),
                new Vector2(90f, 52f),
                () => AdjustMediaVolume(-1));
            ConfigureControlCenterButton(
                _settingsVolumeDownButton,
                "−",
                "SON");
            _settingsAudioPill = MakeStatusPill(
                _settingsDeckRect,
                "AUDIO",
                "AUDIO SYSTÈME // --%",
                new Vector2(0f, 13f),
                new Vector2(150f, 46f),
                out _settingsAudioLabel);
            _settingsAudioRing = AddVisionProgressRing(
                _settingsAudioPill.transform,
                "Audio progress",
                new Vector2(-48f, 0f),
                28f);
            _settingsVolumeUpButton = MakeButton(
                _settingsDeckRect,
                "+",
                new Vector2(190f, 13f),
                new Vector2(90f, 52f),
                () => AdjustMediaVolume(1));
            ConfigureControlCenterButton(
                _settingsVolumeUpButton,
                "+",
                "SON");
            _settingsRecenterButton = MakeButton(
                _settingsDeckRect,
                "RECENTRER UI",
                new Vector2(-120f, -57f),
                new Vector2(225f, 52f),
                RecenterAllWindows);
            ConfigureControlCenterButton(
                _settingsRecenterButton,
                "◎",
                "RECENTRER");
            _settingsCloseAllButton = MakeButton(
                _settingsDeckRect,
                "FERMER TOUT",
                new Vector2(120f, -57f),
                new Vector2(225f, 52f),
                CloseAllWindows);
            ConfigureControlCenterButton(
                _settingsCloseAllButton,
                "×",
                "FERMER");
            _settingsTrackingPill = MakeStatusPill(
                _settingsDeckRect,
                "TRACKING",
                "TRACKING // INITIALISATION",
                new Vector2(0f, -118f),
                new Vector2(220f, 46f),
                out _settingsTrackingLabel);
            _settingsLensPill = MakeStatusPill(
                _settingsDeckRect,
                "LENTILLES",
                "LUM --/-- • EC --/--",
                new Vector2(0f, -175f),
                new Vector2(250f, 46f),
                out _settingsLensLabel);
            _settingsFollowLabel = MakeText(
                _settingsDeckRect,
                "SUIVI TÊTE = CHOIX MANUEL • PAS DE BASCULE AUTO",
                new Vector2(0f, -225f),
                new Vector2(510f, 30f),
                12f,
                new Color(.62f, .82f, 1f));
            _settingsFollowLabel.gameObject.SetActive(false);
            _settingsLensButtons[0] = MakeButton(
                _settingsDeckRect,
                "LUM -",
                new Vector2(-183f, -280f),
                new Vector2(112f, 44f),
                () => AdjustLensControl(false, -1));
            ConfigureControlCenterButton(
                _settingsLensButtons[0],
                "☀−",
                "LUM");
            _settingsLensButtons[1] = MakeButton(
                _settingsDeckRect,
                "LUM +",
                new Vector2(-61f, -280f),
                new Vector2(112f, 44f),
                () => AdjustLensControl(false, 1));
            ConfigureControlCenterButton(
                _settingsLensButtons[1],
                "☀+",
                "LUM");
            _settingsBrightnessRing = AddVisionProgressRing(
                _settingsLensButtons[1].transform,
                "Brightness progress",
                Vector2.zero,
                104f);
            _settingsLensButtons[2] = MakeButton(
                _settingsDeckRect,
                "EC -",
                new Vector2(61f, -280f),
                new Vector2(112f, 44f),
                () => AdjustLensControl(true, -1));
            ConfigureControlCenterButton(
                _settingsLensButtons[2],
                "◐−",
                "TEINTE");
            _settingsLensButtons[3] = MakeButton(
                _settingsDeckRect,
                "EC +",
                new Vector2(183f, -280f),
                new Vector2(112f, 44f),
                () => AdjustLensControl(true, 1));
            ConfigureControlCenterButton(
                _settingsLensButtons[3],
                "◐+",
                "TEINTE");
            _settingsEcRing = AddVisionProgressRing(
                _settingsLensButtons[3].transform,
                "Electrochromic progress",
                Vector2.zero,
                104f);
            _settingsMoveHandle = MakeImage(
                _settingsDeckRect,
                "Settings gaze move handle",
                new Vector2(0f, -337f),
                new Vector2(120f, 7f),
                new Color(.76f, .78f, .82f, .78f));
            _settingsMoveHandle.raycastTarget = false;
            AddVisionHandleDot(_settingsMoveHandle, true);
            _settingsMoveHandle.gameObject.SetActive(false);
            _settingsResizeHandle = MakeImage(
                _settingsDeckRect,
                "Settings gaze resize handle",
                new Vector2(-267f, -330f),
                new Vector2(24f, 32f),
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionResizeHandle(_settingsResizeHandle, false);
            _settingsResizeHandle.raycastTarget = false;
            _settingsResizeHandle.gameObject.SetActive(false);
            _settingsResizeHandleRight = MakeImage(
                _settingsDeckRect,
                "Settings gaze resize handle right",
                new Vector2(267f, -330f),
                new Vector2(24f, 32f),
                new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionResizeHandle(_settingsResizeHandleRight, true);
            _settingsResizeHandleRight.raycastTarget = false;
            _settingsResizeHandleRight.gameObject.SetActive(false);
            _settingsDepthHandle = MakeImage(
                _settingsDeckRect,
                "Settings gaze depth handle",
                new Vector2(58f, -337f),
                new Vector2(72f, 7f),
                new Color(.76f, .78f, .82f, .78f));
            _settingsDepthHandle.raycastTarget = false;
            AddVisionHandleDot(_settingsDepthHandle, false);
            _settingsDepthHandle.gameObject.SetActive(false);
            _settingsCloseHandle = MakeText(
                _settingsDeckRect,
                "×",
                new Vector2(267f, 330f),
                new Vector2(34f, 34f),
                24f,
                new Color(.82f, .84f, .88f, .90f));
            _settingsCloseHandle.raycastTarget = false;
            _settingsCloseHandle.gameObject.SetActive(false);
            LayoutSettingsDeck();
            _settingsHitGraphics.Clear();
            _settingsDeckRect.GetComponentsInChildren(
                true,
                _settingsHitGraphics);
            go.SetActive(false);
            ProbeLensControl();
            RefreshSettingsDeck();
        }

        private void LayoutSettingsDeck()
        {
            if (_settingsDeckRect == null) return;
            Vector2 size = _settingsDeckRect.sizeDelta;
            float halfWidth = size.x * .5f;
            float halfHeight = size.y * .5f;
            bool landscape = size.x >= size.y;

            // The Canvas includes a narrow interaction gutter. The visible
            // glass is one continuous rounded surface inside it; handles and
            // orientation controls therefore sit genuinely outside the frame.
            const float sideGutter = 30f;
            const float verticalGutter = 48f;
            float surfaceLeft = -halfWidth + sideGutter;
            float surfaceRight = halfWidth - sideGutter;
            float surfaceTop = halfHeight - verticalGutter;
            float surfaceBottom = -halfHeight + verticalGutter;
            float surfaceWidth = surfaceRight - surfaceLeft;
            float surfaceHeight = surfaceTop - surfaceBottom;
            bool compact = surfaceWidth < 600f;
            Vector2 surfaceCenter = new Vector2(
                0f,
                (surfaceTop + surfaceBottom) * .5f);
            LayoutSurface(
                _settingsWindowRim,
                surfaceCenter,
                new Vector2(surfaceWidth + 4f, surfaceHeight + 4f));
            LayoutSurface(
                _settingsWindowSurface,
                surfaceCenter,
                new Vector2(surfaceWidth, surfaceHeight));

            // One window, two optical densities: the darker header touches the
            // same outer edges and has a flat lower edge. No nested card and no
            // separator line.
            float headerHeight = compact
                ? Mathf.Clamp(surfaceHeight * .37f, 235f, 282f)
                : Mathf.Clamp(surfaceHeight * .34f, 176f, 220f);
            float headerBottom = surfaceTop - headerHeight;
            LayoutSurface(
                _settingsHeaderSurface,
                new Vector2(0f, (surfaceTop + headerBottom) * .5f),
                new Vector2(surfaceWidth, headerHeight));
            if (_settingsControlsSurface != null)
                _settingsControlsSurface.gameObject.SetActive(false);
            if (_settingsSectionDivider != null)
                _settingsSectionDivider.gameObject.SetActive(false);

            LayoutButton(
                _settingsPortraitButton,
                new Vector2(surfaceLeft + 35f, surfaceTop - 27f),
                new Vector2(52f, 38f));
            LayoutButton(
                _settingsLandscapeButton,
                new Vector2(surfaceLeft + 95f, surfaceTop - 27f),
                new Vector2(52f, 38f));
            SetOrientationSelection(landscape);

            float clockScale = compact ? .88f : 1f;
            float clockY = surfaceTop - (compact ? 38f : 34f);
            LayoutScaledText(
                _settingsTitleLabel, new Vector2(0f, clockY),
                new Vector2(300f, 52f), clockScale);

            if (compact)
            {
                // Responsive portrait/narrow layout: information is reflowed,
                // never merely shrunk out of the visible glass.
                float gaugeScale = Mathf.Clamp(
                    (surfaceWidth - 90f) / 410f,
                    .72f,
                    .84f);
                float gaugeSize = 66f * gaugeScale;
                float gaugeX = Mathf.Min(102f, surfaceWidth * .22f);
                float gaugeTopY = surfaceTop - 112f;
                float gaugeBottomY = surfaceTop - 202f;
                LayoutGauge(_settingsDevicePill,
                    new Vector2(-gaugeX, gaugeTopY), gaugeSize);
                LayoutGauge(_settingsLensPill,
                    new Vector2(gaugeX, gaugeTopY), gaugeSize);
                LayoutGauge(_settingsTrackingPill,
                    new Vector2(-gaugeX, gaugeBottomY), gaugeSize);
                LayoutGauge(_settingsAudioPill,
                    new Vector2(gaugeX, gaugeBottomY), gaugeSize);

                float buttonScale = Mathf.Clamp(
                    (surfaceWidth - 70f) / 430f,
                    .72f,
                    .84f);
                float buttonStep = Mathf.Min(132f, surfaceWidth * .27f);
                float buttonRowOne = headerBottom - 54f;
                float buttonRowTwo = buttonRowOne - 96f;
                LayoutScaledButton(_settingsWindowModeButton,
                    new Vector2(-buttonStep, buttonRowOne), 78f, buttonScale);
                LayoutScaledButton(_settingsGesturesButton,
                    new Vector2(0f, buttonRowOne), 78f, buttonScale);
                LayoutScaledButton(_settingsRayButton,
                    new Vector2(buttonStep, buttonRowOne), 78f, buttonScale);
                LayoutScaledButton(_settingsRecenterButton,
                    new Vector2(-buttonStep * .5f, buttonRowTwo),
                    78f, buttonScale);
                LayoutScaledButton(_settingsCloseAllButton,
                    new Vector2(buttonStep * .5f, buttonRowTwo),
                    78f, buttonScale);

                float stepScale = Mathf.Clamp(
                    (surfaceWidth - 50f) / 475f,
                    .68f,
                    .82f);
                float stepX = Mathf.Min(134f, surfaceWidth * .28f);
                float stepY = surfaceBottom +
                    (HasOptionalLabSettingsActions() ? 230f : 92f);
                LayoutScaledRoot(_settingsBrightnessControl,
                    new Vector2(-stepX, stepY), stepScale);
                LayoutScaledRoot(_settingsEcControl,
                    new Vector2(0f, stepY), stepScale);
                LayoutScaledRoot(_settingsVolumeControl,
                    new Vector2(stepX, stepY), stepScale);
            }
            else
            {
                float contentScale = Mathf.Clamp(
                    surfaceWidth / 780f,
                    .88f,
                    1.08f);
                float gaugeY = surfaceTop - 116f;
                float gaugeStep = Mathf.Min(118f, surfaceWidth * .15f);
                LayoutGauge(_settingsDevicePill,
                    new Vector2(-gaugeStep * 1.5f, gaugeY),
                    66f * contentScale);
                LayoutGauge(_settingsLensPill,
                    new Vector2(-gaugeStep * .5f, gaugeY),
                    66f * contentScale);
                LayoutGauge(_settingsTrackingPill,
                    new Vector2(gaugeStep * .5f, gaugeY),
                    66f * contentScale);
                LayoutGauge(_settingsAudioPill,
                    new Vector2(gaugeStep * 1.5f, gaugeY),
                    66f * contentScale);

                // Five actions share one row when space permits. On a narrow
                // portrait window, the branch above becomes a deliberate 3+2.
                float buttonScale = Mathf.Clamp(contentScale, .88f, 1f);
                float buttonStep = (surfaceWidth - 80f) / 5f;
                float controlsY = headerBottom - 58f;
                LayoutScaledButton(_settingsWindowModeButton,
                    new Vector2(-buttonStep * 2f, controlsY),
                    78f, buttonScale);
                LayoutScaledButton(_settingsGesturesButton,
                    new Vector2(-buttonStep, controlsY),
                    78f, buttonScale);
                LayoutScaledButton(_settingsRayButton,
                    new Vector2(0f, controlsY), 78f, buttonScale);
                LayoutScaledButton(_settingsRecenterButton,
                    new Vector2(buttonStep, controlsY),
                    78f, buttonScale);
                LayoutScaledButton(_settingsCloseAllButton,
                    new Vector2(buttonStep * 2f, controlsY),
                    78f, buttonScale);

                float stepScale = Mathf.Clamp(contentScale, .84f, 1f);
                float stepX = Mathf.Min(220f, surfaceWidth * .28f);
                float stepY = Mathf.Lerp(
                    controlsY,
                    surfaceBottom,
                    HasOptionalLabSettingsActions() ? .48f : .56f);
                LayoutScaledRoot(_settingsBrightnessControl,
                    new Vector2(-stepX, stepY), stepScale);
                LayoutScaledRoot(_settingsEcControl,
                    new Vector2(0f, stepY), stepScale);
                LayoutScaledRoot(_settingsVolumeControl,
                    new Vector2(stepX, stepY), stepScale);
            }

            float lensScale = compact ? .80f : 1f;
            LayoutScaledText(
                _settingsLensLabel,
                new Vector2(
                    0f,
                    surfaceBottom +
                    (HasOptionalLabSettingsActions()
                        ? (compact ? 142f : 114f)
                        : 20f)),
                new Vector2(370f, 22f), lensScale);

            float bottom = -halfHeight + 17f;
            LayoutHandle(_settingsMoveHandle, new Vector2(-70f, bottom),
                new Vector2(104f, 5f));
            LayoutHandle(_settingsDepthHandle, new Vector2(72f, bottom),
                new Vector2(52f, 34f));
            LayoutHandle(_settingsTiltHandle, new Vector2(136f, bottom),
                new Vector2(52f, 34f));
            LayoutHandle(
                _settingsFreeResizeHandle,
                new Vector2(-halfWidth + 96f, bottom),
                new Vector2(52f, 32f));
            LayoutHandle(_settingsResizeHandle,
                new Vector2(-halfWidth + 20f, -halfHeight + 22f),
                Vector2.one * 52f);
            LayoutHandle(_settingsResizeHandleRight,
                new Vector2(halfWidth - 20f, -halfHeight + 22f),
                Vector2.one * 52f);
            LayoutRect(_settingsCloseHandle,
                new Vector2(surfaceRight + 14f, surfaceTop + 14f),
                new Vector2(34f, 34f));
            LayoutButton(_settingsBlockButton,
                new Vector2(surfaceRight - 36f, surfaceTop + 14f),
                new Vector2(48f, 34f));
            LayoutOptionalLabSettingsActions(surfaceWidth, surfaceBottom, compact);
        }

        private static void LayoutScaledText(
            TMP_Text label,
            Vector2 basePosition,
            Vector2 baseSize,
            float scale)
        {
            if (label == null) return;
            label.rectTransform.anchoredPosition = basePosition;
            label.rectTransform.sizeDelta = baseSize;
            label.rectTransform.localScale = Vector3.one * scale;
        }

        private static void LayoutSurface(
            Image surface,
            Vector2 position,
            Vector2 size)
        {
            if (surface == null) return;
            surface.rectTransform.anchoredPosition = position;
            surface.rectTransform.sizeDelta = size;
            surface.rectTransform.localScale = Vector3.one;
        }

        private static void LayoutScaledButton(
            Button button,
            Vector2 basePosition,
            float baseSize,
            float scale)
        {
            if (button == null) return;
            LayoutButton(button, basePosition, Vector2.one * baseSize);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.localScale = Vector3.one * scale;
            VisionSpatialControlFeedback feedback =
                button.GetComponent<VisionSpatialControlFeedback>();
            if (feedback != null) feedback.SetLayoutScale(scale);
        }

        private static void LayoutScaledRoot(
            RectTransform root,
            Vector2 basePosition,
            float scale)
        {
            if (root == null) return;
            root.anchoredPosition = basePosition;
            root.localScale = Vector3.one * scale;
        }

        private void LayoutSettingsDeckV16()
        {
            if (_settingsDeckRect == null) return;
            Vector2 size = _settingsDeckRect.sizeDelta;
            float halfWidth = size.x * .5f;
            float halfHeight = size.y * .5f;
            float gaugeSize = 66f;
            float gaugeGap = Mathf.Min(118f, (size.x - 170f) / 4f);
            float controlSize = 78f;
            float controlGap = Mathf.Min(166f, (size.x - 120f) / 4f);
            float stepGap = Mathf.Min(220f, (size.x - 160f) / 3f);

            LayoutRect(_settingsTitleLabel,
                new Vector2(0f, halfHeight - 28f), new Vector2(300f, 52f));
            float diagnosticsY = halfHeight - 112f;
            LayoutGauge(_settingsDevicePill,
                new Vector2(-1.5f * gaugeGap, diagnosticsY), gaugeSize);
            LayoutGauge(_settingsLensPill,
                new Vector2(-.5f * gaugeGap, diagnosticsY), gaugeSize);
            LayoutGauge(_settingsTrackingPill,
                new Vector2(.5f * gaugeGap, diagnosticsY), gaugeSize);
            LayoutGauge(_settingsAudioPill,
                new Vector2(1.5f * gaugeGap, diagnosticsY), gaugeSize);

            float controlsY = halfHeight - 235f;
            LayoutButton(_settingsWindowModeButton,
                new Vector2(-1.5f * controlGap, controlsY),
                Vector2.one * controlSize);
            LayoutButton(_settingsGesturesButton,
                new Vector2(-.5f * controlGap, controlsY),
                Vector2.one * controlSize);
            LayoutButton(_settingsRayButton,
                new Vector2(.5f * controlGap, controlsY),
                Vector2.one * controlSize);
            LayoutButton(_settingsRecenterButton,
                new Vector2(1.5f * controlGap, controlsY),
                Vector2.one * controlSize);

            float stepY = controlsY - 175f;
            LayoutRoot(_settingsBrightnessControl,
                new Vector2(-stepGap, stepY));
            LayoutRoot(_settingsEcControl, new Vector2(0f, stepY));
            LayoutRoot(_settingsVolumeControl,
                new Vector2(stepGap, stepY));
            LayoutRect(_settingsLensLabel,
                new Vector2(0f, stepY - 90f), new Vector2(370f, 22f));
            LayoutButton(_settingsCloseAllButton,
                new Vector2(halfWidth - 58f, stepY + 25f),
                Vector2.one * 58f);

            float bottom = -halfHeight + 10f;
            LayoutHandle(_settingsMoveHandle, new Vector2(-48f, bottom),
                new Vector2(104f, 5f));
            LayoutHandle(_settingsDepthHandle, new Vector2(82f, bottom),
                new Vector2(42f, 28f));
            LayoutHandle(_settingsResizeHandle,
                new Vector2(-halfWidth + 25f, -halfHeight + 25f),
                Vector2.one * 52f);
            LayoutHandle(_settingsResizeHandleRight,
                new Vector2(halfWidth - 25f, -halfHeight + 25f),
                Vector2.one * 52f);
            LayoutRect(_settingsCloseHandle,
                new Vector2(halfWidth - 17f, halfHeight - 17f),
                new Vector2(34f, 34f));
        }

        private static void LayoutRoot(RectTransform root, Vector2 position)
        {
            if (root == null) return;
            root.anchoredPosition = position;
        }

        private void LayoutSettingsDeckV15()
        {
            if (_settingsDeckRect == null) return;
            Vector2 size = _settingsDeckRect.sizeDelta;
            float halfWidth = size.x * .5f;
            float halfHeight = size.y * .5f;
            bool landscape = size.x >= size.y;
            float controlSize = landscape ? 78f : 70f;
            float gaugeSize = landscape ? 92f : 82f;
            float gaugeGap = landscape
                ? Mathf.Min(145f, (size.x - 230f) / 3f)
                : Mathf.Min(122f, (size.x - 180f) / 3f);
            float controlGap = landscape
                ? Mathf.Min(170f, (size.x - 160f) / 4f)
                : Mathf.Min(122f, (size.x - 90f) / 4f);

            LayoutRect(_settingsTitleLabel,
                new Vector2(0f, halfHeight - 28f), new Vector2(300f, 52f));
            float gaugeY = halfHeight - 120f;
            LayoutGauge(_settingsDevicePill,
                new Vector2(-gaugeGap, gaugeY), gaugeSize);
            LayoutGauge(_settingsLensPill, new Vector2(0f, gaugeY), gaugeSize);
            LayoutGauge(_settingsTrackingPill,
                new Vector2(gaugeGap, gaugeY), gaugeSize);

            float controlsY = halfHeight - (landscape ? 245f : 235f);
            LayoutButton(_settingsWindowModeButton,
                new Vector2(-1.5f * controlGap, controlsY),
                Vector2.one * controlSize);
            LayoutButton(_settingsGesturesButton,
                new Vector2(-.5f * controlGap, controlsY),
                Vector2.one * controlSize);
            LayoutButton(_settingsRayButton,
                new Vector2(.5f * controlGap, controlsY),
                Vector2.one * controlSize);
            LayoutButton(_settingsRecenterButton,
                new Vector2(1.5f * controlGap, controlsY),
                Vector2.one * controlSize);

            float lensY = controlsY - (landscape ? 132f : 125f);
            for (int i = 0; i < _settingsLensButtons.Length; i++)
                LayoutButton(
                    _settingsLensButtons[i],
                    new Vector2((i - 1.5f) * controlGap, lensY),
                    Vector2.one * controlSize);
            LayoutRect(_settingsLensLabel,
                new Vector2(0f, lensY - 72f), new Vector2(380f, 22f));

            float audioY = lensY - (landscape ? 150f : 145f);
            LayoutButton(_settingsVolumeDownButton,
                new Vector2(-1.45f * controlGap, audioY),
                Vector2.one * (controlSize - 8f));
            LayoutGauge(_settingsAudioPill,
                new Vector2(-.48f * controlGap, audioY),
                controlSize - 4f);
            LayoutButton(_settingsVolumeUpButton,
                new Vector2(.48f * controlGap, audioY),
                Vector2.one * (controlSize - 8f));
            LayoutButton(_settingsCloseAllButton,
                new Vector2(1.45f * controlGap, audioY),
                Vector2.one * (controlSize - 8f));

            float bottom = -halfHeight + 10f;
            LayoutHandle(_settingsMoveHandle, new Vector2(-48f, bottom),
                new Vector2(104f, 7f));
            LayoutHandle(_settingsDepthHandle, new Vector2(82f, bottom),
                Vector2.one * 34f);
            LayoutHandle(_settingsResizeHandle,
                new Vector2(-halfWidth + 23f, -halfHeight + 23f),
                Vector2.one * 48f);
            LayoutHandle(_settingsResizeHandleRight,
                new Vector2(halfWidth - 23f, -halfHeight + 23f),
                Vector2.one * 48f);
            LayoutRect(_settingsCloseHandle,
                new Vector2(halfWidth - 17f, halfHeight - 17f),
                new Vector2(34f, 34f));
        }

        private static void LayoutGauge(Image gauge, Vector2 position, float size)
        {
            if (gauge == null) return;
            gauge.rectTransform.anchoredPosition = position;
            // Preserve the authored circular proportions; only scale as a unit
            // so its ring, icon, value and fine caption stay aligned.
            float authored = Mathf.Max(1f, gauge.rectTransform.sizeDelta.x);
            gauge.rectTransform.localScale = Vector3.one * (size / authored);
        }

        private void LayoutSettingsDeckLegacy()
        {
            if (_settingsDeckRect == null) return;
            Vector2 size = _settingsDeckRect.sizeDelta;
            float halfWidth = size.x * .5f;
            float halfHeight = size.y * .5f;
            bool landscape = size.x >= size.y;

            if (landscape)
            {
                LayoutRect(_settingsTitleLabel, new Vector2(0f, halfHeight - 14f),
                    new Vector2(240f, 18f));
                LayoutStatusPill(_settingsDevicePill,
                    new Vector2(-260f, halfHeight - 49f), new Vector2(250f, 46f));
                LayoutStatusPill(_settingsTrackingPill,
                    new Vector2(0f, halfHeight - 49f), new Vector2(220f, 46f));
                LayoutStatusPill(_settingsLensPill,
                    new Vector2(260f, halfHeight - 49f), new Vector2(250f, 46f));

                float firstRow = halfHeight - 145f;
                float secondRow = halfHeight - 260f;
                float thirdRow = halfHeight - 386f;
                float actionSize = 92f;
                LayoutButton(_settingsWindowModeButton,
                    new Vector2(-270f, firstRow), Vector2.one * actionSize);
                LayoutButton(_settingsGesturesButton,
                    new Vector2(-90f, firstRow), Vector2.one * actionSize);
                LayoutButton(_settingsRayButton,
                    new Vector2(90f, firstRow), Vector2.one * actionSize);
                LayoutButton(_settingsRecenterButton,
                    new Vector2(270f, firstRow), Vector2.one * actionSize);
                LayoutButton(_settingsVolumeDownButton,
                    new Vector2(-150f, secondRow), Vector2.one * 86f);
                LayoutStatusPill(_settingsAudioPill,
                    new Vector2(0f, secondRow), new Vector2(130f, 48f));
                LayoutButton(_settingsVolumeUpButton,
                    new Vector2(150f, secondRow), Vector2.one * 86f);
                LayoutButton(_settingsCloseAllButton,
                    new Vector2(300f, secondRow), Vector2.one * 86f);
                for (int i = 0; i < _settingsLensButtons.Length; i++)
                    LayoutButton(
                        _settingsLensButtons[i],
                        new Vector2((i - 1.5f) * 160f, thirdRow),
                        Vector2.one * 88f);
            }
            else
            {
                LayoutRect(_settingsTitleLabel, new Vector2(0f, halfHeight - 14f),
                    new Vector2(240f, 18f));
                LayoutStatusPill(_settingsDevicePill,
                    new Vector2(-125f, halfHeight - 49f), new Vector2(235f, 44f));
                LayoutStatusPill(_settingsTrackingPill,
                    new Vector2(130f, halfHeight - 49f), new Vector2(205f, 44f));
                LayoutStatusPill(_settingsLensPill,
                    new Vector2(0f, halfHeight - 100f), new Vector2(250f, 42f));

                float rowOne = halfHeight - 190f;
                float rowTwo = halfHeight - 305f;
                float rowThree = halfHeight - 425f;
                float rowFour = halfHeight - 545f;
                LayoutButton(_settingsWindowModeButton,
                    new Vector2(-145f, rowOne), Vector2.one * 90f);
                LayoutButton(_settingsGesturesButton,
                    new Vector2(0f, rowOne), Vector2.one * 90f);
                LayoutButton(_settingsRayButton,
                    new Vector2(145f, rowOne), Vector2.one * 90f);
                LayoutButton(_settingsVolumeDownButton,
                    new Vector2(-145f, rowTwo), Vector2.one * 86f);
                LayoutStatusPill(_settingsAudioPill,
                    new Vector2(0f, rowTwo), new Vector2(120f, 46f));
                LayoutButton(_settingsVolumeUpButton,
                    new Vector2(145f, rowTwo), Vector2.one * 86f);
                LayoutButton(_settingsRecenterButton,
                    new Vector2(-75f, rowThree), Vector2.one * 88f);
                LayoutButton(_settingsCloseAllButton,
                    new Vector2(75f, rowThree), Vector2.one * 88f);
                for (int i = 0; i < _settingsLensButtons.Length; i++)
                    LayoutButton(
                        _settingsLensButtons[i],
                        new Vector2((i - 1.5f) * 120f, rowFour),
                        Vector2.one * 84f);
            }

            float bottom = -halfHeight + 10f;
            LayoutHandle(_settingsMoveHandle, new Vector2(-58f, bottom),
                new Vector2(118f, 7f));
            LayoutHandle(_settingsDepthHandle, new Vector2(58f, bottom),
                new Vector2(72f, 7f));
            LayoutHandle(_settingsResizeHandle,
                new Vector2(-halfWidth + 13f, -halfHeight + 13f),
                new Vector2(24f, 32f));
            LayoutHandle(_settingsResizeHandleRight,
                new Vector2(halfWidth - 13f, -halfHeight + 13f),
                new Vector2(24f, 32f));
            LayoutRect(_settingsCloseHandle,
                new Vector2(halfWidth - 17f, halfHeight - 17f),
                new Vector2(34f, 34f));
        }

        private static void LayoutRect(
            TMP_Text label,
            Vector2 position,
            Vector2 size)
        {
            if (label == null) return;
            RectTransform rect = label.rectTransform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void LayoutStatusPill(
            Image pill,
            Vector2 position,
            Vector2 size)
        {
            if (pill == null) return;
            RectTransform rect = pill.rectTransform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            TMP_Text[] labels = pill.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                RectTransform labelRect = labels[i].rectTransform;
                labelRect.sizeDelta = new Vector2(
                    Mathf.Max(40f, size.x - 16f),
                    labelRect.sizeDelta.y);
            }
        }

        private static void LayoutHandle(
            Image image,
            Vector2 position,
            Vector2 size)
        {
            if (image == null) return;
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
        }

        private static void LayoutButton(
            Button button,
            Vector2 position,
            Vector2 size)
        {
            if (button == null) return;
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            BoxCollider collider = button.GetComponent<BoxCollider>();
            if (collider != null)
                collider.size = new Vector3(size.x, size.y, 14f);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                if (label.gameObject.name == "Vision caption")
                {
                    label.rectTransform.anchoredPosition = new Vector2(
                        0f,
                        -size.y * .5f - 15f);
                    label.rectTransform.sizeDelta = new Vector2(
                        Mathf.Max(112f, size.x + 38f),
                        22f);
                }
                else
                    label.rectTransform.sizeDelta = size - new Vector2(12f, 8f);
            }

            const string facePrefix = "Button ";
            string suffix = button.gameObject.name.StartsWith(facePrefix,
                    StringComparison.Ordinal)
                ? button.gameObject.name.Substring(facePrefix.Length)
                : button.gameObject.name;
            Transform depth = button.transform.parent.Find(
                "Button depth " + suffix);
            if (depth == null) return;
            depth.localPosition = new Vector3(
                position.x,
                position.y,
                depth.localPosition.z);
            depth.localScale = new Vector3(
                size.x,
                size.y,
                depth.localScale.z);
        }

        private void ToggleSettingsDeck()
        {
            if (_settingsDeck == null) BuildSettingsDeck();
            if (_settingsDeck == null || _camera == null) return;
            bool show = !_settingsDeck.gameObject.activeSelf;
            if (show)
                OpenSettingsDeck(true);
            else
                CloseWindow(DeckWindowKind.Settings);
        }

        private void SetSettingsOrientation(bool landscape)
        {
            if (_settingsDeckRect == null) return;
            Vector2 target = landscape
                ? new Vector2(840f, 620f)
                : new Vector2(620f, 840f);
            target = AdjustOptionalLabSettingsOrientation(target);
            _settingsDeckRect.sizeDelta = target;
            _deckManipulationStartSize = target;
            _deckManipulationTargetSize = target;
            SaveSettingsSize(target);
            LayoutSettingsDeck();
            ShowGestureToast(
                landscape ? "FENETRE // PAYSAGE" : "FENETRE // PORTRAIT",
                new Color(.55f, .78f, 1f));
        }

        private void SetOrientationSelection(bool landscape)
        {
            SetControlCenterState(
                _settingsPortraitButton,
                !landscape,
                VisionPressed);
            SetControlCenterState(
                _settingsLandscapeButton,
                landscape,
                VisionPressed);
        }

        private void OpenSettingsDeck(bool recenter)
        {
            if (_settingsDeck == null) BuildSettingsDeck();
            if (_settingsDeck == null || _camera == null) return;
            _settingsDeck.gameObject.SetActive(true);
            _lastWindow = DeckWindowKind.Settings;
            if (recenter) ApplyPreferredSettingsPose();
            RefreshSettingsDeck();
            if (_autoJoinWindowBlock)
                JoinWindowBlock(DeckWindowKind.Settings, null, false);
        }

        private void ApplyPreferredSettingsPose()
        {
            if (_settingsDeckRect == null || _camera == null) return;
            float savedWidth = PlayerPrefs.GetFloat(
                SettingsLayoutPrefix + "width",
                840f);
            float savedHeight = PlayerPrefs.GetFloat(
                SettingsLayoutPrefix + "height",
                620f);
            bool landscape = savedWidth >= savedHeight;
            if (landscape)
            {
                savedWidth = Mathf.Clamp(savedWidth, 680f, 1120f);
                savedHeight = Mathf.Clamp(
                    savedHeight,
                    HasOptionalLabSettingsActions() ? 760f : 520f,
                    HasOptionalLabSettingsActions() ? 960f : 820f);
                savedWidth = Mathf.Max(savedWidth, savedHeight + 80f);
            }
            else
            {
                savedWidth = Mathf.Clamp(savedWidth, 500f, 760f);
                savedHeight = Mathf.Clamp(
                    savedHeight,
                    HasOptionalLabSettingsActions() ? 920f : 700f,
                    1120f);
                savedHeight = Mathf.Max(savedHeight, savedWidth + 80f);
            }
            _settingsDeckRect.sizeDelta = new Vector2(savedWidth, savedHeight);
            LayoutSettingsDeck();
            Vector3 local = PlayerPrefs.HasKey(SettingsLayoutPrefix + "x")
                ? new Vector3(
                    PlayerPrefs.GetFloat(SettingsLayoutPrefix + "x"),
                    PlayerPrefs.GetFloat(SettingsLayoutPrefix + "y"),
                    PlayerPrefs.GetFloat(SettingsLayoutPrefix + "z", .92f))
                : new Vector3(-.32f, .20f, .92f);
            Vector3 position = _camera.transform.TransformPoint(local);
            Vector3 forward = (position - _camera.transform.position).normalized;
            _settingsDeckRect.SetPositionAndRotation(
                position,
                BuildWindowRotation(
                    forward,
                    PlayerPrefs.GetFloat(SettingsLayoutPrefix + "tilt", 0f),
                    PlayerPrefs.GetFloat(SettingsLayoutPrefix + "turn", 0f)));
            float scale = PlayerPrefs.GetFloat(
                SettingsLayoutPrefix + "scale",
                .00062f);
            _settingsDeckRect.localScale = Vector3.one * Mathf.Clamp(
                scale,
                .00038f,
                .00108f);
        }

        private RectTransform RectForWindow(DeckWindowKind window) => window switch
        {
            DeckWindowKind.Workspace => _spatialDeckRect,
            DeckWindowKind.Settings => _settingsDeckRect,
            DeckWindowKind.External => _activeExternalWindow?.Rect,
            _ => null,
        };

        private void CloseWindow(DeckWindowKind window)
        {
            if (window == DeckWindowKind.Workspace)
            {
                _lastWindow = DeckWindowKind.Workspace;
                SetDeckMinimized(true);
                return;
            }
            if (window == DeckWindowKind.Settings && _settingsDeck != null)
            {
                if (
                    _deckManipulationMode != DeckManipulationMode.None &&
                    _activeWindow == DeckWindowKind.Settings)
                    EndDeckManipulation();
                _lastWindow = DeckWindowKind.Settings;
                _settingsDeck.gameObject.SetActive(false);
                SetDeckHandleVisuals(
                    DeckManipulationMode.None,
                    DeckWindowKind.None);
            }
            if (window == DeckWindowKind.External && _activeExternalWindow != null)
            {
                ExternalSpatialWindowState state = _activeExternalWindow;
                _lastWindow = DeckWindowKind.External;
                _lastExternalWindow = state;
                state.Close?.Invoke();
            }
        }

        private void ToggleGesturePower()
        {
            ResolveInteractionSettings();
            if (_interactionSettings == null) return;
            _interactionSettings.SetGestureStandby(
                !_interactionSettings.IsGestureStandby);
        }

        private void ToggleEyeRay()
        {
            ResolveInteractionSettings();
            if (_interactionSettings == null) return;
            _interactionSettings.ToggleRayVisible();
            _status = _interactionSettings.IsRayVisible
                ? "RAYON EYE ACTIF // CURSEUR ACTIF"
                : "RAYON EYE COUPÉ // CURSEUR ACTIF";
            RefreshSettingsDeck();
            RefreshSpatialDeck();
        }

        private void ToggleWindowMode()
        {
            if (_camera == null) return;
            SaveVisibleWindowLayouts();
            int current = _headFollowWindows ? 1 : (_manualFrozenWindows ? 2 : 0);
            int next = (current + 1) % 3;
            _headFollowWindows = next == 1;
            _manualFrozenWindows = next == 2;
            CancelSpatialTrackingFallback();
            if (_manualFrozenWindows) BeginSpatialTrackingFallback();
            SetExternalWindowChromeFrozen(_manualFrozenWindows);
            SetDeckHandleVisuals(
                DeckManipulationMode.None,
                DeckWindowKind.None);
            PlayerPrefs.SetInt(
                WindowModePreference,
                next);
            PlayerPrefs.Save();
            _status = _headFollowWindows
                ? "FENÊTRES // SUIVI TÊTE MANUEL"
                : (_manualFrozenWindows
                    ? "FENÊTRES // ANCRAGE FIGÉ MANUEL"
                    : "FENÊTRES // ANCRAGE 6DOF");
            RefreshSettingsDeck();
            ShowGestureToast(
                _headFollowWindows
                    ? "SUIVI TÊTE ACTIF"
                    : (_manualFrozenWindows
                        ? "ANCRAGE FIGÉ MANUEL"
                        : "ANCRAGE 6DOF ACTIF"),
                new Color(.35f, 1f, .94f));
        }

        private void SaveVisibleWindowLayouts()
        {
            if (!_deckMinimized && _spatialDeckRect != null)
                SaveWindowLayout(
                    DeckWindowKind.Workspace,
                    _spatialDeckRect.position,
                    _spatialDeckRect.localScale.x);
            if (_settingsDeck != null && _settingsDeck.gameObject.activeSelf)
            {
                SaveWindowLayout(
                    DeckWindowKind.Settings,
                    _settingsDeckRect.position,
                    _settingsDeckRect.localScale.x);
                SaveSettingsSize(_settingsDeckRect.sizeDelta);
            }
            SaveExternalWindowLayouts();
        }

        private void RecenterAllWindows()
        {
            if (_camera == null) return;
            bool workspaceVisible = !_deckMinimized && _spatialDeckRect != null;
            bool settingsVisible =
                _settingsDeck != null && _settingsDeck.gameObject.activeSelf;
            if (workspaceVisible)
                PlaceWindowAtCameraLocal(
                    _spatialDeckRect,
                    settingsVisible
                        ? new Vector3(-.24f, .04f, 1.12f)
                        : new Vector3(0f, .04f, 1.12f));
            if (settingsVisible)
                PlaceWindowAtCameraLocal(
                    _settingsDeckRect,
                    workspaceVisible
                        ? new Vector3(.34f, .09f, 1.02f)
                        : new Vector3(0f, .06f, .96f));
            if (_windowDock != null && _windowDock.gameObject.activeSelf)
                PlaceWindowAtCameraLocal(
                    _windowDockRect,
                    new Vector3(0f, 0f, .82f));
            RecenterExternalWindows();
            SaveVisibleWindowLayouts();
            ShowGestureToast(
                "FENÊTRES RECENTRÉES",
                new Color(.35f, 1f, .94f));
        }

        private void PlaceWindowAtCameraLocal(
            RectTransform window,
            Vector3 local)
        {
            if (window == null || _camera == null) return;
            Vector3 position = _camera.transform.TransformPoint(local);
            Vector3 forward = (position - _camera.transform.position).normalized;
            window.SetPositionAndRotation(
                position,
                BuildWindowRotation(forward, 0f, 0f));
            _deckManipulationSmoothing = false;
        }

        private void CloseAllWindows()
        {
            if (_deckManipulationMode != DeckManipulationMode.None)
                EndDeckManipulation();
            SetDeckMinimized(true);
            if (_settingsDeck != null)
                _settingsDeck.gameObject.SetActive(false);
            if (_windowDock != null)
                _windowDock.gameObject.SetActive(false);
            CloseAllExternalWindows();
            SetDeckHandleVisuals(
                DeckManipulationMode.None,
                DeckWindowKind.None);
            ShowGestureToast(
                "UI FERMÉE // PAUME POUR RAPPELER",
                new Color(.35f, 1f, .94f));
        }

        private void AdjustMediaVolume(int direction)
        {
            TryAdjustAndroidMediaVolume(direction);
            _nextSettingsTelemetryAt = 0f;
            UpdateSettingsTelemetry();
        }

        private void ProbeLensControl()
        {
            _lensControlState = XrealPrivateLensControl.Probe();
            Debug.Log("[XrealLensControl] probe=" + _lensControlState);
            if (_settingsLensLabel != null)
                _settingsLensLabel.text =
                    CompactLensStatus(_lensControlState);
            UpdateLensProgressRings();
        }

        private static string CompactLensStatus(string state) =>
            XrealPrivateLensControl.HumanStatus(state).Replace(
                "LENTILLES // ",
                string.Empty);

        private void UpdateLensProgressRings()
        {
            int brightness = ReadLensMetric(_lensControlState, "b");
            int brightnessCount = ReadLensMetric(_lensControlState, "bc");
            int ec = ReadLensMetric(_lensControlState, "ec");
            int ecCount = ReadLensMetric(_lensControlState, "ecc");
            if (_settingsBrightnessRing != null)
                _settingsBrightnessRing.fillAmount = brightness < 0
                    ? 0f
                    : Mathf.Clamp01((brightness + 1f) /
                        Mathf.Max(1f, brightnessCount));
            if (_settingsEcRing != null)
                _settingsEcRing.fillAmount = ec < 0
                    ? 0f
                    : Mathf.Clamp01((ec + 1f) / Mathf.Max(1f, ecCount));
        }

        private static int ReadLensMetric(string state, string key)
        {
            if (string.IsNullOrEmpty(state)) return -1;
            string prefix = key + "=";
            string[] parts = state.Split('|');
            for (int i = 0; i < parts.Length; i++)
                if (
                    parts[i].StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(parts[i].Substring(prefix.Length), out int value))
                    return value;
            return -1;
        }

        private void AdjustLensControl(bool electrochromic, int direction)
        {
            // The first press rewrites current values only. A second press is
            // required before any visible lens state can change. Validation
            // also performs the private service's minimal official startup.
            if (!_lensControlValidated)
            {
                _lensControlState = XrealPrivateLensControl.ValidateCurrent();
                Debug.Log("[XrealLensControl] validate=" + _lensControlState);
                _lensControlValidated = _lensControlState.StartsWith(
                    "VALID|",
                    StringComparison.Ordinal);
                if (_settingsLensLabel != null)
                    _settingsLensLabel.text =
                        CompactLensStatus(_lensControlState);
                UpdateLensProgressRings();
                ShowGestureToast(
                    _lensControlValidated
                        ? "LENTILLES PRETES // REPETER POUR CHANGER"
                        : "TEST LENTILLES REFUSE",
                    _lensControlValidated
                        ? new Color(.35f, 1f, .72f)
                        : new Color(1f, .55f, .3f));
                return;
            }

            _lensControlState = electrochromic
                ? XrealPrivateLensControl.StepEc(direction)
                : XrealPrivateLensControl.StepBrightness(direction);
            Debug.Log("[XrealLensControl] change=" + _lensControlState);
            if (_settingsLensLabel != null)
                _settingsLensLabel.text =
                    CompactLensStatus(_lensControlState);
            UpdateLensProgressRings();
            bool succeeded =
                XrealPrivateLensControl.IsSuccess(_lensControlState);
            ShowGestureToast(
                succeeded
                    ? (electrochromic
                        ? "ASSOMBRISSEMENT XREAL MODIFIE"
                        : "LUMINOSITE XREAL MODIFIEE")
                    : "COMMANDE LENTILLES REFUSEE",
                succeeded
                    ? new Color(.35f, 1f, .94f)
                    : new Color(1f, .55f, .3f));
        }

        private static void TryAdjustAndroidMediaVolume(int direction)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unity = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unity.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject audio =
                    activity.Call<AndroidJavaObject>(
                        "getSystemService",
                        "audio");
                const int streamMusic = 3;
                const int flags = 0;
                audio.Call(
                    "adjustStreamVolume",
                    streamMusic,
                    direction > 0 ? 1 : -1,
                    flags);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[WorldCreator] Android media volume unavailable: " +
                    exception.Message);
            }
#else
            AudioListener.volume = Mathf.Clamp01(
                AudioListener.volume + direction * .1f);
#endif
        }

        private static int ReadMediaVolumePercent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unity = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unity.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject audio =
                    activity.Call<AndroidJavaObject>(
                        "getSystemService",
                        "audio");
                const int streamMusic = 3;
                int current = audio.Call<int>("getStreamVolume", streamMusic);
                int maximum = audio.Call<int>("getStreamMaxVolume", streamMusic);
                return maximum <= 0
                    ? 0
                    : Mathf.RoundToInt(current * 100f / maximum);
            }
            catch
            {
                return -1;
            }
#else
            return Mathf.RoundToInt(AudioListener.volume * 100f);
#endif
        }

        private void UpdateSettingsTelemetry()
        {
            if (
                _settingsDeck == null ||
                !_settingsDeck.gameObject.activeSelf ||
                Time.unscaledTime < _nextSettingsTelemetryAt)
                return;
            _nextSettingsTelemetryAt = Time.unscaledTime + 1f;
            ResolveInteractionSettings();

            if (_settingsTitleLabel != null)
                _settingsTitleLabel.text = DateTime.Now.ToString("HH:mm");

            float batteryLevel = SystemInfo.batteryLevel;
            int batteryPercent = batteryLevel < 0f
                ? -1
                : Mathf.RoundToInt(batteryLevel * 100f);
            if (_settingsDeviceLabel != null)
                _settingsDeviceLabel.text = batteryPercent < 0
                    ? "--"
                    : batteryPercent + "%";
            if (_settingsBatteryRing != null)
            {
                _settingsBatteryRing.fillAmount = batteryLevel < 0f
                    ? 0f
                    : Mathf.Clamp01(batteryLevel);
                _settingsBatteryRing.color = batteryLevel >= 0f && batteryLevel < .2f
                    ? new Color(1f, .42f, .32f, .96f)
                    : new Color(.55f, .86f, 1f, .96f);
            }

            string temperature =
                _interactionSettings?.GlassesTemperatureStatus ??
                "XREAL // TEMP --";
            string foldedTemperature = temperature.ToUpperInvariant();
            bool hot = foldedTemperature.Contains("ELEV") ||
                foldedTemperature.Contains("HOT") ||
                foldedTemperature.Contains("CHAUD");
            bool warm = foldedTemperature.Contains("TIEDE") ||
                foldedTemperature.Contains("WARM");
            bool unknown = foldedTemperature.EndsWith("--");
            Color temperatureColor = hot
                ? new Color(1f, .36f, .30f, .98f)
                : (warm
                    ? new Color(1f, .67f, .22f, .98f)
                    : new Color(.35f, 1f, .72f, .96f));
            if (_settingsTemperatureLabel != null)
            {
                _settingsTemperatureLabel.text = unknown
                    ? "--"
                    : (hot ? "CHAUD" : (warm ? "TIEDE" : "OK"));
                _settingsTemperatureLabel.color = temperatureColor;
            }
            if (_settingsTemperatureRing != null)
            {
                _settingsTemperatureRing.fillAmount = unknown ? 0f : 1f;
                _settingsTemperatureRing.color = temperatureColor;
            }

            string tracking = _interactionSettings?.TrackingStatus ??
                "TRACKING // INDISPONIBLE";
            bool trackingOk = tracking.EndsWith("OK", StringComparison.Ordinal);
            if (_settingsTrackingLabel != null)
            {
                _settingsTrackingLabel.text = trackingOk ? "OK" : "BAD";
                _settingsTrackingLabel.color = trackingOk
                    ? new Color(.35f, 1f, .72f)
                    : new Color(1f, .72f, .24f);
            }
            if (_settingsTrackingRing != null)
            {
                _settingsTrackingRing.fillAmount = 1f;
                _settingsTrackingRing.color = trackingOk
                    ? new Color(.35f, 1f, .72f, .96f)
                    : new Color(1f, .67f, .22f, .96f);
            }

            int volume = ReadMediaVolumePercent();
            if (_settingsAudioLabel != null)
                _settingsAudioLabel.text = volume < 0 ? "--" : volume + "%";
            if (_settingsAudioRing != null)
                _settingsAudioRing.fillAmount = volume < 0
                    ? 0f
                    : Mathf.Clamp01(volume / 100f);
            if (_settingsVolumeControlRing != null)
                _settingsVolumeControlRing.fillAmount = volume < 0
                    ? 0f
                    : Mathf.Clamp01(volume / 100f);
        }

        private void UpdateSettingsTelemetryLegacy()
        {
            if (
                _settingsDeck == null ||
                !_settingsDeck.gameObject.activeSelf ||
                Time.unscaledTime < _nextSettingsTelemetryAt)
                return;
            _nextSettingsTelemetryAt = Time.unscaledTime + 1f;
            ResolveInteractionSettings();
            float batteryLevel = SystemInfo.batteryLevel;
            string battery = batteryLevel < 0f
                ? "--"
                : Mathf.RoundToInt(batteryLevel * 100f).ToString();
            string temperature = _interactionSettings?.GlassesTemperatureStatus ??
                "XREAL // TEMP --";
            if (_settingsDeviceLabel != null)
                _settingsDeviceLabel.text =
                    DateTime.Now.ToString("HH:mm") +
                    "  •  " + battery + "%  •  " +
                    temperature.Replace("XREAL // ", string.Empty);
            if (_settingsBatteryRing != null)
                _settingsBatteryRing.fillAmount = batteryLevel < 0f
                    ? 0f
                    : Mathf.Clamp01(batteryLevel);
            if (_settingsDevicePill != null)
                _settingsDevicePill.color =
                    temperature.Contains("ÉLEVÉE", StringComparison.Ordinal) ||
                    temperature.Contains("TIÈDE", StringComparison.Ordinal)
                        ? new Color(.42f, .25f, .08f, .78f)
                        : new Color(.18f, .19f, .22f, .64f);
            int volume = ReadMediaVolumePercent();
            if (_settingsAudioLabel != null)
                _settingsAudioLabel.text = volume < 0
                    ? "--%"
                    : volume + "%";
            if (_settingsAudioRing != null)
                _settingsAudioRing.fillAmount = volume < 0
                    ? 0f
                    : Mathf.Clamp01(volume / 100f);
            if (_settingsTrackingLabel != null)
            {
                string tracking = _interactionSettings?.TrackingStatus ??
                    "TRACKING // INDISPONIBLE";
                bool trackingOk = tracking.EndsWith("OK");
                _settingsTrackingLabel.text = tracking.Replace(
                    "TRACKING // ",
                    string.Empty);
                _settingsTrackingLabel.color = tracking.EndsWith("OK")
                    ? new Color(.35f, 1f, .72f)
                    : new Color(1f, .72f, .24f);
                if (_settingsTrackingPill != null)
                    _settingsTrackingPill.color = trackingOk
                        ? new Color(.08f, .32f, .22f, .72f)
                        : new Color(.42f, .25f, .08f, .78f);
            }
        }

        private void RefreshSettingsDeck()
        {
            ResolveInteractionSettings();
            bool standby =
                _interactionSettings != null &&
                _interactionSettings.IsGestureStandby;
            bool rayVisible =
                _interactionSettings != null &&
                _interactionSettings.IsRayVisible;
            if (_settingsGestureLabel != null)
                _settingsGestureLabel.text = standby ? "Gestes eco" : "Gestes";
            if (_settingsRayLabel != null)
                _settingsRayLabel.text = "Curseur";
            if (_settingsWindowModeLabel != null)
                _settingsWindowModeLabel.text = _headFollowWindows
                    ? "Suivi tete"
                    : (_manualFrozenWindows ? "Ancrage fige" : "Ancrage");
            SetControlCenterState(
                _settingsGesturesButton,
                true,
                standby
                    ? new Color(1f, .65f, .22f, .96f)
                    : VisionPressed);
            SetControlCenterState(
                _settingsRayButton,
                rayVisible,
                VisionPressed);
            SetControlCenterState(
                _settingsWindowModeButton,
                true,
                _headFollowWindows
                    ? new Color(1f, .65f, .22f, .96f)
                    : (_manualFrozenWindows
                        ? new Color(.55f, .75f, 1f, .96f)
                        : VisionPressed));
            _nextSettingsTelemetryAt = 0f;
            UpdateSettingsTelemetry();
            RefreshOptionalLabSettingsActions();
        }

        private void RefreshSettingsDeckLegacy()
        {
            ResolveInteractionSettings();
            bool standby =
                _interactionSettings != null &&
                _interactionSettings.IsGestureStandby;
            bool rayVisible =
                _interactionSettings != null &&
                _interactionSettings.IsRayVisible;
            if (_settingsGestureLabel != null)
                _settingsGestureLabel.text = standby
                    ? "✋\n<size=38%>BASSE 4 FPS</size>"
                    : "✋\n<size=38%>GESTES 25 FPS</size>";
            if (_settingsRayLabel != null)
                _settingsRayLabel.text = rayVisible
                    ? "◉\n<size=38%>RAYON ACTIF</size>"
                    : "◉\n<size=38%>RAYON COUPÉ</size>";
            if (_settingsWindowModeLabel != null)
                _settingsWindowModeLabel.text = _headFollowWindows
                    ? "⌖\n<size=38%>SUIVI TÊTE</size>"
                    : (_manualFrozenWindows
                        ? "⌖\n<size=38%>ANCRAGE FIGÉ</size>"
                        : "⌖\n<size=38%>ANCRAGE 6DOF</size>");
            SetControlCenterState(
                _settingsGesturesButton,
                true,
                standby
                    ? new Color(1f, .65f, .22f, .96f)
                    : VisionPressed);
            SetControlCenterState(
                _settingsRayButton,
                rayVisible,
                VisionPressed);
            SetControlCenterState(
                _settingsWindowModeButton,
                true,
                _headFollowWindows
                    ? new Color(1f, .65f, .22f, .96f)
                    : (_manualFrozenWindows
                        ? new Color(.55f, .75f, 1f, .96f)
                        : VisionPressed));
            _nextSettingsTelemetryAt = 0f;
            UpdateSettingsTelemetry();
        }

        private void ResolveInteractionSettings()
        {
            if (_interactionSettings != null) return;
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(
                FindObjectsSortMode.None))
            {
                if (behaviour is IWorldCreatorInteractionSettings settings)
                {
                    _interactionSettings = settings;
                    return;
                }
            }
        }

        private void BuildWindowDock()
        {
            if (_windowDock != null || _camera == null) return;
            var go = new GameObject("Atelier Vision Dock");
            _windowDock = go.AddComponent<Canvas>();
            _windowDock.renderMode = RenderMode.WorldSpace;
            _windowDock.worldCamera = _camera;
            _windowDock.sortingOrder = 130;
            _windowDockGroup = go.AddComponent<CanvasGroup>();
            go.AddComponent<GraphicRaycaster>();
            _windowDockRect = go.GetComponent<RectTransform>();
            _windowDockRect.sizeDelta = new Vector2(320f, 170f);
            _windowDockRect.localScale = Vector3.one * .00072f;
            if (!_osOnlyMode)
            {
                MakeDockOrbButton(
                    _windowDockRect,
                    VisionIconKind.Workspace,
                    "Pupitre",
                    new Vector2(-76f, 15f),
                    () => OpenWindowFromDock(DeckWindowKind.Workspace));
            }
            MakeDockOrbButton(
                _windowDockRect,
                VisionIconKind.Settings,
                "Reglages",
                _osOnlyMode ? Vector2.zero : new Vector2(76f, 15f),
                () => OpenWindowFromDock(DeckWindowKind.Settings));
            RefreshWindowDockHitTargets();
            go.SetActive(false);
        }

        /// <summary>
        /// Lab adds application orbs and its depth bar after the base dock is
        /// constructed. Refresh the world-space target list so Eye/hand input
        /// resolves those runtime controls without S24 screen coordinates.
        /// </summary>
        public void RefreshWindowDockHitTargets()
        {
            _windowDockHitGraphics.Clear();
            if (_windowDockRect != null)
                _windowDockRect.GetComponentsInChildren(
                    true,
                    _windowDockHitGraphics);
        }

        public void OpenWindowDockFromTwoPalms()
        {
            if (_windowDock == null) BuildWindowDock();
            if (_windowDock == null || _camera == null) return;
            Vector3 forward = _camera.transform.forward.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .96f
                ? _camera.transform.up
                : Vector3.up;
            _windowDockRect.SetPositionAndRotation(
                _camera.transform.position + forward * WindowDockDepth,
                Quaternion.LookRotation(forward, up));
            _windowDock.gameObject.SetActive(true);
            _windowDockShownAt = Time.unscaledTime;
            _windowDockRect.localScale = Vector3.one * .00072f;
            if (_windowDockGroup != null) _windowDockGroup.alpha = 0f;
        }

        private void UpdateWindowDockAnimation()
        {
            if (
                _windowDock == null ||
                !_windowDock.gameObject.activeSelf ||
                _windowDockRect == null ||
                _windowDockShownAt < 0f)
                return;
            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01((Time.unscaledTime - _windowDockShownAt) / .18f));
            _windowDockRect.localScale = Vector3.one *
                Mathf.Lerp(.00072f, .00078f, progress);
            if (_windowDockGroup != null) _windowDockGroup.alpha = progress;
            if (progress >= 1f) _windowDockShownAt = -1f;
        }

        private void OpenWindowFromDock(DeckWindowKind window)
        {
            if (_windowDock != null) _windowDock.gameObject.SetActive(false);
            if (window == DeckWindowKind.Settings)
            {
                OpenSettingsDeck(true);
                return;
            }
            if (_osOnlyMode)
            {
                OpenWindowDockFromTwoPalms();
                return;
            }
            _lastWindow = DeckWindowKind.Workspace;
            if (_deckMinimized) SetDeckMinimized(false);
            SetDeckPose();
            RefreshSpatialDeck();
            if (_autoJoinWindowBlock)
                JoinWindowBlock(DeckWindowKind.Workspace, null, false);
        }

        private static Button MakeDockOrbButton(
            Transform parent,
            VisionIconKind icon,
            string label,
            Vector2 position,
            UnityEngine.Events.UnityAction action)
        {
            bool workspace = icon == VisionIconKind.Workspace;
            Image rim = MakeImage(
                parent,
                "Dock rim " + label,
                position,
                new Vector2(112f, 112f),
                Color.clear);
            rim.sprite = GetVisionCircleSprite();
            rim.type = Image.Type.Simple;
            rim.raycastTarget = false;
            Image hit = MakeImage(
                parent,
                "Dock orb " + label,
                position,
                new Vector2(98f, 98f),
                workspace ? Color.white : Color.clear);
            hit.sprite = GetVisionCircleSprite();
            hit.type = Image.Type.Simple;
            Button button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var collider = hit.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(112f, 112f, 18f);
            button.onClick.AddListener(action);
            BuildVisionIcon(hit.transform, icon, Vector2.zero, 1.25f);
            if (workspace)
            {
                Graphic[] iconGraphics = hit.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < iconGraphics.Length; i++)
                    if (iconGraphics[i] != hit)
                        iconGraphics[i].color = new Color(.035f, .04f, .05f, 1f);
            }
            MakeText(
                parent,
                label,
                position + new Vector2(0f, -68f),
                new Vector2(124f, 24f),
                14f,
                VisionSecondary,
                FontStyles.Bold);
            var feedback = hit.gameObject.AddComponent<
                VisionSpatialControlFeedback>();
            feedback.Configure(
                hit,
                workspace ? Color.white : Color.clear,
                workspace
                    ? new Color(.86f, .89f, .94f, 1f)
                    : new Color(.34f, .36f, .42f, .24f),
                VisionPressed,
                workspace ? VisionInk : VisionText);
            return button;
        }

        private void BuildGestureToast()
        {
            if (_gestureToastCanvas != null || _camera == null) return;
            var go = new GameObject("Atelier Gesture Status Toast");
            _gestureToastCanvas = go.AddComponent<Canvas>();
            _gestureToastCanvas.renderMode = RenderMode.WorldSpace;
            _gestureToastCanvas.worldCamera = _camera;
            _gestureToastCanvas.sortingOrder = 120;
            _gestureToastGroup = go.AddComponent<CanvasGroup>();
            _gestureToastGroup.interactable = false;
            _gestureToastGroup.blocksRaycasts = false;
            _gestureToastRect = go.GetComponent<RectTransform>();
            _gestureToastRect.sizeDelta = new Vector2(360f, 44f);
            _gestureToastRect.localScale = Vector3.one * .00066f;
            Image rim = MakeImage(
                _gestureToastRect,
                "Vision notification fine rim",
                Vector2.zero,
                new Vector2(364f, 48f),
                new Color(.88f, .91f, .98f, .10f));
            rim.raycastTarget = false;
            _gestureToastPanel = MakeImage(
                _gestureToastRect,
                "Vision notification glass",
                Vector2.zero,
                _gestureToastRect.sizeDelta,
                new Color(.025f, .030f, .040f, .74f));
            _gestureToastPanel.raycastTarget = false;
            _gestureToastLabel = MakeText(
                _gestureToastPanel.transform,
                string.Empty,
                Vector2.zero,
                new Vector2(334f, 36f),
                13.5f,
                VisionText,
                FontStyles.Bold);
            _gestureToastLabel.alignment = TextAlignmentOptions.Center;
            _gestureToastGroup.alpha = 0f;
            go.SetActive(false);
        }

        private void ShowGestureToast(string text, Color color)
        {
            if (_gestureToastCanvas == null) BuildGestureToast();
            if (
                _gestureToastCanvas == null ||
                _gestureToastLabel == null ||
                _camera == null)
                return;
            Vector3 forward = _camera.transform.forward.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .96f
                ? _camera.transform.up
                : Vector3.up;
            _gestureToastRect.SetPositionAndRotation(
                _camera.transform.position +
                forward * .88f +
                _camera.transform.up * .04f,
                Quaternion.LookRotation(forward, up));
            _gestureToastLabel.text = text;
            _gestureToastLabel.color = Color.Lerp(VisionText, color, .28f);
            _gestureToastCanvas.gameObject.SetActive(true);
            _gestureToastShownAt = Time.unscaledTime;
            _gestureToastHideAt = Time.unscaledTime + 1.65f;
            _gestureToastRect.localScale = Vector3.one * .00062f;
            if (_gestureToastGroup != null) _gestureToastGroup.alpha = 0f;
        }

        private void UpdateGestureToast()
        {
            if (
                _gestureToastCanvas == null ||
                !_gestureToastCanvas.gameObject.activeSelf ||
                _gestureToastHideAt < 0f)
                return;
            float now = Time.unscaledTime;
            float intro = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01((now - _gestureToastShownAt) / .16f));
            float outro = Mathf.Clamp01((_gestureToastHideAt - now) / .24f);
            if (_gestureToastGroup != null)
                _gestureToastGroup.alpha = Mathf.Min(intro, outro);
            if (_gestureToastRect != null)
                _gestureToastRect.localScale = Vector3.one *
                    Mathf.Lerp(.00062f, .00066f, intro);
            if (now < _gestureToastHideAt) return;
            _gestureToastCanvas.gameObject.SetActive(false);
            _gestureToastShownAt = -1f;
            _gestureToastHideAt = -1f;
        }

        private static Image MakeImage(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.sprite = GetVisionRoundedSprite();
            image.type = Image.Type.Sliced;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return image;
        }

        private static Image MakeStatusPill(
            Transform parent,
            string title,
            string value,
            Vector2 position,
            Vector2 size,
            out TextMeshProUGUI valueLabel)
        {
            Image pill = MakeImage(
                parent,
                "Vision status " + title,
                position,
                size,
                new Color(.18f, .19f, .22f, .64f));
            pill.raycastTarget = false;
            TextMeshProUGUI titleLabel = MakeText(
                pill.transform,
                title,
                new Vector2(0f, 10f),
                new Vector2(size.x - 16f, 16f),
                9f,
                new Color(.72f, .74f, .80f, .92f),
                FontStyles.Bold);
            titleLabel.characterSpacing = 1.2f;
            valueLabel = MakeText(
                pill.transform,
                value,
                new Vector2(0f, -8f),
                new Vector2(size.x - 16f, 22f),
                12f,
                VisionText,
                FontStyles.Bold);
            valueLabel.enableWordWrapping = false;
            return pill;
        }

        private static void ConfigureControlCenterButton(
            Button button,
            string icon,
            string caption)
        {
            if (button == null) return;
            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = GetVisionCircleSprite();
                image.type = Image.Type.Simple;
            }
            TextMeshProUGUI label =
                button.GetComponentInChildren<TextMeshProUGUI>();
            if (label == null) return;
            label.text = icon + "\n<size=38%>" + caption + "</size>";
            label.fontSize = 29f;
            label.fontStyle = FontStyles.Normal;
            label.enableWordWrapping = false;
            label.lineSpacing = -12f;
        }

        private static void ConfigureControlCenterButton(
            Button button,
            VisionIconKind icon,
            string caption)
        {
            if (button == null) return;
            Image surface = button.GetComponent<Image>();
            if (surface != null)
            {
                surface.sprite = GetVisionCircleSprite();
                surface.type = Image.Type.Simple;
                surface.color = new Color(.16f, .17f, .20f, .68f);
            }

            // MakeButton also authors a rectangular depth plate.  It is useful
            // on the workshop deck, but behind a circular Control Center icon
            // it reads as an ugly square.  Vision controls are intentionally
            // borderless orbs, so remove only their matching plate.
            const string buttonPrefix = "Button ";
            string suffix = button.gameObject.name.StartsWith(
                    buttonPrefix, StringComparison.Ordinal)
                ? button.gameObject.name.Substring(buttonPrefix.Length)
                : button.gameObject.name;
            Transform depth = button.transform.parent?.Find(
                "Button depth " + suffix);
            if (depth != null) depth.gameObject.SetActive(false);

            TextMeshProUGUI label =
                button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.gameObject.name = "Vision caption";
                label.text = caption;
                label.fontSize = 11f;
                label.fontStyle = FontStyles.Normal;
                label.enableWordWrapping = false;
                label.characterSpacing = .35f;
                label.color = VisionSecondary;
                label.rectTransform.anchoredPosition = new Vector2(
                    0f,
                    -button.GetComponent<RectTransform>().sizeDelta.y * .5f -
                    15f);
                label.rectTransform.sizeDelta = new Vector2(124f, 22f);
            }
            BuildVisionIcon(button.transform, icon, Vector2.zero, 1f);
            VisionSpatialControlFeedback feedback =
                button.GetComponent<VisionSpatialControlFeedback>();
            if (feedback != null)
                feedback.Configure(
                    surface,
                    new Color(.16f, .17f, .20f, .68f),
                    new Color(.38f, .40f, .44f, .92f),
                    VisionPressed,
                    VisionText);
        }

        private static void BuildVisionIcon(
            Transform parent,
            VisionIconKind kind,
            Vector2 offset,
            float scale)
        {
            Color color = VisionText;
            void Line(float x, float y, float w, float h, float rotation = 0f)
            {
                Image line = MakeImage(
                    parent, "Vision icon line", offset + new Vector2(x, y) * scale,
                    new Vector2(w, h) * scale, color);
                line.raycastTarget = false;
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            }
            void Dot(float x, float y, float diameter, bool ring = false)
            {
                Image dot = MakeImage(
                    parent, "Vision icon dot", offset + new Vector2(x, y) * scale,
                    Vector2.one * diameter * scale, color);
                dot.sprite = ring ? GetVisionRingSprite() : GetVisionCircleSprite();
                dot.type = Image.Type.Simple;
                dot.raycastTarget = false;
            }
            void Sign(bool plus, float x = 13f, float y = -11f)
            {
                Line(x, y, 12f, 2.5f);
                if (plus) Line(x, y, 2.5f, 12f);
            }
            void Sun()
            {
                Dot(0f, 2f, 15f, true);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * 45f;
                    float radians = angle * Mathf.Deg2Rad;
                    Line(
                        Mathf.Cos(radians) * 14f,
                        2f + Mathf.Sin(radians) * 14f,
                        8f,
                        2.2f,
                        angle);
                }
            }

            switch (kind)
            {
                case VisionIconKind.Phone:
                    Line(-10f, 0f, 3f, 30f);
                    Line(10f, 0f, 3f, 30f);
                    Line(0f, 14f, 20f, 3f);
                    Line(0f, -14f, 20f, 3f);
                    Dot(0f, -9f, 3.5f);
                    break;
                case VisionIconKind.Temperature:
                    Line(-3f, 4f, 6f, 24f);
                    Dot(-3f, -10f, 13f);
                    Line(7f, 8f, 8f, 2f);
                    Line(7f, 1f, 6f, 2f);
                    break;
                case VisionIconKind.Tracking:
                    Dot(0f, 1f, 29f, true);
                    Dot(0f, 1f, 8f);
                    Line(-19f, 1f, 8f, 2f);
                    Line(19f, 1f, 8f, 2f);
                    break;
                case VisionIconKind.Audio:
                    Image speaker = MakeImage(
                        parent, "Vision icon speaker", offset,
                        new Vector2(40f, 34f) * scale, color);
                    speaker.sprite = GetVisionSpeakerSprite();
                    speaker.type = Image.Type.Simple;
                    speaker.raycastTarget = false;
                    break;
                case VisionIconKind.Glasses:
                    Image leftLens = MakeImage(
                        parent, "Vision icon glasses left",
                        offset + new Vector2(-10f, 0f) * scale,
                        new Vector2(22f, 16f) * scale, color);
                    leftLens.sprite = GetVisionRingSprite();
                    leftLens.type = Image.Type.Simple;
                    leftLens.raycastTarget = false;
                    Image rightLens = MakeImage(
                        parent, "Vision icon glasses right",
                        offset + new Vector2(10f, 0f) * scale,
                        new Vector2(22f, 16f) * scale, color);
                    rightLens.sprite = GetVisionRingSprite();
                    rightLens.type = Image.Type.Simple;
                    rightLens.raycastTarget = false;
                    Line(0f, 1f, 7f, 2.5f);
                    Line(-21f, 4f, 8f, 2.5f, 12f);
                    Line(21f, 4f, 8f, 2.5f, -12f);
                    break;
                case VisionIconKind.Depth:
                    Line(-7f, 5f, 19f, 2.5f);
                    Line(-16f, -4f, 2.5f, 19f);
                    Line(-7f, -13f, 19f, 2.5f);
                    Line(2f, -4f, 2.5f, 19f);
                    Line(7f, 13f, 17f, 2.5f);
                    Line(15f, 5f, 2.5f, 17f);
                    Line(10f, -3f, 12f, 2.5f);
                    break;
                case VisionIconKind.Window:
                case VisionIconKind.Workspace:
                    Line(-15f, 0f, 3f, 27f);
                    Line(15f, 0f, 3f, 27f);
                    Line(0f, 13f, 30f, 3f);
                    Line(0f, -13f, 30f, 3f);
                    Line(-7f, 7f, 10f, 2.5f);
                    break;
                case VisionIconKind.Hand:
                    Line(0f, -5f, 21f, 19f);
                    Line(-8f, 8f, 4f, 16f);
                    Line(-2f, 11f, 4f, 21f);
                    Line(4f, 10f, 4f, 19f);
                    Line(10f, 7f, 4f, 14f);
                    break;
                case VisionIconKind.Eye:
                    Image eye = MakeImage(
                        parent, "Vision icon eye", offset,
                        new Vector2(37f, 23f) * scale, color);
                    eye.sprite = GetVisionRingSprite();
                    eye.type = Image.Type.Simple;
                    eye.raycastTarget = false;
                    Dot(0f, 0f, 9f);
                    break;
                case VisionIconKind.VolumeMinus:
                case VisionIconKind.VolumePlus:
                    Line(-12f, 0f, 8f, 13f);
                    Line(-5f, 0f, 4f, 24f);
                    Line(2f, 5f, 9f, 2.5f, 35f);
                    Line(2f, -5f, 9f, 2.5f, -35f);
                    Sign(kind == VisionIconKind.VolumePlus);
                    break;
                case VisionIconKind.Recenter:
                    Line(-12f, 11f, 10f, 2.5f);
                    Line(-16f, 7f, 2.5f, 10f);
                    Line(12f, 11f, 10f, 2.5f);
                    Line(16f, 7f, 2.5f, 10f);
                    Line(-12f, -11f, 10f, 2.5f);
                    Line(-16f, -7f, 2.5f, 10f);
                    Line(12f, -11f, 10f, 2.5f);
                    Line(16f, -7f, 2.5f, 10f);
                    Dot(0f, 0f, 6f);
                    break;
                case VisionIconKind.Close:
                    Line(0f, 0f, 30f, 3f, 45f);
                    Line(0f, 0f, 30f, 3f, -45f);
                    break;
                case VisionIconKind.Brightness:
                    Sun();
                    break;
                case VisionIconKind.BrightnessMinus:
                case VisionIconKind.BrightnessPlus:
                    Sun();
                    Sign(kind == VisionIconKind.BrightnessPlus);
                    break;
                case VisionIconKind.ElectrochromicMinus:
                case VisionIconKind.ElectrochromicPlus:
                    Dot(-2f, 2f, 29f, true);
                    Line(-8f, 2f, 11f, 25f);
                    Sign(kind == VisionIconKind.ElectrochromicPlus);
                    break;
                case VisionIconKind.Settings:
                    Line(0f, 10f, 30f, 2.5f);
                    Line(0f, 0f, 30f, 2.5f);
                    Line(0f, -10f, 30f, 2.5f);
                    Dot(-8f, 10f, 7f);
                    Dot(8f, 0f, 7f);
                    Dot(-3f, -10f, 7f);
                    break;
                case VisionIconKind.Portrait:
                    Line(-9f, 0f, 2.5f, 27f);
                    Line(9f, 0f, 2.5f, 27f);
                    Line(0f, 13f, 18f, 2.5f);
                    Line(0f, -13f, 18f, 2.5f);
                    break;
                case VisionIconKind.Landscape:
                    Line(-15f, 0f, 2.5f, 18f);
                    Line(15f, 0f, 2.5f, 18f);
                    Line(0f, 9f, 30f, 2.5f);
                    Line(0f, -9f, 30f, 2.5f);
                    break;
                case VisionIconKind.Ultrawide:
                    Line(-17f, 0f, 2.5f, 14f);
                    Line(17f, 0f, 2.5f, 14f);
                    Line(0f, 7f, 34f, 2.5f);
                    Line(0f, -7f, 34f, 2.5f);
                    Line(-10f, 0f, 5f, 2f);
                    Line(10f, 0f, 5f, 2f);
                    break;
                case VisionIconKind.Tilt:
                    Line(0f, 0f, 31f, 3f, 18f);
                    Line(-13f, 7f, 8f, 2.5f, 52f);
                    Line(13f, -7f, 8f, 2.5f, 52f);
                    break;
                case VisionIconKind.Power:
                    Dot(0f, -1f, 31f, true);
                    Line(0f, 10f, 3.4f, 18f);
                    break;
                case VisionIconKind.Vr:
                    TextMeshProUGUI vr = MakeText(
                        parent, "VR", offset, new Vector2(40f, 26f),
                        15f * scale, color, FontStyles.Bold);
                    vr.characterSpacing = 1.2f;
                    vr.raycastTarget = false;
                    break;
                case VisionIconKind.Keyboard:
                    Line(-16f, 0f, 2.5f, 23f);
                    Line(16f, 0f, 2.5f, 23f);
                    Line(0f, 11f, 32f, 2.5f);
                    Line(0f, -11f, 32f, 2.5f);
                    for (int row = 0; row < 2; row++)
                        for (int column = 0; column < 4; column++)
                            Dot(-11f + column * 7.5f, 5f - row * 8f, 2.8f);
                    Line(0f, -7f, 18f, 2.5f);
                    break;
                case VisionIconKind.Record:
                    // Familiar camera-record glyph: a precise ring with a
                    // solid core. The Lab bridge animates the complete orb
                    // red while capture is active.
                    Dot(0f, 1f, 31f, true);
                    Dot(0f, 1f, 14f);
                    break;
                case VisionIconKind.Lock:
                    // Compact visionOS-style closed padlock. The selected
                    // surface is tinted by SetControlCenterState.
                    Line(-11f, -4f, 3f, 18f);
                    Line(11f, -4f, 3f, 18f);
                    Line(0f, -12f, 24f, 3f);
                    Line(0f, 4f, 24f, 3f);
                    Line(-8f, 11f, 3f, 14f);
                    Line(8f, 11f, 3f, 14f);
                    Line(0f, 17f, 16f, 3f);
                    Dot(0f, -4f, 4f);
                    break;
            }
        }

        private static void SetControlCenterState(
            Button button,
            bool selected,
            Color selectedSurface)
        {
            if (button == null) return;
            VisionSpatialControlFeedback feedback =
                button.GetComponent<VisionSpatialControlFeedback>();
            if (feedback != null)
                feedback.SetSelected(selected, selectedSurface, VisionInk);
        }

        private static Sprite GetVisionRoundedSprite()
        {
            if (_visionRoundedSprite != null) return _visionRoundedSprite;
            _visionRoundedSprite = BuildVisionSprite(false);
            return _visionRoundedSprite;
        }

        public static Sprite GetLabWindowHandleSprite() =>
            GetVisionRoundedSprite();

        private static Sprite GetVisionTopRoundedSprite()
        {
            if (_visionTopRoundedSprite != null)
                return _visionTopRoundedSprite;
            const int size = 64;
            const float radius = 18f;
            var texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false, true)
            {
                name = "MLOmega Vision top-rounded section",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 1f;
                    if (y + .5f > size - radius)
                    {
                        float centreX = x + .5f < radius
                            ? radius
                            : (x + .5f > size - radius
                                ? size - radius
                                : x + .5f);
                        float dx = x + .5f - centreX;
                        float dy = y + .5f - (size - radius);
                        float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                        alpha = Mathf.Clamp01(.75f - distance);
                    }
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply(false, true);
            _visionTopRoundedSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            return _visionTopRoundedSprite;
        }

        private static Sprite GetVisionCircleSprite()
        {
            if (_visionCircleSprite != null) return _visionCircleSprite;
            _visionCircleSprite = BuildVisionSprite(true);
            return _visionCircleSprite;
        }

        private static Sprite GetVisionRingSprite()
        {
            if (_visionRingSprite != null) return _visionRingSprite;
            const int size = 64;
            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "MLOmega Vision progress ring",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            float half = size * .5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + .5f - half;
                    float py = y + .5f - half;
                    float radius = Mathf.Sqrt(px * px + py * py);
                    float outer = Mathf.Clamp01(1.2f - Mathf.Abs(radius - 29f));
                    float inner = Mathf.Clamp01((radius - 24.5f) * 2f);
                    texture.SetPixel(
                        x,
                        y,
                        new Color(1f, 1f, 1f, outer * inner));
                }
            }
            texture.Apply(false, true);
            _visionRingSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f);
            return _visionRingSprite;
        }

        private static Sprite GetVisionSpeakerSprite()
        {
            if (_visionSpeakerSprite != null) return _visionSpeakerSprite;
            const int size = 64;
            var texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false, true)
            {
                name = "MLOmega Vision speaker",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool body = x >= 8 && x <= 20 && y >= 25 && y <= 39;
                    bool cone = false;
                    if (x >= 20 && x <= 36)
                    {
                        float t = (x - 20f) / 16f;
                        float upper = Mathf.Lerp(25f, 14f, t);
                        float lower = Mathf.Lerp(39f, 50f, t);
                        cone = y >= upper && y <= lower;
                    }
                    float dx = x - 35f;
                    float dy = y - 32f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    bool wave = x >= 38 &&
                        (Mathf.Abs(distance - 13f) < 1.45f ||
                         Mathf.Abs(distance - 22f) < 1.45f);
                    float alpha = body || cone || wave ? 1f : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply(false, true);
            _visionSpeakerSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f);
            return _visionSpeakerSprite;
        }

        private static Image AddVisionProgressRing(
            Transform parent,
            string name,
            Vector2 position,
            float size)
        {
            Image ring = MakeImage(
                parent,
                name,
                position,
                Vector2.one * size,
                new Color(.94f, .96f, 1f, .90f));
            ring.sprite = GetVisionRingSprite();
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;
            ring.raycastTarget = false;
            return ring;
        }

        private static Sprite GetVisionCornerArcSprite()
        {
            if (_visionCornerArcSprite != null) return _visionCornerArcSprite;
            const int size = 72;
            var texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false, true)
            {
                name = "MLOmega Vision corner resize arc",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            // A genuine quarter-circle hugging the lower corner, rather than a
            // nearly vertical parenthesis.  The mirrored right handle reuses
            // the exact same texture and therefore remains visually balanced.
            Vector2 centre = new Vector2(size + 2f, size + 2f);
            const float radius = 51f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Abs(
                        Vector2.Distance(new Vector2(x + .5f, y + .5f), centre) -
                        radius);
                    float alpha = Mathf.Clamp01(2.7f - distance);
                    // Feather the two open ends so the affordance looks drawn,
                    // not cut off by its texture bounds.
                    float edge = Mathf.Min(x + .5f, y + .5f);
                    alpha *= Mathf.Clamp01(edge / 5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply(false, true);
            _visionCornerArcSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f);
            return _visionCornerArcSprite;
        }

        private static Sprite GetVisionCornerArcSpriteLegacy()
        {
            if (_visionCornerArcSprite != null) return _visionCornerArcSprite;
            const int width = 48;
            const int height = 64;
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "MLOmega Vision resize parenthesis",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < height; y++)
            {
                float ny = ((y + .5f) / height) * 2f - 1f;
                float targetX = width * (.30f + .25f * ny * ny);
                for (int x = 0; x < width; x++)
                {
                    float distance = Mathf.Abs(x + .5f - targetX);
                    float alpha = Mathf.Clamp01(2.35f - distance);
                    alpha *= Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((1f - Mathf.Abs(ny)) * 8f));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply(false, true);
            _visionCornerArcSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(.5f, .5f),
                100f);
            return _visionCornerArcSprite;
        }

        private static void ConfigureVisionResizeHandle(
            Image handle,
            bool mirrored)
        {
            if (handle == null) return;
            handle.sprite = GetVisionCornerArcSprite();
            handle.type = Image.Type.Simple;
            handle.preserveAspect = true;
            handle.rectTransform.localRotation = Quaternion.identity;
            handle.rectTransform.localScale = new Vector3(
                mirrored ? -1f : 1f,
                1f,
                1f);
        }

        private static void ConfigureVisionDepthHandle(Image handle)
        {
            if (handle == null) return;
            handle.sprite = GetVisionCircleSprite();
            handle.type = Image.Type.Simple;
            BuildVisionIcon(
                handle.transform,
                VisionIconKind.Depth,
                Vector2.zero,
                .58f);
        }

        private static Image MakeVisionFreeResizeHandle(
            Transform parent,
            Vector2 position)
        {
            Image handle = MakeImage(
                parent,
                "Gaze free resize handle",
                position,
                new Vector2(52f, 32f),
                new Color(.22f, .23f, .27f, .88f));
            handle.raycastTarget = false;
            TextMeshProUGUI label = MakeText(
                handle.transform,
                "H/W",
                Vector2.zero,
                new Vector2(46f, 24f),
                11f,
                new Color(.92f, .94f, .98f, .95f),
                FontStyles.Bold);
            label.raycastTarget = false;
            return handle;
        }

        private static void ConfigureVisionTiltHandle(Image handle)
        {
            if (handle == null) return;
            handle.sprite = GetVisionCircleSprite();
            handle.type = Image.Type.Simple;
            BuildVisionIcon(
                handle.transform,
                VisionIconKind.Tilt,
                Vector2.zero,
                .58f);
        }

        private static void AddVisionHandleDot(Image bar, bool onLeft)
        {
            if (bar == null) return;
            Image dot = MakeImage(
                bar.transform,
                "Vision handle dot",
                new Vector2(onLeft
                    ? -bar.rectTransform.sizeDelta.x * .5f - 8f
                    : bar.rectTransform.sizeDelta.x * .5f + 28f, 0f),
                new Vector2(7f, 7f),
                bar.color);
            dot.sprite = GetVisionCircleSprite();
            dot.type = Image.Type.Simple;
            dot.raycastTarget = false;
        }

        private static Sprite BuildVisionSprite(bool circle)
        {
            const int size = 64;
            const float radius = 18f;
            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = circle
                    ? "MLOmega Vision circle"
                    : "MLOmega Vision rounded rectangle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            float half = size * .5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + .5f - half;
                    float py = y + .5f - half;
                    float distance;
                    if (circle)
                    {
                        distance = Mathf.Sqrt(px * px + py * py) - (half - 1f);
                    }
                    else
                    {
                        float qx = Mathf.Abs(px) - (half - radius - 1f);
                        float qy = Mathf.Abs(py) - (half - radius - 1f);
                        float ox = Mathf.Max(qx, 0f);
                        float oy = Mathf.Max(qy, 0f);
                        distance = Mathf.Sqrt(ox * ox + oy * oy) +
                            Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    }
                    float alpha = Mathf.Clamp01(.75f - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply(false, true);
            Vector4 border = circle
                ? Vector4.zero
                : new Vector4(radius, radius, radius, radius);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
        }

        private static TextMeshProUGUI MakeText(
            Transform parent,
            string text,
            Vector2 position,
            Vector2 size,
            float fontSize,
            Color color,
            FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text " + text);
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return label;
        }

        private static Button MakeButton(
            Transform parent,
            string label,
            Vector2 position,
            Vector2 size,
            UnityEngine.Events.UnityAction action,
            bool primary = false)
        {
            MakeSpatialPlate(
                parent,
                "Button depth " + label,
                position,
                size,
                primary ? 12f : 7f,
                primary);
            Color normalSurface = primary
                ? new Color(.92f, .95f, .98f, .96f)
                : VisionGlass;
            Image image = MakeImage(
                parent,
                "Button " + label,
                position,
                size,
                normalSurface);
            Button button = image.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var collider = image.gameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(size.x, size.y, 14f);
            button.onClick.AddListener(action);
            TextMeshProUGUI labelText = MakeText(
                image.transform,
                label,
                Vector2.zero,
                size - new Vector2(12f, 8f),
                primary ? 21f : 16f,
                primary ? VisionInk : VisionText,
                primary ? FontStyles.Bold : FontStyles.Normal);
            labelText.enableWordWrapping = false;
            var feedback = image.gameObject.AddComponent<
                VisionSpatialControlFeedback>();
            feedback.Configure(
                image,
                normalSurface,
                primary ? Color.white : VisionGlassHover,
                VisionPressed,
                primary ? VisionInk : VisionText);
            return button;
        }

        private static void MakeSpatialPlate(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            float depth,
            bool primary)
        {
            GameObject plate =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = name;
            plate.transform.SetParent(parent, false);
            plate.transform.localPosition =
                new Vector3(position.x, position.y, depth * .5f + 8f);
            plate.transform.localScale =
                new Vector3(size.x, size.y, depth);
            Collider collider = plate.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            MeshRenderer renderer = plate.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetDeckDepthMaterial(primary);
        }

        private static Material GetDeckDepthMaterial(bool primary)
        {
            Material cached =
                primary ? _deckPrimaryDepthMaterial : _deckDepthMaterial;
            if (cached != null) return cached;
            Shader shader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Transparent") ??
                Shader.Find("MLOmega/XREAL Runtime Unlit");
            var material = new Material(shader);
            Color color = primary
                ? new Color(.72f, .76f, .82f, .10f)
                : new Color(.32f, .35f, .40f, .045f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            material.renderQueue = 3000;
            if (primary)
                _deckPrimaryDepthMaterial = material;
            else
                _deckDepthMaterial = material;
            return material;
        }

        private static TMP_InputField MakeInput(
            Transform parent,
            string placeholder,
            Vector2 position,
            Vector2 size,
            UnityEngine.Events.UnityAction<string> changed)
        {
            Image image = MakeImage(
                parent,
                "Input " + placeholder,
                position,
                size,
                new Color(.16f, .17f, .20f, .68f));
            TMP_InputField input =
                image.gameObject.AddComponent<TMP_InputField>();
            var collider = image.gameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(size.x, size.y, 14f);
            TextMeshProUGUI text = MakeText(
                image.transform,
                string.Empty,
                Vector2.zero,
                size - new Vector2(28f, 8f),
                18f,
                VisionText);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            TextMeshProUGUI hint = MakeText(
                image.transform,
                placeholder,
                Vector2.zero,
                size - new Vector2(28f, 8f),
                17f,
                new Color(.65f, .67f, .72f, .88f));
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            input.textComponent = text;
            input.placeholder = hint;
            input.textViewport = image.rectTransform;
            input.targetGraphic = image;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit =
                placeholder.StartsWith("Titre", StringComparison.Ordinal)
                    ? 120
                    : 240;
            input.onValueChanged.AddListener(changed);
            return input;
        }

        private static void TintButton(Button button, bool selected)
        {
            if (button == null) return;
            VisionSpatialControlFeedback feedback =
                button.GetComponent<VisionSpatialControlFeedback>();
            if (feedback != null)
                feedback.SetSelected(
                    selected,
                    VisionPressed,
                    VisionInk);
        }

        private void EnsurePreview()
        {
            if (_preview != null) return;
            var go = new GameObject("Atelier Hologram Preview");
            _preview = go.AddComponent<WorldHologram>();
            _previewContext = new UIComponentContext(null, null, _camera);
            _preview.Configure(_previewContext, null);
            _preview.Admit(BuildPreviewIntent(), null, _ => { });
        }

        private void RefreshPreview()
        {
            if (_preview == null || _selected == null) return;
            _preview.gameObject.SetActive(true);
            _preview.transform.rotation =
                _previewRotation * Quaternion.Euler(0f, _yaw, 0f);
            _preview.transform.localScale = Vector3.one;
            _preview.Refresh(BuildPreviewIntent());
        }

        private UIIntent BuildPreviewIntent()
        {
            WorldMapStore.WorldAsset asset =
                string.IsNullOrEmpty(_pendingAssetId)
                    ? null
                    : Spatial?.CreatorMap?.FindAsset(_pendingAssetId);
            return new UIIntent
            {
            Type = "ui_intent",
            ContractsVersion = ContractDefaults.Version,
            UiIntentId = "atelier-preview",
            Producer = "world-atelier",
            Component = "world_hologram",
            Anchor = new Dictionary<string, object>
            {
                { "coordinate_space", "tracking_local" },
                {
                    "position",
                    new Dictionary<string, object>
                    {
                        { "x", _previewPosition.x },
                        { "y", _previewPosition.y },
                        { "z", _previewPosition.z },
                    }
                },
            },
            Content = new Dictionary<string, object>
            {
                { "pose_valid", true },
                { "depth_valid", true },
                { "calibration_id", "xreal-eye-tracking-local-v1" },
                { "anchor_quality", .94f },
                { "marker_id", "atelier-preview" },
                { "template_id", _selected?.templateId ?? "neon_sign" },
                { "archetype_id", _selected?.archetypeId ?? "preview" },
                { "style_id", _selected?.styleId ?? "cyan-violet" },
                { "animation_id", _selected?.animationId ?? "soft_pulse" },
                { "accent_hex", _selected?.accentHex ?? "18E8FF" },
                { "secondary_hex", _selected?.secondaryHex ?? "7B3CFF" },
                { "asset_id", asset?.assetId ?? string.Empty },
                { "asset_mime", asset?.mimeType ?? string.Empty },
                { "asset_sha256", asset?.sha256 ?? string.Empty },
                { "asset_base64", asset?.base64Data ?? string.Empty },
                { "asset_file_path", asset?.localFilePath ?? string.Empty },
                { "motion_path", MotionPaths[_motionIndex] },
                { "motion_radius_m", MotionRadius() },
                { "motion_speed", .8f },
                { "motion_height_m", MotionHeight() },
                {
                    "local_euler",
                    new Dictionary<string, object>
                    {
                        { "x", _previewRotation.eulerAngles.x },
                        { "y", _previewRotation.eulerAngles.y + _yaw },
                        { "z", _previewRotation.eulerAngles.z },
                    }
                },
                {
                    "scale",
                    new Dictionary<string, object>
                    {
                        {
                            "x",
                            (_selected?.defaultScale.x ?? 1f) * _uniformScale
                        },
                        {
                            "y",
                            (_selected?.defaultScale.y ?? 1f) * _uniformScale
                        },
                        {
                            "z",
                            (_selected?.defaultScale.z ?? 1f) * _uniformScale
                        },
                    }
                },
                { "label", _label },
                { "subtitle", _subtitle },
                { "kind", "atelier_preview" },
            },
            TruthLevel = "observed",
            Confidence = .94,
            TtlMs = 86400000,
            EvidenceRefs = new List<string>
            {
                "depth:xreal-mesh", "creator:user-confirmed",
            },
            };
        }

        private void OnCreatorOperation(
            string contentId,
            bool success,
            string detail)
        {
            if (success)
            {
                if (detail == "saved")
                {
                    _lastCreatedId = contentId;
                    _status = "ANCRE SAUVEGARDÉE // " +
                        (Spatial?.CreatorMap?.Contents.Count ?? 0) +
                        " ÉLÉMENT(S)";
                }
                else if (detail == "dynamic_saved")
                    _status = "RÈGLE DYNAMIQUE SAUVEGARDÉE // " +
                        (Spatial?.CreatorMap?.DynamicBindings.Count ?? 0);
                else if (detail == "dynamic_removed" || detail == string.Empty)
                    _status = "ÉLÉMENT SUPPRIMÉ";
                else if (detail.StartsWith("map_", StringComparison.Ordinal))
                    _status = "MAP // " + detail.Replace("_", " ").ToUpperInvariant();
            }
            else
            {
                _status = "ÉCHEC ANCRE // " + detail;
            }
        }

        private float MotionRadius() =>
            Mathf.Clamp(
                Mathf.Max(1.5f, _uniformScale * 1.2f),
                .1f,
                40f);

        private float MotionHeight() =>
            MotionPaths[_motionIndex] == "static"
                ? 0f
                : Mathf.Clamp(_uniformScale * .35f, 0f, 20f);

        private void OnImageImported(string path)
        {
            string error = string.Empty;
            if (
                Spatial?.CreatorMap != null &&
                Spatial.CreatorMap.TryAddImageAsset(
                    path,
                    out string assetId,
                    out error))
            {
                _pendingAssetId = assetId;
                _status = "LOGO HOLOGRAPHIQUE PRÊT // " + assetId;
            }
            else
            {
                _status = "LOGO REFUSÉ // " + (error ?? "unknown");
            }
        }

        private void OnGlbImported(string path)
        {
            string error = string.Empty;
            if (
                Spatial?.CreatorMap != null &&
                Spatial.CreatorMap.TryAddGlbAsset(
                    path,
                    out string assetId,
                    out error))
            {
                _pendingAssetId = assetId;
                _status = "MODÈLE GLB VALIDÉ // " + assetId;
                RefreshPreview();
            }
            else
            {
                _status = "GLB REFUSÉ // " + (error ?? "unknown");
            }
        }

        private string ManagedLabel()
        {
            WorldMapStore map = Spatial?.CreatorMap;
            int count = ManagedCount;
            if (map == null || count == 0) return "AUCUN ÉLÉMENT";
            _managedIndex = Mathf.Clamp(_managedIndex, 0, count - 1);
            if (_managedIndex < map.Contents.Count)
            {
                WorldMapStore.WorldContent item = map.Contents[_managedIndex];
                return "A // " +
                    (string.IsNullOrWhiteSpace(item.label)
                        ? item.templateId
                        : item.label);
            }
            WorldMapStore.WorldDynamicBinding binding =
                map.DynamicBindings[_managedIndex - map.Contents.Count];
            return "D // " +
                (string.IsNullOrWhiteSpace(binding.targetLabel)
                    ? binding.targetKind
                    : binding.targetLabel);
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            Texture2D panelTexture = SolidTexture(
                new Color(.015f, .025f, .065f, .92f));
            Texture2D buttonTexture = SolidTexture(
                new Color(.04f, .09f, .16f, .94f));
            Texture2D selectedTexture = SolidTexture(
                new Color(.04f, .32f, .38f, .96f));
            _panel = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(18, 18, 16, 16),
                normal = { background = panelTexture },
            };
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(.25f, 1f, .93f) },
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(.72f, .9f, 1f) },
            };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                wordWrap = true,
                normal =
                {
                    background = buttonTexture,
                    textColor = new Color(.72f, .92f, 1f),
                },
                hover =
                {
                    background = selectedTexture,
                    textColor = Color.white,
                },
            };
            _selectedButton = new GUIStyle(_button)
            {
                fontStyle = FontStyle.Bold,
                normal =
                {
                    background = selectedTexture,
                    textColor = new Color(.95f, 1f, 1f),
                },
            };
            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15,
                normal =
                {
                    background = buttonTexture,
                    textColor = Color.white,
                },
            };
        }

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
