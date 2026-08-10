// MLOmega V19 — E33
// MenuPanel: the liquid-glass action menu opened by the open-palm gesture (§5) or
// the voice command "menu". A grid of actions — Modes (FreeGuy/Minimal/Cacher/
// Privé), Apps (Maps/YouTube/+), Mémoire, Replay, Écran virtuel, Mode payant on/off,
// Fermer — selected by gaze+dwell OR pinch (E26 gestures).
//
// The load-bearing rule (§5): a menu selection emits the SAME device_command /
// intent as the voice path — there is exactly ONE execution path. MenuPanel never
// toggles UI or launches apps itself; it builds a DeviceCommand and hands it to the
// shared DeviceCommandHandler.Execute(...), then emits a UIReceipt (acted).
//
// The panel is a standalone interactive surface (like StatusBar), not an intent-
// admitted component: it is opened/closed by input, so its logic (Open/Close/Select)
// is directly unit-testable in EditMode without the component lifecycle.
using System;
using System.Collections.Generic;
using System.Text;
using MLOmega.Contracts.V19;
using MLOmega.XR.UI;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>One selectable menu action → the device command it emits.</summary>
    public sealed class MenuAction
    {
        public string Label { get; }
        public DeviceCommand Command { get; }

        public MenuAction(string label, DeviceCommand command)
        {
            Label = label;
            Command = command;
        }
    }

    public sealed class MenuPanel : MonoBehaviour, IManipulablePanel, IManipulationFeedback
    {
        [SerializeField] private DeviceCommandHandler _commandHandler;
        [SerializeField] private UITheme _theme;
        [SerializeField] private Material _glassMaterial;
        [SerializeField] private Camera _camera;
        [SerializeField] private AugmentedRealityFeatureRegistry _augmentedReality;

        [Tooltip("Seconds of continuous gaze on an item before it selects (dwell).")]
        [SerializeField] private float _dwellSeconds = 1.0f;
        [SerializeField] private Vector2 _size = new Vector2(0.78f, 0.68f);
        [SerializeField] private Vector2 _minSize = new Vector2(0.48f, 0.42f);
        [SerializeField] private Vector2 _maxSize = new Vector2(1.15f, 0.95f);

        /// <summary>Whether the panel is currently open.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The ordered action grid (built in Awake; overridable for tests).</summary>
        public IReadOnlyList<MenuAction> Actions => _actions;

        /// <summary>Raised when an action is selected (label, command). For receipts/tests.</summary>
        public event Action<MenuAction> ActionSelected;

        /// <summary>Where UIReceipts go (acted on a selection). Optional.</summary>
        public IReceiptSink ReceiptSink { get; set; }

        private readonly List<MenuAction> _actions = new List<MenuAction>();
        private int _gazeIndex = -1;
        private float _gazeStart;
        private GlassPanel _panel;
        private bool _registered;
        private bool _placed;
        private bool _minimised;
        private Vector3 _restorePosition;
        private Quaternion _restoreRotation;
        private Vector2 _restoreSize;
        private bool _augmentedSettingsPage;
        private bool _worldMapsPage;
        private int _augmentedPageIndex;
        // Six features + previous/next/back keeps every physical page at <= 9 rows.
        private const int AugmentedItemsPerPage = 6;
        private static readonly string[] AugmentedLabels =
        {
            "AR globale", "Menus objets", "Actions", "Sons", "Contexte",
            "Mesure AR", "Navigation monde", "Labels du monde",
            "Ancres persistantes", "Occlusion Depth", "Style FreeGuy",
            "Futurs de foule", "Clavier spatial", "Vision mouvement",
            "Lancer ludique", "Carte radio", "Profils studio", "Aura pouls (exp.)",
            "Effets monde auto", "Texte du monde",
            "Navigation intérieure", "Planétarium", "Météo contextuelle",
            "Aide contexte social/juridique",
        };
        private static readonly string[] AugmentedFeatures =
        {
            AugmentedRealityFeatureRegistry.Master,
            AugmentedRealityFeatureRegistry.ObjectMenus,
            AugmentedRealityFeatureRegistry.ActionRecognition,
            AugmentedRealityFeatureRegistry.SemanticSound,
            AugmentedRealityFeatureRegistry.ContextualKnowledge,
            AugmentedRealityFeatureRegistry.ArMeasurement,
            AugmentedRealityFeatureRegistry.StreetNavigation,
            AugmentedRealityFeatureRegistry.WorldLabels,
            AugmentedRealityFeatureRegistry.PersistentAnchors,
            AugmentedRealityFeatureRegistry.DepthOcclusion,
            AugmentedRealityFeatureRegistry.WorldStyling,
            AugmentedRealityFeatureRegistry.TrajectoryForecast,
            AugmentedRealityFeatureRegistry.SpatialKeyboard,
            AugmentedRealityFeatureRegistry.EventVision,
            AugmentedRealityFeatureRegistry.BallisticPreview,
            AugmentedRealityFeatureRegistry.RadioField,
            AugmentedRealityFeatureRegistry.ConsentedPeople,
            AugmentedRealityFeatureRegistry.PulseAura,
            AugmentedRealityFeatureRegistry.AutomaticWorldFx,
            AugmentedRealityFeatureRegistry.WorldText,
            AugmentedRealityFeatureRegistry.IndoorNavigation,
            AugmentedRealityFeatureRegistry.Planetarium,
            AugmentedRealityFeatureRegistry.WeatherContext,
            AugmentedRealityFeatureRegistry.LegalContext,
        };

        private void Awake()
        {
            if (_commandHandler == null) _commandHandler = FindAnyObjectByType<DeviceCommandHandler>();
            if (_camera == null) _camera = Camera.main;
            if (_augmentedReality == null)
                _augmentedReality = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
            if (ReceiptSink == null) ReceiptSink = FindAnyObjectByType<UIReceiptTransportSink>();
            EnsureVisual();
            BuildDefaultActions();
            gameObject.SetActive(false);
        }

        private void EnsureVisual()
        {
            if (_panel != null) return;
            if (_glassMaterial == null)
            {
                Shader shader = Shader.Find("MLOmega/LiquidGlass");
                if (shader != null) _glassMaterial = new Material(shader);
            }
            _panel = new GlassPanel(transform, _size, _theme, _glassMaterial,
                withTitle: true, withBody: true, withTruthChip: false);
            _panel.Title.text = "VIKI // MENU                         −   ×";
            if (_panel.Body != null)
            {
                _panel.Body.fontSize = 0.034f;
                _panel.Body.lineSpacing = 12f;
                _panel.Body.richText = true;
            }
            _panel.SetAlpha(1f);
        }

        /// <summary>The default action grid (§5). Public so tests/scene-builders can rebuild it.</summary>
        public void BuildDefaultActions()
        {
            _augmentedSettingsPage = false;
            _worldMapsPage = false;
            _augmentedPageIndex = 0;
            BuildMainActions();
        }

        private void BuildMainActions()
        {
            ResolveAugmentedRegistry();
            _actions.Clear();
            // Modes.
            _actions.Add(Mode("FreeGuy dynamique", "freeguy"));
            _actions.Add(Mode("FreeGuy ancré", "freeguy_anchored"));
            _actions.Add(new MenuAction(
                "Importer monde",
                new DeviceCommand
                {
                    Type = "device_command",
                    Action = "import_world_map",
                }));
            _actions.Add(new MenuAction(
                "Choisir mondes",
                new DeviceCommand
                {
                    Type = "device_command",
                    Action = "open_world_maps",
                }));
            _actions.Add(Mode("Minimal", "minimal"));
            _actions.Add(Mode("Cacher", "hide_all"));
            _actions.Add(new MenuAction("Privé", new DeviceCommand { Type = "device_command", Action = "privacy_pause" }));
            // Apps.
            _actions.Add(App("Maps", "maps"));
            _actions.Add(App("YouTube", "youtube"));
            // Memory (a voice question is prompted by the app; the menu just opens it).
            _actions.Add(new MenuAction("Mémoire", new DeviceCommand { Type = "device_command", Action = "ask_memory_prompt" }));
            // Owner voice setup (E37 §3): arms the wearer-voice enrolment, exactly like
            // saying "configure ma voix" — the single execution path (owner_enroll intent).
            _actions.Add(new MenuAction("Ma voix", new DeviceCommand { Type = "device_command", Action = "owner_enroll" }));
            // Replay + virtual screen.
            _actions.Add(new MenuAction("Replay", new DeviceCommand { Type = "device_command", Action = "replay" }));
            // T3 explicit, bounded investigation. PC owns the state so this menu
            // choice toggles the exact same session as the natural voice path.
            _actions.Add(new MenuAction("Sherlock", new DeviceCommand { Type = "device_command", Action = "sherlock_toggle" }));
            _actions.Add(new MenuAction("Écran virtuel", new DeviceCommand { Type = "device_command", Action = "virtual_screen" }));
            // E48-A: live on-device translation (On=null → the handler flips current state).
            _actions.Add(new MenuAction("Traduire", new DeviceCommand { Type = "device_command", Action = "translate_live" }));
            // Paid mode on/off.
            _actions.Add(new MenuAction("Mode payant", new DeviceCommand { Type = "device_command", Action = "paid_mode" }));
            _actions.Add(new MenuAction("Mode local", new DeviceCommand { Type = "device_command", Action = "local_mode" }));
            _actions.Add(new MenuAction(
                "Augmenté : " +
                (_augmentedReality?.DisplayState(AugmentedRealityFeatureRegistry.Master) ?? "OFF"),
                Feature(AugmentedRealityFeatureRegistry.Master)));
            _actions.Add(new MenuAction(
                "Réglages AR",
                new DeviceCommand { Type = "device_command", Action = "open_augmented_settings" }));
            // Close.
            _actions.Add(new MenuAction("Fermer", new DeviceCommand { Type = "device_command", Action = "close_menu" }));
            RefreshVisual();
        }

        public void BuildAugmentedActions()
        {
            ResolveAugmentedRegistry();
            _augmentedSettingsPage = true;
            _actions.Clear();
            int pageCount = Mathf.CeilToInt((float)AugmentedFeatures.Length / AugmentedItemsPerPage);
            _augmentedPageIndex = Mathf.Clamp(_augmentedPageIndex, 0, pageCount - 1);
            int start = _augmentedPageIndex * AugmentedItemsPerPage;
            int end = Mathf.Min(start + AugmentedItemsPerPage, AugmentedFeatures.Length);
            for (int i = start; i < end; i++)
                _actions.Add(ToggleLabel(AugmentedLabels[i], AugmentedFeatures[i]));
            if (_augmentedPageIndex > 0)
                _actions.Add(new MenuAction(
                    "Page précédente",
                    new DeviceCommand { Type = "device_command", Action = "augmented_prev_page" }));
            if (_augmentedPageIndex + 1 < pageCount)
                _actions.Add(new MenuAction(
                    "Page suivante",
                    new DeviceCommand { Type = "device_command", Action = "augmented_next_page" }));
            _actions.Add(new MenuAction(
                "Retour",
                new DeviceCommand { Type = "device_command", Action = "back_main_menu" }));
            RefreshVisual();
        }

        public void BuildWorldMapActions()
        {
            _augmentedSettingsPage = false;
            _worldMapsPage = true;
            _actions.Clear();
            IReadOnlyList<WorldMapSelection> maps =
                _commandHandler?.AvailableWorldMaps ??
                Array.Empty<WorldMapSelection>();
            foreach (WorldMapSelection map in maps)
            {
                _actions.Add(new MenuAction(
                    (map.active ? "✓ " : "○ ") + map.displayName +
                    $" ({map.anchoredCount}A/{map.dynamicCount}D)",
                    new DeviceCommand
                    {
                        Type = "device_command",
                        Action = "toggle_world_map",
                        Feature = map.mapId,
                        On = !map.active,
                    }));
            }
            if (maps.Count == 0)
                _actions.Add(new MenuAction(
                    "Aucune map importée",
                    new DeviceCommand
                    {
                        Type = "device_command",
                        Action = "open_world_maps",
                    }));
            _actions.Add(new MenuAction(
                "Importer une map",
                new DeviceCommand
                {
                    Type = "device_command",
                    Action = "import_world_map",
                }));
            _actions.Add(new MenuAction(
                "Retour",
                new DeviceCommand
                {
                    Type = "device_command",
                    Action = "back_main_menu",
                }));
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (_panel?.Body == null) return;
            ResolveAugmentedRegistry();
            RefreshTitle();
            var text = new StringBuilder(256);
            for (int i = 0; i < _actions.Count; i++)
            {
                text.Append(i == _gazeIndex
                    ? "<color=#7FE7FF>›</color> "
                    : "  ");
                DeviceCommand command = _actions[i].Command;
                if (command?.Action == "set_augmented_feature")
                {
                    bool active = _augmentedReality?.IsActive(command.Feature) ?? false;
                    bool selected = _augmentedReality?.IsSelected(command.Feature) ?? false;
                    text.Append(active
                        ? "<color=#67F0C1>●</color> "
                        : selected
                            ? "<color=#FFD24A>◌</color> "
                            : "<color=#617386>○</color> ");
                }
                text.Append(_actions[i].Label);
                if (i + 1 < _actions.Count) text.Append('\n');
            }
            _panel.Body.text = text.ToString();
        }

        private void ResolveAugmentedRegistry()
        {
            if (_augmentedReality == null)
                _augmentedReality = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
        }

        private void RefreshTitle()
        {
            if (_panel?.Title == null) return;
            if (!_augmentedSettingsPage)
            {
                _panel.Title.text =
                    "<color=#7FE7FF>VIKI</color> // MENU                         −   ×";
                return;
            }
            string status = _augmentedReality?.MasterEnabled == true
                ? (_augmentedReality.LastServiceStatus == "ready" ? "ACTIF" : "SYNCHRO")
                : "OFF";
            string colour = status == "ACTIF" ? "#67F0C1" :
                            status == "SYNCHRO" ? "#FFD24A" : "#8FA3B8";
            _panel.Title.text =
                "<color=#7FE7FF>VIKI</color> // AUGMENTÉ  " +
                $"<color={colour}>{status}</color>          −   ×";
        }

        private static MenuAction Mode(string label, string uiMode) =>
            new MenuAction(label, new DeviceCommand { Type = "device_command", Action = "set_ui_mode", UiMode = uiMode });

        private static MenuAction App(string label, string app) =>
            new MenuAction(label, new DeviceCommand { Type = "device_command", Action = "open_app", App = app });

        private static DeviceCommand Feature(string feature) =>
            new DeviceCommand
            {
                Type = "device_command",
                Action = "set_augmented_feature",
                Feature = feature,
                On = null,
            };

        private MenuAction ToggleLabel(string label, string feature) =>
            new MenuAction(
                label + " : " + (_augmentedReality?.DisplayState(feature) ?? "OFF"),
                Feature(feature));

        /// <summary>Open the panel (palm gesture or "menu" command). Idempotent.</summary>
        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _gazeIndex = -1;
            EnsureVisual();
            if (!_placed) PlaceInView();
            gameObject.SetActive(true);
            if (!_registered)
            {
                ManipulablePanelRegistry.Register(this);
                _registered = true;
            }
            RefreshVisual();
        }

        /// <summary>Close the panel. Idempotent.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_registered)
            {
                ManipulablePanelRegistry.Unregister(this);
                _registered = false;
            }
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (_registered)
            {
                ManipulablePanelRegistry.Unregister(this);
                _registered = false;
            }
        }

        private void PlaceInView()
        {
            if (PanelPlacementStore.TryGet(PersistenceKey, out PanelPlacement saved))
            {
                transform.SetPositionAndRotation(saved.Position, saved.Rotation);
                ResizeTo(saved.Size);
            }
            else if (_camera != null)
            {
                transform.SetPositionAndRotation(
                    _camera.transform.TransformPoint(new Vector3(0f, 0f, 1.15f)),
                    Quaternion.LookRotation(transform.position - _camera.transform.position, Vector3.up));
            }
            _placed = true;
        }

        /// <summary>Toggle open/closed (palm gesture).</summary>
        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        /// <summary>
        /// Select an action by index (gaze+dwell or pinch resolve to this). Emits the
        /// SAME device_command the voice path would, via the shared handler, and a
        /// UIReceipt (acted). "Fermer" just closes. Returns the executed command's ok.
        /// </summary>
        public bool Select(int index)
        {
            if (index < 0 || index >= _actions.Count) return false;
            MenuAction action = _actions[index];
            ActionSelected?.Invoke(action);

            string act = action.Command?.Action ?? string.Empty;
            if (act == "open_augmented_settings")
            {
                BuildAugmentedActions();
                SendReceipt(action);
                return true;
            }
            if (act == "open_world_maps")
            {
                BuildWorldMapActions();
                SendReceipt(action);
                return true;
            }
            if (act == "back_main_menu")
            {
                BuildDefaultActions();
                SendReceipt(action);
                return true;
            }
            if (act == "augmented_prev_page" || act == "augmented_next_page")
            {
                _augmentedPageIndex += act == "augmented_next_page" ? 1 : -1;
                BuildAugmentedActions();
                SendReceipt(action);
                return true;
            }
            if (act == "close_menu")
            {
                Close();
                SendReceipt(action);
                return true;
            }

            bool ok = false;
            if (_commandHandler != null && action.Command != null)
            {
                ok = _commandHandler.ExecuteFromMenu(action.Command);
            }
            SendReceipt(action);
            if (act == "set_augmented_feature")
            {
                if (_augmentedSettingsPage) BuildAugmentedActions(); else BuildMainActions();
                return ok;
            }
            if (act == "toggle_world_map")
            {
                BuildWorldMapActions();
                return ok;
            }
            // Selecting a mode/app action closes the menu (single-shot), like a tap.
            Close();
            return ok;
        }

        private void SendReceipt(MenuAction action)
        {
            if (ReceiptSink == null) return;
            var receipt = new UIReceipt
            {
                UiIntentId = "menu:" + (action.Command?.Action ?? "?"),
                Event = "acted",
                Source = "menu",
                UserAction = new Dictionary<string, object> { { "menu_label", action.Label } },
            };
            ReceiptSink.Send(receipt);
        }

        // --- gaze+dwell / pinch input (real, editor-simulatable) ------------------
        private void Update()
        {
            if (!IsOpen) return;
            // Gaze+dwell selection is driven by the renderer telling us which item the
            // gaze ray hits; here we advance the dwell timer for the hovered index.
            if (_gazeIndex >= 0 && Time.unscaledTime - _gazeStart >= _dwellSeconds)
            {
                int sel = _gazeIndex;
                _gazeIndex = -1;
                Select(sel);
            }
        }

        /// <summary>Called by the renderer/gesture layer when the gaze hovers item i (-1 = none).</summary>
        public void SetGazeHover(int index)
        {
            if (index != _gazeIndex)
            {
                _gazeIndex = index;
                _gazeStart = Time.unscaledTime;
                RefreshVisual();
            }
        }

        /// <summary>Called on a pinch (E26): commit the currently-hovered item immediately.</summary>
        public void PinchCommit()
        {
            if (_gazeIndex >= 0) Select(_gazeIndex);
        }

        /// <summary>Resolve a real gesture/gaze viewport point to the visible menu row.</summary>
        public int ResolveActionAtViewport(Vector2 viewportPoint)
        {
            if (!IsOpen || _panel?.Body == null || _camera == null ||
                viewportPoint.x < 0f || viewportPoint.y < 0f) return -1;
            RectTransform body = _panel.Body.rectTransform;
            Ray ray = _camera.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            Plane plane = new Plane(-body.forward, body.position);
            if (!plane.Raycast(ray, out float enter)) return -1;
            Vector3 local3 = body.InverseTransformPoint(ray.GetPoint(enter));
            Vector2 local = new Vector2(local3.x, local3.y);
            Rect rect = body.rect;
            if (!rect.Contains(local) || _actions.Count == 0) return -1;
            float fromTop = rect.yMax - local.y;
            return Mathf.Clamp(Mathf.FloorToInt(fromTop / rect.height * _actions.Count), 0, _actions.Count - 1);
        }

        public void HoverAtViewport(Vector2 viewportPoint) =>
            SetGazeHover(ResolveActionAtViewport(viewportPoint));

        /// <summary>PanelManipulator calls this with panel-local coordinates so an
        /// action row remains clickable instead of being stolen as a window drag.</summary>
        public bool IsActionPoint(Vector2 panelLocalPoint)
        {
            if (_panel?.Body == null) return false;
            Vector3 world = transform.TransformPoint(new Vector3(panelLocalPoint.x, panelLocalPoint.y, 0f));
            Vector3 bodyLocal3 = _panel.Body.rectTransform.InverseTransformPoint(world);
            return _panel.Body.rectTransform.rect.Contains(new Vector2(bodyLocal3.x, bodyLocal3.y));
        }

        public string PersistenceKey => "menu_panel";
        public Transform PanelTransform => transform;
        public Vector2 PanelSize => _size;
        public bool IsManipulable => IsOpen && gameObject.activeInHierarchy;
        public bool LockAspectRatio => false;
        public Vector2 MinSize => _minSize;
        public Vector2 MaxSize => _maxSize;
        public bool IsMinimised => _minimised;

        public void MoveTo(Vector3 worldPosition)
        {
            _placed = true;
            transform.position = worldPosition;
        }

        public void ResizeTo(Vector2 size)
        {
            _placed = true;
            _size = size;
            if (_panel != null) _panel.Root.sizeDelta = size;
        }

        public void CloseFromGesture() => Close();

        public void MinimiseFromGesture()
        {
            if (_minimised) return;
            _minimised = true;
            _restorePosition = transform.position;
            _restoreRotation = transform.rotation;
            _restoreSize = _size;
            ResizeTo(new Vector2(0.12f, 0.12f));
            if (_panel?.Body != null) _panel.Body.gameObject.SetActive(false);
            if (_camera != null)
            {
                transform.SetPositionAndRotation(
                    _camera.transform.TransformPoint(new Vector3(0.55f, -0.3f, 1.15f)),
                    Quaternion.LookRotation(transform.position - _camera.transform.position, Vector3.up));
            }
        }

        public void RestoreFromGesture()
        {
            if (!_minimised) return;
            _minimised = false;
            transform.SetPositionAndRotation(_restorePosition, _restoreRotation);
            ResizeTo(_restoreSize);
            if (_panel?.Body != null) _panel.Body.gameObject.SetActive(true);
            RefreshVisual();
        }

        public void SetManipulationFeedback(bool active, bool resizing) =>
            _panel?.SetManipulationFeedback(active, resizing);
    }
}
