using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Optional spatial surfaces hosted by the proven Atelier window system.
    /// With no registered surface every Product/Atelier branch is unchanged.
    /// The Browser Lab uses this seam instead of maintaining a second pointer,
    /// grab, smoothing or chrome implementation.
    /// </summary>
    public sealed partial class WorldCreatorController
    {
        private sealed class ExternalSpatialWindowState
        {
            public string Id;
            public string LayoutPrefix;
            public RectTransform Rect;
            public Action Close;
            public Action<Vector2, bool> Resize;
            public Action<Vector4, bool> Crop;
            public readonly List<Graphic> HitGraphics = new List<Graphic>();
            public Image Move;
            public Image ResizeLeft;
            public Image ResizeRight;
            public Image Depth;
            public Image Tilt;
            public Image FreeResize;
            public Image CropLeft;
            public Image CropRight;
            public Image CropBottom;
            public Image CropTop;
            public TextMeshProUGUI CloseHandle;
            public Button Portrait;
            public Button Landscape;
            public Button Ultrawide;
            public Button Block;
            public RectTransform AspectMenu;
            public Vector2 NormalSize;
            public float NormalScale;
            public bool AspectPresetActive;
            public bool CropEditing;
            public RectTransform CropViewport;
            public RectTransform CropContent;
            public Vector2 CropBaseSize;
            public Vector4 CropInsets;
            public Vector4 CropStartInsets;
            public Vector4 CropTargetInsets;
            public Vector3 CropBaseWorldPosition;
            public bool BlockLocked;
            public int BlockSlot;
        }

        private readonly List<ExternalSpatialWindowState> _externalSpatialWindows =
            new List<ExternalSpatialWindowState>();
        private ExternalSpatialWindowState _hoverExternalWindow;
        private ExternalSpatialWindowState _activeExternalWindow;
        private ExternalSpatialWindowState _lastExternalWindow;
        private ExternalSpatialWindowState _externalAffordanceWindow;
        private bool _gestureWindowSnapshotValid;
        private bool _gestureWorkspaceWasVisible;
        private bool _gestureSettingsWasVisible;
        private bool _gestureDockWasVisible;
        private readonly List<ExternalSpatialWindowState> _gestureVisibleWindows =
            new List<ExternalSpatialWindowState>();
        private const int ExternalCropSchema = 2;

        public void RegisterExternalSpatialWindow(
            RectTransform rect,
            string id,
            Action close,
            Action<Vector2, bool> resize = null,
            Action<Vector4, bool> crop = null)
        {
            if (rect == null) throw new ArgumentNullException(nameof(rect));
            ExternalSpatialWindowState existing = ExternalWindowFor(rect);
            if (existing != null)
            {
                existing.Close = close;
                existing.Resize = resize;
                existing.Crop = crop;
                FocusExternalSpatialWindow(rect);
                return;
            }

            var state = new ExternalSpatialWindowState
            {
                Id = string.IsNullOrWhiteSpace(id) ? rect.name : id.Trim(),
                Rect = rect,
                Close = close,
                Resize = resize,
                Crop = crop,
            };
            state.LayoutPrefix =
                "mlomega.atelier.external." + SanitizeLayoutId(state.Id) + ".v1.";
            // Protected-surface source cropping is not atomic between both eyes
            // on One Pro. Keep H/W resize, but never expose or restore that crop.
            if (state.Crop == null) ClearLegacyExternalCrop(state);
            state.NormalSize = rect.rect.size;
            state.NormalScale = rect.localScale.x;
            state.CropBaseSize = rect.rect.size;
            BuildExternalCropViewport(state);
            BuildExternalWindowHandles(state);
            InitializeWindowBlockState(DeckWindowKind.External, state);
            state.Rect.GetComponentsInChildren(true, state.HitGraphics);
            _externalSpatialWindows.Add(state);
            RestoreExternalWindowLayout(state);
            if (_autoJoinWindowBlock)
                JoinWindowBlock(DeckWindowKind.External, state, false);
            // AUTO always means the deterministic factory footprint captured
            // before restoring a saved/custom layout. It is the size equivalent
            // of recentering, never the last oversized preset.
            _activeExternalWindow = state;
            _lastExternalWindow = state;
            _lastWindow = DeckWindowKind.External;
            RevealExternalWindowAffordances(state);
            if (_manualFrozenWindows)
                SetExternalWindowChromeFrozen(true);
        }

        public void RefreshExternalSpatialWindow(RectTransform rect)
        {
            ExternalSpatialWindowState state = ExternalWindowFor(rect);
            if (state == null) return;
            // The browser can change its footprint when entering/leaving XR.
            // Keep the shared Atelier affordances attached to the new outer
            // bounds before rebuilding the hit list.
            if (state.CropInsets.sqrMagnitude <= .000001f)
            {
                state.CropBaseSize = rect.sizeDelta;
                if (state.CropContent != null)
                    state.CropContent.sizeDelta = rect.sizeDelta;
            }
            LayoutExternalWindowHandles(state);
            state.HitGraphics.Clear();
            rect.GetComponentsInChildren(true, state.HitGraphics);
        }

        public void FocusExternalSpatialWindow(RectTransform rect)
        {
            ExternalSpatialWindowState state = ExternalWindowFor(rect);
            if (state == null) return;
            _activeExternalWindow = state;
            _lastExternalWindow = state;
            _lastWindow = DeckWindowKind.External;
            DismissWindowDock();
        }

        public void UnregisterExternalSpatialWindow(RectTransform rect)
        {
            ExternalSpatialWindowState state = ExternalWindowFor(rect);
            if (state == null) return;
            if (_activeExternalWindow == state && IsDeckManipulating)
                EndDeckManipulation();
            _externalSpatialWindows.Remove(state);
            if (_hoverExternalWindow == state) _hoverExternalWindow = null;
            if (_activeExternalWindow == state) _activeExternalWindow = null;
            if (_lastExternalWindow == state)
                _lastExternalWindow = LastVisibleExternalWindow();
            if (_externalAffordanceWindow == state)
                _externalAffordanceWindow = null;
        }

        public void DismissWindowDock()
        {
            if (_windowDock != null) _windowDock.gameObject.SetActive(false);
        }

        private bool HasVisibleExternalSpatialWindows()
        {
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
                if (IsExternalWindowVisible(_externalSpatialWindows[i])) return true;
            return false;
        }

        private ExternalSpatialWindowState LastVisibleExternalWindow()
        {
            for (int i = _externalSpatialWindows.Count - 1; i >= 0; i--)
                if (IsExternalWindowVisible(_externalSpatialWindows[i]))
                    return _externalSpatialWindows[i];
            return null;
        }

        private static bool IsExternalWindowVisible(ExternalSpatialWindowState state) =>
            state?.Rect != null && state.Rect.gameObject.activeInHierarchy;

        private ExternalSpatialWindowState ExternalWindowFor(RectTransform rect)
        {
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
                if (_externalSpatialWindows[i].Rect == rect)
                    return _externalSpatialWindows[i];
            return null;
        }

        private bool TryProjectExternalWindows(
            Ray ray,
            ref float bestDistance,
            ref Vector3 worldPoint)
        {
            bool hit = false;
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
            {
                ExternalSpatialWindowState state = _externalSpatialWindows[i];
                if (!IsExternalWindowVisible(state)) continue;
                hit |= TryProjectExternalWindow(
                    ray, state.Rect, ref bestDistance, ref worldPoint);
            }
            return hit;
        }

        private static bool TryProjectExternalWindow(
            Ray ray,
            RectTransform rect,
            ref float bestDistance,
            ref Vector3 bestPoint)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return false;
            var plane = new Plane(rect.forward, rect.position);
            if (!plane.Raycast(ray, out float distance) ||
                distance < .03f || distance > 4f || distance >= bestDistance)
                return false;
            Vector3 point = ray.GetPoint(distance);
            Vector3 local = rect.InverseTransformPoint(point);
            Rect bounds = rect.rect;
            // Window controls genuinely float outside the glass. Extend only the
            // interaction gutter, never the rendered application surface.
            bounds.xMin -= 70f;
            bounds.xMax += 70f;
            bounds.yMin -= 70f;
            bounds.yMax += 250f;
            if (!bounds.Contains(new Vector2(local.x, local.y))) return false;
            bestDistance = distance;
            bestPoint = point;
            return true;
        }

        private void ResolveExternalWindowTargets(
            Vector3 worldPoint,
            ref GameObject target,
            ref float smallestArea)
        {
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
            {
                ExternalSpatialWindowState state = _externalSpatialWindows[i];
                if (!IsExternalWindowVisible(state)) continue;
                ResolveTargetInGraphics(
                    state.HitGraphics,
                    worldPoint,
                    ref target,
                    ref smallestArea);
            }
        }

        /// <summary>
        /// Resolve Lab-only content before the screen-space GraphicRaycaster.
        /// XREAL eye coordinates and the S24 display coordinates differ, so a
        /// screen-space hit can suppress the exact world-space result. With no
        /// external window this is a strict no-op for Product and Atelier.
        /// </summary>
        public bool TryResolveExternalSpatialTarget(
            Vector3 worldPoint,
            out GameObject target)
        {
            target = null;
            float smallestArea = float.MaxValue;
            ResolveExternalWindowTargets(
                worldPoint,
                ref target,
                ref smallestArea);
            return target != null;
        }

        private DeckManipulationMode ClassifyExternalWindowHandle(
            Vector3 worldPoint,
            out ExternalSpatialWindowState state)
        {
            state = null;
            if (_manualFrozenWindows) return DeckManipulationMode.None;
            for (int i = _externalSpatialWindows.Count - 1; i >= 0; i--)
            {
                ExternalSpatialWindowState candidate = _externalSpatialWindows[i];
                if (
                    IsExternalWindowVisible(candidate) &&
                    IsPointInsideExternalHandle(
                        candidate.CloseHandle,
                        worldPoint))
                {
                    state = candidate;
                    return DeckManipulationMode.Minimize;
                }
                if (candidate.CropEditing)
                {
                    if (IsPointInsideExternalHandle(candidate.CropLeft, worldPoint))
                    {
                        state = candidate;
                        return DeckManipulationMode.CropLeft;
                    }
                    if (IsPointInsideExternalHandle(candidate.CropRight, worldPoint))
                    {
                        state = candidate;
                        return DeckManipulationMode.CropRight;
                    }
                    if (IsPointInsideExternalHandle(candidate.CropBottom, worldPoint))
                    {
                        state = candidate;
                        return DeckManipulationMode.CropBottom;
                    }
                    if (IsPointInsideExternalHandle(candidate.CropTop, worldPoint))
                    {
                        state = candidate;
                        return DeckManipulationMode.CropTop;
                    }
                }
                // CROP is an ordinary click action, not a manipulation handle.
                // Its visual occupies the old H/W slot inside the bottom rim;
                // without this early exclusion the generic rim classifier
                // claimed it as Move and the Button never received onClick.
                if (
                    candidate.Crop != null &&
                    IsPointInsideExternalHandle(candidate.FreeResize, worldPoint))
                {
                    state = candidate;
                    return DeckManipulationMode.None;
                }
                if (
                    candidate.Crop == null &&
                    IsPointInsideExternalHandle(candidate.FreeResize, worldPoint))
                {
                    state = candidate;
                    return DeckManipulationMode.ResizeFree;
                }
                DeckManipulationMode mode = ClassifyWindowHandle(
                    candidate.Rect,
                    IsExternalWindowVisible(candidate),
                    worldPoint);
                if (mode == DeckManipulationMode.None) continue;
                state = candidate;
                return mode;
            }
            return DeckManipulationMode.None;
        }

        private static void BuildExternalCropViewport(
            ExternalSpatialWindowState state)
        {
            if (state?.Rect == null || state.Crop == null) return;
            var existing = new List<Transform>();
            for (int i = 0; i < state.Rect.childCount; i++)
            {
                Transform child = state.Rect.GetChild(i);
                // Native app action handles deliberately live outside the video
                // frame and must never be clipped with page content.
                if (child.name.StartsWith("Android app ", StringComparison.Ordinal))
                    continue;
                existing.Add(child);
            }

            var viewportObject = new GameObject("External crop viewport");
            viewportObject.transform.SetParent(state.Rect, false);
            state.CropViewport = viewportObject.AddComponent<RectTransform>();
            state.CropViewport.anchorMin = Vector2.zero;
            state.CropViewport.anchorMax = Vector2.one;
            state.CropViewport.offsetMin = Vector2.zero;
            state.CropViewport.offsetMax = Vector2.zero;
            viewportObject.AddComponent<RectMask2D>();
            state.CropViewport.SetAsFirstSibling();

            var contentObject = new GameObject("External crop content");
            contentObject.transform.SetParent(state.CropViewport, false);
            state.CropContent = contentObject.AddComponent<RectTransform>();
            state.CropContent.anchorMin = state.CropContent.anchorMax =
                new Vector2(.5f, .5f);
            state.CropContent.anchoredPosition = Vector2.zero;
            state.CropContent.sizeDelta = state.CropBaseSize;
            for (int i = 0; i < existing.Count; i++)
                existing[i].SetParent(state.CropContent, false);
        }

        private void BuildExternalWindowHandles(ExternalSpatialWindowState state)
        {
            Rect rect = state.Rect.rect;
            float bottom = rect.yMin + 17f;
            state.Move = MakeImage(
                state.Rect, "Gaze move handle", new Vector2(-70f, bottom),
                new Vector2(104f, 5f), new Color(.76f, .78f, .82f, .78f));
            state.Move.raycastTarget = false;
            AddVisionHandleDot(state.Move, false);
            state.ResizeLeft = MakeImage(
                state.Rect, "Gaze resize handle", new Vector2(rect.xMin + 13f, rect.yMin + 13f),
                new Vector2(24f, 32f), new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionResizeHandle(state.ResizeLeft, false);
            state.ResizeLeft.raycastTarget = false;
            state.ResizeRight = MakeImage(
                state.Rect, "Gaze resize handle right", new Vector2(rect.xMax - 13f, rect.yMin + 13f),
                new Vector2(24f, 32f), new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionResizeHandle(state.ResizeRight, true);
            state.ResizeRight.raycastTarget = false;
            state.Depth = MakeImage(
                state.Rect, "Gaze depth handle", new Vector2(72f, bottom),
                new Vector2(52f, 34f), new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionDepthHandle(state.Depth);
            state.Depth.raycastTarget = false;
            state.Tilt = MakeImage(
                state.Rect, "Gaze tilt handle", new Vector2(136f, bottom),
                new Vector2(52f, 34f), new Color(.76f, .78f, .82f, .78f));
            ConfigureVisionTiltHandle(state.Tilt);
            state.Tilt.raycastTarget = false;
            if (state.Resize != null)
            {
                state.FreeResize = MakeVisionFreeResizeHandle(
                    state.Rect,
                    new Vector2(rect.xMin + 96f, bottom));
                if (state.Crop != null)
                {
                    state.FreeResize.raycastTarget = true;
                    TextMeshProUGUI label =
                        state.FreeResize.GetComponentInChildren<TextMeshProUGUI>();
                    if (label != null) label.text = "CROP";
                    Button cropButton = state.FreeResize.gameObject.AddComponent<Button>();
                    cropButton.targetGraphic = state.FreeResize;
                    cropButton.onClick.AddListener(() => ToggleExternalCropFrame(state));
                    state.CropLeft = MakeImage(
                        state.Rect, "Crop left " + state.Id, Vector2.zero,
                        new Vector2(8f, 120f), new Color(.65f, .92f, 1f, .94f));
                    state.CropRight = MakeImage(
                        state.Rect, "Crop right " + state.Id, Vector2.zero,
                        new Vector2(8f, 120f), new Color(.65f, .92f, 1f, .94f));
                    state.CropBottom = MakeImage(
                        state.Rect, "Crop bottom " + state.Id, Vector2.zero,
                        new Vector2(120f, 8f), new Color(.65f, .92f, 1f, .94f));
                    state.CropTop = MakeImage(
                        state.Rect, "Crop top " + state.Id, Vector2.zero,
                        new Vector2(120f, 8f), new Color(.65f, .92f, 1f, .94f));
                    state.CropLeft.raycastTarget = false;
                    state.CropRight.raycastTarget = false;
                    state.CropBottom.raycastTarget = false;
                    state.CropTop.raycastTarget = false;
                }
                state.Portrait = MakeOrientationButton(
                    state.Rect,
                    "External Portrait " + state.Id,
                    VisionIconKind.Portrait,
                    () => ApplyExternalAspectPreset(state, 3f / 4f));
                state.Landscape = MakeOrientationButton(
                    state.Rect,
                    "External Landscape " + state.Id,
                    VisionIconKind.Landscape,
                    () => ApplyExternalAspectPreset(state, 16f / 9f));
                state.Ultrawide = MakeOrientationButton(
                    state.Rect,
                    "External Ultrawide " + state.Id,
                    VisionIconKind.Ultrawide,
                    () => ToggleExternalAspectMenu(state));
                BuildExternalAspectMenu(state);
            }
            state.CloseHandle = MakeText(
                state.Rect, "×", new Vector2(rect.xMax - 17f, rect.yMax - 17f),
                new Vector2(34f, 34f), 24f,
                new Color(.82f, .84f, .88f, .90f));
            state.CloseHandle.raycastTarget = false;
            state.Block = MakeOrientationButton(
                state.Rect,
                "External Block " + state.Id,
                VisionIconKind.Lock,
                () => ToggleWindowBlock(DeckWindowKind.External, state));
            LayoutExternalWindowHandles(state);
            SetExternalHandlesActive(state, false);
        }

        private void SetExternalHandlesActive(
            ExternalSpatialWindowState state,
            bool active)
        {
            if (state == null) return;
            if (state.Move != null) state.Move.gameObject.SetActive(active);
            if (state.ResizeLeft != null) state.ResizeLeft.gameObject.SetActive(active);
            if (state.ResizeRight != null) state.ResizeRight.gameObject.SetActive(active);
            if (state.Depth != null) state.Depth.gameObject.SetActive(active);
            if (state.Tilt != null) state.Tilt.gameObject.SetActive(active);
            if (state.FreeResize != null) state.FreeResize.gameObject.SetActive(active);
            SetCropFrameActive(state, active && state.CropEditing);
            if (state.CloseHandle != null) state.CloseHandle.gameObject.SetActive(active);
        }

        private void SetExternalWindowHandleVisuals(
            DeckManipulationMode mode,
            DeckWindowKind window)
        {
            if (_manualFrozenWindows)
            {
                for (int i = 0; i < _externalSpatialWindows.Count; i++)
                    SetExternalHandlesActive(_externalSpatialWindows[i], false);
                return;
            }
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
            {
                ExternalSpatialWindowState state = _externalSpatialWindows[i];
                bool reveal =
                    state == _externalAffordanceWindow &&
                    Time.unscaledTime < _deckAffordanceRevealUntil &&
                    _deckAffordanceRevealWindow == DeckWindowKind.External;
                SetExternalVisionHandle(state.Move, reveal, mode, window,
                    DeckManipulationMode.Move, state);
                SetExternalVisionHandle(state.ResizeLeft, reveal, mode, window,
                    DeckManipulationMode.ResizeLeft, state);
                SetExternalVisionHandle(state.ResizeRight, reveal, mode, window,
                    DeckManipulationMode.ResizeRight, state);
                SetExternalVisionHandle(state.Depth, reveal, mode, window,
                    DeckManipulationMode.Depth, state);
                SetExternalVisionHandle(state.Tilt, reveal, mode, window,
                    DeckManipulationMode.Tilt, state);
                SetExternalVisionHandle(state.FreeResize, reveal, mode, window,
                    DeckManipulationMode.ResizeFree, state);
                SetExternalCropEdgeVisual(state.CropLeft, state, mode, window,
                    DeckManipulationMode.CropLeft);
                SetExternalCropEdgeVisual(state.CropRight, state, mode, window,
                    DeckManipulationMode.CropRight);
                SetExternalCropEdgeVisual(state.CropBottom, state, mode, window,
                    DeckManipulationMode.CropBottom);
                SetExternalCropEdgeVisual(state.CropTop, state, mode, window,
                    DeckManipulationMode.CropTop);
                SetExternalVisionHandle(state.CloseHandle, reveal, mode, window,
                    DeckManipulationMode.Minimize, state);
            }
        }

        private void SetExternalWindowChromeFrozen(bool frozen)
        {
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
            {
                ExternalSpatialWindowState state = _externalSpatialWindows[i];
                if (frozen)
                    SetExternalHandlesActive(state, false);
                if (state.Portrait != null)
                    state.Portrait.gameObject.SetActive(!frozen);
                if (state.Landscape != null)
                    state.Landscape.gameObject.SetActive(!frozen);
                if (state.Ultrawide != null)
                    state.Ultrawide.gameObject.SetActive(!frozen);
                if (state.Block != null)
                    state.Block.gameObject.SetActive(!frozen);
                if (state.AspectMenu != null)
                    state.AspectMenu.gameObject.SetActive(false);
            }
            if (!frozen)
                SetExternalWindowHandleVisuals(
                    DeckManipulationMode.None,
                    DeckWindowKind.None);
        }

        private void SetExternalVisionHandle(
            Graphic handle,
            bool reveal,
            DeckManipulationMode mode,
            DeckWindowKind window,
            DeckManipulationMode ownMode,
            ExternalSpatialWindowState owner)
        {
            if (handle == null) return;
            bool targeted = window == DeckWindowKind.External &&
                _hoverExternalWindow == owner && mode == ownMode;
            bool engaged = _activeExternalWindow == owner &&
                _deckManipulationMode == ownMode;
            handle.gameObject.SetActive(reveal || targeted || engaged);
            if (!handle.gameObject.activeSelf) return;
            if (handle == owner.FreeResize)
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

        private void SetExternalCropEdgeVisual(
            Image handle,
            ExternalSpatialWindowState owner,
            DeckManipulationMode mode,
            DeckWindowKind window,
            DeckManipulationMode ownMode)
        {
            if (handle == null) return;
            handle.gameObject.SetActive(owner.CropEditing);
            if (!owner.CropEditing) return;
            bool targeted = window == DeckWindowKind.External &&
                _hoverExternalWindow == owner && mode == ownMode;
            bool engaged = _activeExternalWindow == owner &&
                _deckManipulationMode == ownMode;
            handle.color = engaged
                ? Color.white
                : (targeted
                    ? new Color(.82f, .98f, 1f, 1f)
                    : new Color(.55f, .86f, .98f, .82f));
        }

        private void RevealExternalWindowAffordances(
            ExternalSpatialWindowState state,
            float seconds = 4f)
        {
            _externalAffordanceWindow = state;
            _deckAffordanceRevealWindow = DeckWindowKind.External;
            _deckAffordanceRevealUntil = Time.unscaledTime + seconds;
            SetExternalWindowHandleVisuals(
                DeckManipulationMode.None,
                DeckWindowKind.External);
        }

        private string LayoutPrefixForWindow(DeckWindowKind window)
        {
            if (window == DeckWindowKind.Settings) return SettingsLayoutPrefix;
            if (window == DeckWindowKind.External && _activeExternalWindow != null)
                return _activeExternalWindow.LayoutPrefix;
            return DeckLayoutPrefix;
        }

        private void RestoreExternalWindowLayout(ExternalSpatialWindowState state)
        {
            if (state?.Rect == null || _camera == null ||
                !PlayerPrefs.HasKey(state.LayoutPrefix + "x")) return;
            Vector3 local = new Vector3(
                PlayerPrefs.GetFloat(state.LayoutPrefix + "x"),
                PlayerPrefs.GetFloat(state.LayoutPrefix + "y"),
                PlayerPrefs.GetFloat(state.LayoutPrefix + "z", 1.1f));
            Vector3 position = _camera.transform.TransformPoint(local);
            Vector3 forward = (position - _camera.transform.position).normalized;
            state.Rect.SetPositionAndRotation(
                position,
                BuildWindowRotation(
                    forward,
                    PlayerPrefs.GetFloat(state.LayoutPrefix + "tilt", 0f),
                    PlayerPrefs.GetFloat(state.LayoutPrefix + "turn", 0f)));
            float scale = PlayerPrefs.GetFloat(
                state.LayoutPrefix + "scale",
                state.Rect.localScale.x);
            state.Rect.localScale = Vector3.one * Mathf.Clamp(
                scale,
                .00015f,
                .01000f);
            if (PlayerPrefs.HasKey(state.LayoutPrefix + "width"))
            {
                state.Rect.sizeDelta = new Vector2(
                    Mathf.Clamp(
                        PlayerPrefs.GetFloat(state.LayoutPrefix + "width"),
                        360f,
                        2600f),
                    Mathf.Clamp(
                        PlayerPrefs.GetFloat(state.LayoutPrefix + "height"),
                        260f,
                        1200f));
            }
            if (state.Crop != null &&
                PlayerPrefs.GetInt(
                    state.LayoutPrefix + "crop_schema", 0) == ExternalCropSchema &&
                PlayerPrefs.HasKey(state.LayoutPrefix + "crop_base_width"))
            {
                Vector2 visibleSize = state.Rect.sizeDelta;
                state.CropBaseSize = new Vector2(
                    PlayerPrefs.GetFloat(
                        state.LayoutPrefix + "crop_base_width",
                        state.NormalSize.x),
                    PlayerPrefs.GetFloat(
                        state.LayoutPrefix + "crop_base_height",
                        state.NormalSize.y));
                state.CropInsets = new Vector4(
                    PlayerPrefs.GetFloat(state.LayoutPrefix + "crop_left", 0f),
                    PlayerPrefs.GetFloat(state.LayoutPrefix + "crop_right", 0f),
                    PlayerPrefs.GetFloat(state.LayoutPrefix + "crop_bottom", 0f),
                    PlayerPrefs.GetFloat(state.LayoutPrefix + "crop_top", 0f));
                state.CropTargetInsets = state.CropInsets;
                state.Resize?.Invoke(state.CropBaseSize, true);
                state.Rect.sizeDelta = visibleSize;
                UpdateExternalCropContentOffset(state);
                InvokeExternalCrop(state, state.CropInsets, true);
            }
            else
            {
                // v1 persisted XREAL compositor crops. On One Pro they can leave
                // one eye on the old source rectangle. Never restore that state.
                ClearLegacyExternalCrop(state);
                state.CropBaseSize = state.Rect.sizeDelta;
                if (state.CropContent != null)
                    state.CropContent.sizeDelta = state.CropBaseSize;
                state.Resize?.Invoke(state.Rect.sizeDelta, true);
            }
            LayoutExternalWindowHandles(state);
        }

        private void SaveExternalWindowLayouts()
        {
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
            {
                ExternalSpatialWindowState state = _externalSpatialWindows[i];
                if (!IsExternalWindowVisible(state)) continue;
                _activeExternalWindow = state;
                SaveWindowLayout(
                    DeckWindowKind.External,
                    state.Rect.position,
                    state.Rect.localScale.x);
                SaveExternalWindowSize(state);
                SaveExternalCrop(state);
            }
        }

        private void RecenterExternalWindows()
        {
            int visible = 0;
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
                if (IsExternalWindowVisible(_externalSpatialWindows[i])) visible++;
            if (visible == 0 || _camera == null) return;
            int slot = 0;
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
            {
                ExternalSpatialWindowState state = _externalSpatialWindows[i];
                if (!IsExternalWindowVisible(state)) continue;
                float x = (slot - (visible - 1) * .5f) * .58f;
                PlaceWindowAtCameraLocal(state.Rect, new Vector3(x, .05f, 1.12f));
                slot++;
            }
        }

        private void CloseAllExternalWindows()
        {
            ExternalSpatialWindowState[] states = _externalSpatialWindows.ToArray();
            for (int i = 0; i < states.Length; i++)
                if (IsExternalWindowVisible(states[i])) states[i].Close?.Invoke();
        }

        /// <summary>
        /// The fist power gesture is a reversible visual standby. It never calls
        /// Close on Android apps or browser windows, so playback, tabs, layout and
        /// process state survives and the second fist restores the exact set that
        /// was visible.
        /// </summary>
        private void SetWindowsSuspendedForGestureStandby(bool standby)
        {
            if (standby)
            {
                if (_gestureWindowSnapshotValid) return;
                _gestureWindowSnapshotValid = true;
                _gestureWorkspaceWasVisible = !_deckMinimized;
                _gestureSettingsWasVisible =
                    _settingsDeck != null && _settingsDeck.gameObject.activeSelf;
                _gestureDockWasVisible =
                    _windowDock != null && _windowDock.gameObject.activeSelf;
                _gestureVisibleWindows.Clear();
                for (int i = 0; i < _externalSpatialWindows.Count; i++)
                {
                    ExternalSpatialWindowState state = _externalSpatialWindows[i];
                    if (!IsExternalWindowVisible(state)) continue;
                    _gestureVisibleWindows.Add(state);
                    state.Rect.gameObject.SetActive(false);
                }
                if (_gestureWorkspaceWasVisible) SetDeckMinimized(true);
                if (_settingsDeck != null) _settingsDeck.gameObject.SetActive(false);
                if (_windowDock != null) _windowDock.gameObject.SetActive(false);
                return;
            }

            if (!_gestureWindowSnapshotValid) return;
            if (_gestureWorkspaceWasVisible) SetDeckMinimized(false);
            if (_settingsDeck != null)
                _settingsDeck.gameObject.SetActive(_gestureSettingsWasVisible);
            if (_windowDock != null)
                _windowDock.gameObject.SetActive(_gestureDockWasVisible);
            for (int i = 0; i < _gestureVisibleWindows.Count; i++)
            {
                ExternalSpatialWindowState state = _gestureVisibleWindows[i];
                if (state?.Rect == null || !_externalSpatialWindows.Contains(state))
                    continue;
                state.Rect.gameObject.SetActive(true);
                LayoutExternalWindowHandles(state);
            }
            _gestureVisibleWindows.Clear();
            _gestureWindowSnapshotValid = false;
        }

        private void BuildExternalAspectMenu(ExternalSpatialWindowState state)
        {
            var root = new GameObject("External aspect menu " + state.Id);
            root.transform.SetParent(state.Rect, false);
            state.AspectMenu = root.AddComponent<RectTransform>();
            state.AspectMenu.sizeDelta = new Vector2(82f, 172f);
            string[] labels = { "AUTO", "16:9", "21:9", "32:9" };
            float[] ratios = { 0f, 16f / 9f, 21f / 9f, 32f / 9f };
            for (int i = 0; i < labels.Length; i++)
            {
                int choice = i;
                Button button = MakeButton(
                    state.AspectMenu,
                    labels[i],
                    new Vector2(0f, 63f - i * 42f),
                    new Vector2(76f, 36f),
                    () =>
                    {
                        ApplyExternalAspectPreset(state, ratios[choice]);
                        if (state.AspectMenu != null)
                            state.AspectMenu.gameObject.SetActive(false);
                    });
                Transform depth = state.AspectMenu.Find("Button depth " + labels[i]);
                if (depth != null) depth.gameObject.SetActive(false);
                button.gameObject.AddComponent<Components.VisionGazeReveal>()
                    .Configure(.28f, 1f);
            }
            state.AspectMenu.gameObject.SetActive(false);
        }

        private void ToggleExternalAspectMenu(ExternalSpatialWindowState state)
        {
            if (state?.AspectMenu == null) return;
            state.AspectMenu.gameObject.SetActive(!state.AspectMenu.gameObject.activeSelf);
            state.HitGraphics.Clear();
            state.Rect.GetComponentsInChildren(true, state.HitGraphics);
        }

        private void ToggleExternalCropFrame(ExternalSpatialWindowState state)
        {
            if (state?.Crop == null || state.Rect == null) return;
            state.CropEditing = !state.CropEditing;
            SetCropFrameActive(state, state.CropEditing);
            LayoutExternalWindowHandles(state);
            state.HitGraphics.Clear();
            state.Rect.GetComponentsInChildren(true, state.HitGraphics);
            RevealExternalWindowAffordances(state, 8f);
            ShowGestureToast(
                state.CropEditing
                    ? "CROP // TIRE UN BORD, IMAGE INTACTE"
                    : "CROP // CADRE MASQUE",
                new Color(.55f, .90f, 1f));
        }

        private static void SetCropFrameActive(
            ExternalSpatialWindowState state,
            bool active)
        {
            if (state == null) return;
            if (state.CropLeft != null) state.CropLeft.gameObject.SetActive(active);
            if (state.CropRight != null) state.CropRight.gameObject.SetActive(active);
            if (state.CropBottom != null) state.CropBottom.gameObject.SetActive(active);
            if (state.CropTop != null) state.CropTop.gameObject.SetActive(active);
        }

        private static bool IsExternalCropMode(DeckManipulationMode mode) =>
            mode == DeckManipulationMode.CropLeft ||
            mode == DeckManipulationMode.CropRight ||
            mode == DeckManipulationMode.CropBottom ||
            mode == DeckManipulationMode.CropTop;

        private void BeginExternalCropManipulation(
            ExternalSpatialWindowState state)
        {
            if (state?.Rect == null || state.Crop == null) return;
            if (state.CropBaseSize.x < 1f || state.CropBaseSize.y < 1f)
                state.CropBaseSize = state.Rect.sizeDelta;
            state.CropStartInsets = state.CropInsets;
            state.CropTargetInsets = state.CropInsets;
            Vector3 localOffset = new Vector3(
                (state.CropInsets.x - state.CropInsets.y) * .5f,
                (state.CropInsets.z - state.CropInsets.w) * .5f,
                0f);
            state.CropBaseWorldPosition =
                state.Rect.position - state.Rect.TransformVector(localOffset);
            _deckManipulationTargetSize = state.Rect.sizeDelta;
        }

        private void UpdateExternalCropManipulation(
            ExternalSpatialWindowState state,
            DeckManipulationMode mode,
            Vector2 delta)
        {
            if (state?.Rect == null || state.Crop == null) return;
            Vector4 insets = state.CropStartInsets;
            float horizontal = delta.x * state.CropBaseSize.x * 1.65f;
            float vertical = delta.y * state.CropBaseSize.y * 1.65f;
            if (mode == DeckManipulationMode.CropLeft)
                insets.x += horizontal;
            else if (mode == DeckManipulationMode.CropRight)
                insets.y -= horizontal;
            else if (mode == DeckManipulationMode.CropBottom)
                insets.z -= vertical;
            else if (mode == DeckManipulationMode.CropTop)
                insets.w += vertical;

            const float minimumVisiblePixels = 140f;
            insets.x = Mathf.Clamp(
                insets.x, 0f,
                Mathf.Max(0f, state.CropBaseSize.x - insets.y - minimumVisiblePixels));
            insets.y = Mathf.Clamp(
                insets.y, 0f,
                Mathf.Max(0f, state.CropBaseSize.x - insets.x - minimumVisiblePixels));
            insets.z = Mathf.Clamp(
                insets.z, 0f,
                Mathf.Max(0f, state.CropBaseSize.y - insets.w - minimumVisiblePixels));
            insets.w = Mathf.Clamp(
                insets.w, 0f,
                Mathf.Max(0f, state.CropBaseSize.y - insets.z - minimumVisiblePixels));
            state.CropTargetInsets = insets;
            _deckManipulationTargetSize = new Vector2(
                state.CropBaseSize.x - insets.x - insets.y,
                state.CropBaseSize.y - insets.z - insets.w);
            Vector3 pixelOffset = new Vector3(
                (insets.x - insets.y) * .5f,
                (insets.z - insets.w) * .5f,
                0f);
            _deckManipulationTargetPosition =
                state.CropBaseWorldPosition + state.Rect.TransformVector(pixelOffset);
            _deckManipulationTargetRotation = _deckManipulationStartRotation;
            _deckManipulationTargetScale = _deckManipulationStartScale;
        }

        private void SmoothExternalCropManipulation(
            ExternalSpatialWindowState state,
            Vector2 size)
        {
            if (state?.Rect == null || state.Crop == null) return;
            float blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            state.CropInsets = Vector4.Lerp(
                state.CropInsets,
                state.CropTargetInsets,
                blend);
            state.Rect.sizeDelta = size;
            UpdateExternalCropContentOffset(state);
            LayoutExternalWindowHandles(state);
            InvokeExternalCrop(state, state.CropInsets, false);
        }

        private void CompleteExternalCropManipulation(
            ExternalSpatialWindowState state)
        {
            if (state?.Rect == null || state.Crop == null) return;
            state.CropInsets = state.CropTargetInsets;
            state.Rect.sizeDelta = _deckManipulationTargetSize;
            UpdateExternalCropContentOffset(state);
            LayoutExternalWindowHandles(state);
            InvokeExternalCrop(state, state.CropInsets, true);
            SaveExternalWindowSize(state);
            SaveExternalCrop(state);
        }

        private static void InvokeExternalCrop(
            ExternalSpatialWindowState state,
            Vector4 pixelInsets,
            bool final)
        {
            if (state?.Crop == null) return;
            float width = Mathf.Max(1f, state.CropBaseSize.x);
            float height = Mathf.Max(1f, state.CropBaseSize.y);
            state.Crop.Invoke(
                new Vector4(
                    pixelInsets.x / width,
                    pixelInsets.y / width,
                    pixelInsets.z / height,
                    pixelInsets.w / height),
                final);
        }

        private static void UpdateExternalCropContentOffset(
            ExternalSpatialWindowState state)
        {
            if (state?.CropContent == null) return;
            state.CropContent.sizeDelta = state.CropBaseSize;
            state.CropContent.anchoredPosition = new Vector2(
                -(state.CropInsets.x - state.CropInsets.y) * .5f,
                -(state.CropInsets.z - state.CropInsets.w) * .5f);
        }

        private void ResetExternalCrop(
            ExternalSpatialWindowState state,
            Vector2 newBaseSize,
            bool notifyProvider = true)
        {
            if (state == null) return;
            state.CropBaseSize = newBaseSize;
            state.CropInsets = Vector4.zero;
            state.CropStartInsets = Vector4.zero;
            state.CropTargetInsets = Vector4.zero;
            state.CropEditing = false;
            SetCropFrameActive(state, false);
            if (state.CropContent != null)
            {
                state.CropContent.sizeDelta = newBaseSize;
                state.CropContent.anchoredPosition = Vector2.zero;
            }
            if (notifyProvider) state.Crop?.Invoke(Vector4.zero, true);
            else state.Crop?.Invoke(Vector4.zero, false);
        }

        private void ApplyExternalAspectPreset(
            ExternalSpatialWindowState state,
            float ratio)
        {
            if (state?.Rect == null || state.Resize == null) return;
            Vector2 requested;
            if (ratio <= 0f)
            {
                requested = state.NormalSize;
                state.Rect.localScale = Vector3.one * state.NormalScale;
                state.AspectPresetActive = false;
            }
            else
            {
                state.AspectPresetActive = true;
                // Fixed cinematic presets are deterministic and physically grow
                // with wider formats. Content callbacks preserve their own source
                // aspect, so these dimensions never stretch pixels.
                // requested is the OUTER shell. The Android content lives inside
                // a transparent 36 px gutter on every edge, so presets must add
                // 72 px to the desired content geometry. Applying 3:4 or 16:9 to
                // the shell itself produced 708x968 and 1208x648 respectively.
                requested = ratio < 1f
                    ? new Vector2(792f, 1032f)       // 720x960 content, 3:4
                    : (ratio < 2f
                        ? new Vector2(1192f, 702f)   // 1120x630 content, 16:9
                        : (ratio < 3f
                            ? new Vector2(1542f, 702f) // 1470x630 content, 21:9
                            : new Vector2(2600f, 783f))); // 2528x711 content, 32:9
            }
            // Presets are deterministic physical formats. Retaining a previous
            // uniform-resize scale made Landscape stay huge after Portrait.
            state.Rect.localScale = Vector3.one * state.NormalScale;
            // Clear the provider crop in-memory, then let the single final resize
            // create one matching surface. Two back-to-back compositor rebuilds
            // are both wasteful and a source of per-eye races.
            ResetExternalCrop(state, requested, false);
            // Let the provider resize its native display first, then enforce the
            // same outer Unity footprint. Some Android providers update their
            // child surface synchronously and used to leave the shell/handles on
            // the previous landscape rect while content was already portrait.
            state.Resize.Invoke(requested, true);
            state.Rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, requested.x);
            state.Rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, requested.y);
            state.CropBaseSize = requested;
            if (state.CropContent != null)
            {
                state.CropContent.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal, requested.x);
                state.CropContent.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical, requested.y);
                state.CropContent.anchoredPosition = Vector2.zero;
            }
            LayoutExternalWindowHandles(state);
            state.HitGraphics.Clear();
            state.Rect.GetComponentsInChildren(true, state.HitGraphics);
            Canvas.ForceUpdateCanvases();
            Debug.Log(
                "[XR-EXTERNAL-ASPECT] " + state.Id +
                " requested=" + requested +
                " outer=" + state.Rect.rect.size +
                " crop=" + state.CropBaseSize);
            SaveExternalWindowSize(state);
            SaveExternalCrop(state);
            PlayerPrefs.SetFloat(
                state.LayoutPrefix + "scale",
                state.NormalScale);
            PlayerPrefs.Save();
            RevealExternalWindowAffordances(state);
        }

        private static string SanitizeLayoutId(string value)
        {
            char[] chars = value.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars);
        }

        private static bool IsPointInsideExternalHandle(
            Graphic graphic,
            Vector3 worldPoint)
        {
            if (graphic == null) return false;
            RectTransform rect = graphic.rectTransform;
            Vector3 local = rect.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.z) <= 45f && rect.rect.Contains(local);
        }

        private void LayoutExternalWindowHandles(ExternalSpatialWindowState state)
        {
            if (state?.Rect == null) return;
            Rect rect = state.Rect.rect;
            float bottom = rect.yMin + 17f;
            LayoutHandle(state.Move, new Vector2(-70f, bottom), new Vector2(104f, 5f));
            LayoutHandle(state.Depth, new Vector2(72f, bottom), new Vector2(52f, 34f));
            LayoutHandle(state.Tilt, new Vector2(136f, bottom), new Vector2(52f, 34f));
            LayoutHandle(state.FreeResize,
                new Vector2(rect.xMin + 96f, bottom), new Vector2(52f, 32f));
            LayoutHandle(state.ResizeLeft,
                new Vector2(rect.xMin + 18f, rect.yMin + 20f), Vector2.one * 48f);
            LayoutHandle(state.ResizeRight,
                new Vector2(rect.xMax - 18f, rect.yMin + 20f), Vector2.one * 48f);
            LayoutHandle(state.CropLeft,
                new Vector2(rect.xMin, 0f),
                new Vector2(10f, Mathf.Max(120f, rect.height - 12f)));
            LayoutHandle(state.CropRight,
                new Vector2(rect.xMax, 0f),
                new Vector2(10f, Mathf.Max(120f, rect.height - 12f)));
            LayoutHandle(state.CropBottom,
                new Vector2(0f, rect.yMin),
                new Vector2(Mathf.Max(140f, rect.width - 12f), 10f));
            LayoutHandle(state.CropTop,
                new Vector2(0f, rect.yMax),
                new Vector2(Mathf.Max(140f, rect.width - 12f), 10f));
            LayoutRect(state.CloseHandle,
                new Vector2(rect.xMax - 22f, rect.yMax + 28f), new Vector2(34f, 34f));
            LayoutButton(state.Block,
                new Vector2(rect.xMax - 72f, rect.yMax + 28f), new Vector2(48f, 34f));
            LayoutButton(state.Portrait,
                new Vector2(rect.xMin + 30f, rect.yMax + 28f), new Vector2(48f, 34f));
            LayoutButton(state.Landscape,
                new Vector2(rect.xMin + 84f, rect.yMax + 28f), new Vector2(48f, 34f));
            LayoutButton(state.Ultrawide,
                new Vector2(rect.xMin + 138f, rect.yMax + 28f), new Vector2(48f, 34f));
            if (state.AspectMenu != null)
            {
                state.AspectMenu.anchoredPosition =
                    new Vector2(rect.xMin + 138f, rect.yMax + 140f);
                state.AspectMenu.sizeDelta = new Vector2(82f, 172f);
            }
        }

        private void ApplyExternalWindowSize(Vector2 size, bool final)
        {
            ExternalSpatialWindowState state = _activeExternalWindow;
            if (state?.Rect == null || state.Resize == null) return;
            state.Rect.sizeDelta = size;
            if (final)
            {
                state.AspectPresetActive = false;
                ResetExternalCrop(state, size, false);
            }
            LayoutExternalWindowHandles(state);
            state.Resize.Invoke(size, final);
        }

        private void SaveExternalWindowSize(ExternalSpatialWindowState state)
        {
            if (state?.Rect == null || state.Resize == null) return;
            PlayerPrefs.SetFloat(state.LayoutPrefix + "width", state.Rect.sizeDelta.x);
            PlayerPrefs.SetFloat(state.LayoutPrefix + "height", state.Rect.sizeDelta.y);
        }

        private static void SaveExternalCrop(ExternalSpatialWindowState state)
        {
            if (state?.Crop == null) return;
            PlayerPrefs.SetInt(
                state.LayoutPrefix + "crop_schema", ExternalCropSchema);
            PlayerPrefs.SetFloat(
                state.LayoutPrefix + "crop_base_width", state.CropBaseSize.x);
            PlayerPrefs.SetFloat(
                state.LayoutPrefix + "crop_base_height", state.CropBaseSize.y);
            PlayerPrefs.SetFloat(
                state.LayoutPrefix + "crop_left", state.CropInsets.x);
            PlayerPrefs.SetFloat(
                state.LayoutPrefix + "crop_right", state.CropInsets.y);
            PlayerPrefs.SetFloat(
                state.LayoutPrefix + "crop_bottom", state.CropInsets.z);
            PlayerPrefs.SetFloat(
                state.LayoutPrefix + "crop_top", state.CropInsets.w);
        }

        private static void ClearLegacyExternalCrop(ExternalSpatialWindowState state)
        {
            if (state == null) return;
            PlayerPrefs.DeleteKey(state.LayoutPrefix + "crop_base_width");
            PlayerPrefs.DeleteKey(state.LayoutPrefix + "crop_base_height");
            PlayerPrefs.DeleteKey(state.LayoutPrefix + "crop_left");
            PlayerPrefs.DeleteKey(state.LayoutPrefix + "crop_right");
            PlayerPrefs.DeleteKey(state.LayoutPrefix + "crop_bottom");
            PlayerPrefs.DeleteKey(state.LayoutPrefix + "crop_top");
            PlayerPrefs.DeleteKey(state.LayoutPrefix + "crop_schema");
        }

        private void FollowExternalWindowsFromSavedLayout()
        {
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
            {
                ExternalSpatialWindowState state = _externalSpatialWindows[i];
                if (!IsExternalWindowVisible(state)) continue;
                FollowWindowFromSavedLayout(
                    state.Rect,
                    state.LayoutPrefix,
                    new Vector3(0f, .05f, 1.12f));
            }
        }
    }
}
