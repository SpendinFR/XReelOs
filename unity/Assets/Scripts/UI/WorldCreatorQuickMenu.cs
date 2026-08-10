using System;
using MLOmega.XR.UI.Components;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MLOmega.XR.UI
{
    public sealed partial class WorldCreatorController
    {
        private Canvas _quickMenu;
        private CanvasGroup _quickMenuGroup;
        private RectTransform _quickMenuRect;
        private readonly System.Collections.Generic.List<Graphic>
            _quickMenuHitGraphics =
                new System.Collections.Generic.List<Graphic>();
        private TextMeshProUGUI _quickMenuClock;
        private TextMeshProUGUI _quickMenuStatus;
        private Button _quickHeadOnlyButton;
        private Button _quickAutoBlockButton;
        private Button _quickAnchorModeButton;
        private Button _quickLowLightButton;
        private Button _quickElectroButton;
        private Button _quickDockReorderButton;
        private TextMeshProUGUI _quickHeadOnlyCaption;
        private TextMeshProUGUI _quickAutoBlockCaption;
        private TextMeshProUGUI _quickAnchorCaption;
        private TextMeshProUGUI _quickLowLightCaption;
        private TextMeshProUGUI _quickElectroCaption;
        private TextMeshProUGUI _quickDockReorderCaption;
        private RectTransform _quickHeadOnlySubmenu;
        private float _quickMenuShownAt = -1f;
        private float _nextQuickMenuTelemetryAt;
        private Canvas _headOnlyPassiveTab;
        private RectTransform _headOnlyPassiveTabRect;
        private bool _headOnlyVisualEnabled;
        private bool _headOnlyVisualInteraction;

        public void SetHeadOnlyModeVisualState(
            bool enabled,
            bool interaction,
            bool notify)
        {
            _headOnlyVisualEnabled = enabled;
            _headOnlyVisualInteraction = enabled && interaction;
            if (_labKeyboardAction != null && _quickMenu == null)
                BuildQuickMenu();
            ApplyHeadOnlyVisualState();
            if (!notify) return;
            ShowGestureToast(
                !enabled
                    ? "Mains actives"
                    : (interaction ? "Regard interactif" : "Regard passif"),
                enabled
                    ? new Color(.42f, .88f, 1f)
                    : new Color(.35f, 1f, .72f));
        }

        public void ToggleQuickMenuFromThumb()
        {
            if (_labKeyboardAction == null) return;
            if (_quickMenu == null) BuildQuickMenu();
            if (_quickMenu == null || _camera == null) return;
            if (_quickMenu.gameObject.activeSelf)
            {
                _quickMenu.gameObject.SetActive(false);
                return;
            }
            OpenQuickMenuAtGaze();
        }

        private void BuildQuickMenu()
        {
            if (_quickMenu != null || _camera == null) return;
            var go = new GameObject("Atelier Quick Menu");
            _quickMenu = go.AddComponent<Canvas>();
            _quickMenu.renderMode = RenderMode.WorldSpace;
            _quickMenu.worldCamera = _camera;
            _quickMenu.sortingOrder = 155;
            _quickMenuGroup = go.AddComponent<CanvasGroup>();
            go.AddComponent<GraphicRaycaster>();
            _quickMenuRect = go.GetComponent<RectTransform>();
            _quickMenuRect.sizeDelta = new Vector2(920f, 136f);

            Image glass = MakeImage(
                _quickMenuRect,
                "Quick menu liquid glass",
                Vector2.zero,
                _quickMenuRect.sizeDelta,
                new Color(.045f, .05f, .062f, .90f));
            glass.sprite = GetVisionRoundedSprite();
            glass.type = Image.Type.Sliced;
            glass.raycastTarget = false;

            _quickMenuClock = MakeText(
                _quickMenuRect,
                "--:--",
                new Vector2(-400f, 20f),
                new Vector2(100f, 28f),
                18f,
                VisionText,
                FontStyles.Normal);
            _quickMenuStatus = MakeText(
                _quickMenuRect,
                "TEL --\nTRACK --  •  TEMP --",
                new Vector2(-376f, -19f),
                new Vector2(154f, 38f),
                9.5f,
                VisionSecondary,
                FontStyles.Normal);

            MakeQuickMenuButton("Quick window dock", VisionIconKind.Window,
                new Vector2(-238f, 10f), ToggleWindowDockFromQuickMenu, "Apps");
            MakeQuickMenuButton("Quick settings", VisionIconKind.Settings,
                new Vector2(-174f, 10f), () =>
                {
                    if (!_headOnlyVisualEnabled) _quickMenu.gameObject.SetActive(false);
                    OpenSettingsDeck(true);
                }, "Réglages");
            MakeQuickMenuButton("Quick keyboard", VisionIconKind.Keyboard,
                new Vector2(-110f, 10f), () =>
                {
                    if (!_headOnlyVisualEnabled) _quickMenu.gameObject.SetActive(false);
                    ToggleLabKeyboardFromGesture();
                }, "Clavier");
            _quickHeadOnlyButton = MakeQuickMenuButton(
                "Quick head only", VisionIconKind.Eye,
                new Vector2(-46f, 10f), ToggleQuickHeadOnlySubmenu, "Regard");
            _quickHeadOnlyCaption = QuickCaptionFor(_quickHeadOnlyButton);
            _quickAutoBlockButton = MakeQuickMenuButton(
                "Quick automatic block", VisionIconKind.Lock,
                new Vector2(18f, 10f), ToggleAutoJoinWindowBlock, "Bloc auto");
            _quickAutoBlockCaption = QuickCaptionFor(_quickAutoBlockButton);
            _quickAnchorModeButton = MakeQuickMenuButton(
                "Quick anchor mode", VisionIconKind.Window,
                new Vector2(82f, 10f), ToggleWindowMode, "Ancrage");
            _quickAnchorCaption = QuickCaptionFor(_quickAnchorModeButton);
            _quickLowLightButton = MakeQuickMenuButton(
                "Quick hand low light", VisionIconKind.Hand,
                new Vector2(146f, 10f), CycleOptionalLabLowLight, "Main nuit");
            _quickLowLightCaption = QuickCaptionFor(_quickLowLightButton);
            _quickElectroButton = MakeQuickMenuButton(
                "Quick electrochromic", VisionIconKind.Glasses,
                new Vector2(210f, 10f), () => AdjustLensControl(true, 1), "Teinte");
            _quickElectroCaption = QuickCaptionFor(_quickElectroButton);
            _quickDockReorderButton = MakeQuickMenuButton(
                "Quick reorganize dock", VisionIconKind.Settings,
                new Vector2(274f, 10f), ToggleLabDockReorder, "Organiser");
            _quickDockReorderCaption = QuickCaptionFor(_quickDockReorderButton);
            MakeQuickMenuButton("Quick close", VisionIconKind.Close,
                new Vector2(338f, 10f),
                () => _quickMenu.gameObject.SetActive(false), "Fermer");

            BuildQuickHeadOnlySubmenu();
            BuildQuickMenuMoveHandle();
            BuildHeadOnlyPassiveTab();
            _quickMenuHitGraphics.Clear();
            _quickMenuRect.GetComponentsInChildren(true, _quickMenuHitGraphics);
            go.SetActive(false);
            ApplyHeadOnlyVisualState();
        }

        private void ToggleWindowDockFromQuickMenu()
        {
            if (_windowDock != null && _windowDock.gameObject.activeSelf)
                DismissWindowDock();
            else
            {
                OpenWindowDockFromTwoPalms();
                if (_windowDockRect != null && _quickMenuRect != null)
                {
                    _windowDockRect.SetPositionAndRotation(
                        _quickMenuRect.position + _quickMenuRect.up * .23f +
                        _quickMenuRect.forward * .035f,
                        _quickMenuRect.rotation);
                }
            }
        }

        private void BuildQuickHeadOnlySubmenu()
        {
            Image panel = MakeImage(
                _quickMenuRect,
                "Quick gaze submenu",
                new Vector2(-46f, -104f),
                new Vector2(226f, 46f),
                new Color(.055f, .060f, .075f, .96f));
            panel.sprite = GetVisionRoundedSprite();
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = false;
            _quickHeadOnlySubmenu = panel.rectTransform;
            MakeQuickSubmenuButton(panel.rectTransform, "Mains", -72f, () =>
            {
                ResolveInteractionSettings();
                if (_interactionSettings?.IsHeadOnlyModeEnabled == true)
                    _interactionSettings.ToggleHeadOnlyMode();
                _quickHeadOnlySubmenu.gameObject.SetActive(false);
            });
            MakeQuickSubmenuButton(panel.rectTransform, "Actif", 0f, () =>
            {
                ResolveInteractionSettings();
                _interactionSettings?.EnterHeadOnlyInteractionMode();
                _quickHeadOnlySubmenu.gameObject.SetActive(false);
            });
            MakeQuickSubmenuButton(panel.rectTransform, "Passif", 72f, () =>
            {
                ResolveInteractionSettings();
                _interactionSettings?.EnterHeadOnlyPassiveMode();
                _quickHeadOnlySubmenu.gameObject.SetActive(false);
            });
            panel.gameObject.SetActive(false);
        }

        private void MakeQuickSubmenuButton(
            RectTransform parent,
            string label,
            float x,
            UnityEngine.Events.UnityAction action)
        {
            Image surface = MakeImage(parent, "Quick gaze " + label,
                new Vector2(x, 0f), new Vector2(66f, 32f),
                new Color(.18f, .19f, .23f, .90f));
            surface.sprite = GetVisionRoundedSprite();
            surface.type = Image.Type.Sliced;
            Button button = surface.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(action);
            var collider = surface.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(68f, 38f, 18f);
            MakeText(surface.transform, label, Vector2.zero,
                new Vector2(60f, 24f), 11f, VisionText, FontStyles.Normal);
            surface.gameObject.AddComponent<VisionSpatialControlFeedback>();
        }

        private void ToggleQuickHeadOnlySubmenu()
        {
            if (_quickHeadOnlySubmenu == null) return;
            _quickHeadOnlySubmenu.gameObject.SetActive(
                !_quickHeadOnlySubmenu.gameObject.activeSelf);
        }

        private void BuildQuickMenuMoveHandle()
        {
            Image handle = MakeImage(
                _quickMenuRect,
                "Quick menu move bar",
                new Vector2(0f, -79f),
                new Vector2(154f, 8f),
                new Color(.78f, .80f, .86f, .68f));
            handle.sprite = GetVisionRoundedSprite();
            handle.type = Image.Type.Sliced;
            handle.raycastTarget = true;
            var collider = handle.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(190f, 34f, 18f);
            handle.gameObject.AddComponent<WorldCreatorQuickMenuMoveHandle>()
                .Configure(_quickMenuRect);
        }

        private void BuildHeadOnlyPassiveTab()
        {
            if (_headOnlyPassiveTab != null || _camera == null) return;
            var go = new GameObject("Atelier Head Only Passive Tab");
            _headOnlyPassiveTab = go.AddComponent<Canvas>();
            _headOnlyPassiveTab.renderMode = RenderMode.WorldSpace;
            _headOnlyPassiveTab.worldCamera = _camera;
            _headOnlyPassiveTab.sortingOrder = 154;
            _headOnlyPassiveTabRect = go.GetComponent<RectTransform>();
            _headOnlyPassiveTabRect.sizeDelta = new Vector2(142f, 22f);
            _headOnlyPassiveTabRect.localScale = Vector3.one * .00060f;
            Image tab = MakeImage(_headOnlyPassiveTabRect,
                "Head only passive liquid tab", Vector2.zero,
                _headOnlyPassiveTabRect.sizeDelta,
                new Color(.30f, .72f, .86f, .44f));
            tab.sprite = GetVisionRoundedSprite();
            tab.type = Image.Type.Sliced;
            tab.raycastTarget = false;
            go.SetActive(false);
        }

        public bool IsHeadOnlyPassiveMenuGazeTarget(Ray gaze)
        {
            if (_headOnlyPassiveTabRect == null ||
                !_headOnlyPassiveTabRect.gameObject.activeSelf)
                return false;
            var plane = new Plane(_headOnlyPassiveTabRect.forward,
                _headOnlyPassiveTabRect.position);
            if (!plane.Raycast(gaze, out float distance) || distance <= 0f)
                return false;
            Vector3 local = _headOnlyPassiveTabRect.InverseTransformPoint(
                gaze.GetPoint(distance));
            Rect bounds = _headOnlyPassiveTabRect.rect;
            bounds.xMin -= 42f;
            bounds.xMax += 42f;
            bounds.yMin -= 36f;
            bounds.yMax += 36f;
            return bounds.Contains(new Vector2(local.x, local.y));
        }

        private void PlaceHeadOnlyPassiveTab()
        {
            if (_headOnlyPassiveTabRect == null || _camera == null) return;
            if (_quickMenuRect != null)
                _headOnlyPassiveTabRect.SetPositionAndRotation(
                    _quickMenuRect.position, _quickMenuRect.rotation);
            else
            {
                Vector3 forward = _camera.transform.forward.normalized;
                Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .96f
                    ? _camera.transform.up : Vector3.up;
                _headOnlyPassiveTabRect.SetPositionAndRotation(
                    _camera.transform.position + forward * .94f - up * .26f,
                    Quaternion.LookRotation(forward, up));
            }
            _headOnlyPassiveTabRect.localScale = Vector3.one * .00060f;
        }

        private void OpenQuickMenuAtGaze()
        {
            if (_quickMenu == null || _camera == null) return;
            Vector3 forward = _camera.transform.forward.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .96f
                ? _camera.transform.up : Vector3.up;
            _quickMenuRect.SetPositionAndRotation(
                _camera.transform.position + forward * .98f - up * .24f,
                Quaternion.LookRotation(forward, up));
            _quickMenuRect.localScale = Vector3.one * .00056f;
            _quickMenu.gameObject.SetActive(true);
            if (_quickHeadOnlySubmenu != null)
                _quickHeadOnlySubmenu.gameObject.SetActive(false);
            if (_headOnlyPassiveTab != null)
                _headOnlyPassiveTab.gameObject.SetActive(false);
            _quickMenuShownAt = Time.unscaledTime;
            _quickMenuGroup.alpha = 0f;
            RefreshQuickMenuTelemetry();
        }

        private void ApplyHeadOnlyVisualState()
        {
            if (_quickHeadOnlyButton != null)
                SetControlCenterState(_quickHeadOnlyButton,
                    _headOnlyVisualEnabled,
                    new Color(.35f, .90f, 1f, .98f));
            if (_quickHeadOnlyCaption != null)
                _quickHeadOnlyCaption.text = !_headOnlyVisualEnabled
                    ? "Mains"
                    : (_headOnlyVisualInteraction ? "Regard actif" : "Regard passif");
            if (_headOnlyPassiveTab != null)
            {
                bool passive = _headOnlyVisualEnabled && !_headOnlyVisualInteraction;
                if (passive) PlaceHeadOnlyPassiveTab();
                _headOnlyPassiveTab.gameObject.SetActive(passive);
            }
            if (_quickMenu == null) return;
            if (_headOnlyVisualEnabled && !_headOnlyVisualInteraction)
                _quickMenu.gameObject.SetActive(false);
            else if (_headOnlyVisualEnabled && !_quickMenu.gameObject.activeSelf)
                OpenQuickMenuAtGaze();
        }

        private Button MakeQuickMenuButton(
            string name,
            VisionIconKind icon,
            Vector2 position,
            UnityEngine.Events.UnityAction action,
            string caption)
        {
            Image surface = MakeImage(_quickMenuRect, name, position,
                Vector2.one * 50f, new Color(.18f, .19f, .23f, .86f));
            surface.sprite = GetVisionCircleSprite();
            surface.type = Image.Type.Simple;
            var button = surface.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var collider = surface.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(58f, 76f, 18f);
            button.onClick.AddListener(action);
            BuildVisionIcon(surface.transform, icon, Vector2.zero, .66f);
            TextMeshProUGUI label = MakeText(surface.transform, caption,
                new Vector2(0f, -37f), new Vector2(86f, 18f),
                9.5f, VisionSecondary, FontStyles.Normal);
            label.gameObject.name = "Quick caption";
            surface.gameObject.AddComponent<VisionSpatialControlFeedback>();
            return button;
        }

        private static TextMeshProUGUI QuickCaptionFor(Button button) =>
            button == null ? null : Array.Find(
                button.GetComponentsInChildren<TextMeshProUGUI>(true),
                label => label.gameObject.name == "Quick caption");

        private void UpdateQuickMenu()
        {
            if (_quickMenu == null || !_quickMenu.gameObject.activeSelf) return;
            if (_quickMenuShownAt >= 0f)
            {
                float progress = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((Time.unscaledTime - _quickMenuShownAt) / .16f));
                _quickMenuGroup.alpha = progress;
                _quickMenuRect.localScale = Vector3.one *
                    Mathf.Lerp(.00051f, .00056f, progress);
                if (progress >= 1f) _quickMenuShownAt = -1f;
            }
            if (Time.unscaledTime >= _nextQuickMenuTelemetryAt)
            {
                _nextQuickMenuTelemetryAt = Time.unscaledTime + .5f;
                RefreshQuickMenuTelemetry();
            }
        }

        private void UpdateHeadOnlyPassiveTabPose()
        {
            if (_headOnlyPassiveTabRect == null ||
                !_headOnlyPassiveTabRect.gameObject.activeSelf || _camera == null)
                return;
            Vector3 forward = _camera.transform.forward.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .96f
                ? _camera.transform.up : Vector3.up;
            Vector3 target = _camera.transform.position + forward * .88f - up * .20f;
            float blend = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            _headOnlyPassiveTabRect.position = Vector3.Lerp(
                _headOnlyPassiveTabRect.position, target, blend);
            _headOnlyPassiveTabRect.rotation = Quaternion.Slerp(
                _headOnlyPassiveTabRect.rotation,
                Quaternion.LookRotation(forward, up), blend);
        }

        private void RefreshQuickMenuTelemetry()
        {
            if (_quickMenuClock != null) _quickMenuClock.text = DateTime.Now.ToString("HH:mm");
            float battery = SystemInfo.batteryLevel;
            string batteryText = battery < 0f ? "--" :
                Mathf.RoundToInt(battery * 100f) + "%";
            string tracking = _interactionSettings?.TrackingStatus ?? "TRACKING // --";
            string temperature = _interactionSettings?.GlassesTemperatureStatus ??
                "XREAL // TEMP --";
            if (_quickMenuStatus != null)
                _quickMenuStatus.text = "TEL " + batteryText + "\n" +
                    (tracking.EndsWith("OK", StringComparison.Ordinal)
                        ? "TRACK OK" : "TRACK BAD") + "  •  " +
                    (temperature.Contains("NORMALE", StringComparison.Ordinal)
                        ? "TEMP OK" : "TEMP !");

            SetControlCenterState(_quickAutoBlockButton, _autoJoinWindowBlock,
                new Color(.35f, .94f, 1f, .98f));
            if (_quickAutoBlockCaption != null)
                _quickAutoBlockCaption.text = _autoJoinWindowBlock ? "Bloc actif" : "Bloc auto";

            int anchorMode = _headFollowWindows ? 1 : (_manualFrozenWindows ? 2 : 0);
            SetControlCenterState(_quickAnchorModeButton, anchorMode != 0,
                anchorMode == 2 ? new Color(.55f, .75f, 1f, .98f) : VisionPressed);
            if (_quickAnchorCaption != null)
                _quickAnchorCaption.text = anchorMode switch
                {
                    1 => "Suivi tête",
                    2 => "Figé",
                    _ => "6DoF",
                };

            HandLowLightMode lowLight =
                _interactionSettings?.CurrentHandLowLightMode ?? HandLowLightMode.Off;
            SetControlCenterState(_quickLowLightButton,
                lowLight != HandLowLightMode.Off,
                lowLight == HandLowLightMode.Strong
                    ? new Color(.72f, .48f, 1f, .98f) : VisionPressed);
            if (_quickLowLightCaption != null)
                _quickLowLightCaption.text = lowLight switch
                {
                    HandLowLightMode.Light => "Nuit légère",
                    HandLowLightMode.Strong => "Nuit forte",
                    _ => "Main nuit",
                };

            int ec = ReadLensMetric(_lensControlState, "ec");
            int ecCount = ReadLensMetric(_lensControlState, "ecc");
            if (_quickElectroCaption != null)
                _quickElectroCaption.text = ec < 0 || ecCount <= 0
                    ? "Teinte" : "Teinte " + (ec + 1) + "/" + ecCount;

            bool reorder = IsLabDockReorderActive();
            SetControlCenterState(_quickDockReorderButton, reorder, VisionPressed);
            if (_quickDockReorderCaption != null)
                _quickDockReorderCaption.text = reorder ? "Valider" : "Organiser";
        }
    }

    public sealed class WorldCreatorQuickMenuMoveHandle : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        IPointerUpHandler, IDragHandler, IPointerClickHandler
    {
        private RectTransform _target;
        private Vector3 _lastWorld;
        private bool _dragging;

        public void Configure(RectTransform target) => _target = target;

        public void OnPointerEnter(PointerEventData eventData) { }
        public void OnPointerExit(PointerEventData eventData) { }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_target == null) return;
            _lastWorld = eventData.pointerCurrentRaycast.worldPosition;
            _dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || _target == null) return;
            Vector3 current = eventData.pointerCurrentRaycast.worldPosition;
            if (current == Vector3.zero || _lastWorld == Vector3.zero) return;
            _target.position += current - _lastWorld;
            _lastWorld = current;
            eventData.eligibleForClick = false;
        }

        public void OnPointerUp(PointerEventData eventData) => _dragging = false;

        // Lets the shared world-space resolver target this drag-only control.
        public void OnPointerClick(PointerEventData eventData) { }
    }
}
