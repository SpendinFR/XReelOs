using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using TLab.WebView;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.XR.XREAL;
using MLOmega.XR.UI.Components;
using MLOmega.XR.SecureSurfaceSpike;
using MLOmega.XR.Reflex;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Experimental spatial-computing shell. This component is added only to
    /// XrealWorldLab.unity and never to the validated Atelier/Product scenes.
    /// The generic browser stays in WebView. Google, YouTube and Netflix use the
    /// validated Android-app spatial host; Netflix alone hands protected playback
    /// to the v34 cinema path.
    /// </summary>
    public sealed class WorldCreatorLabShell : MonoBehaviour
    {
        [SerializeField] private bool _osOnlyMode = true;

        [Serializable]
        private sealed class BrowserSessionEntry
        {
            public string title;
            public string url;
        }

        [Serializable]
        private sealed class ProtectedSessionEntry
        {
            public string packageName;
            public string uri;
            public string label;
        }

        [Serializable]
        private sealed class BrowserSessionState
        {
            public BrowserSessionEntry[] windows;
            public bool keyboardVisible;
            public bool workspaceVisible;
            public bool netflixVisible;
            public string protectedPackage;
            public string protectedUri;
            public string protectedLabel;
            public ProtectedSessionEntry[] protectedApps;
        }

        private sealed class ProtectedHostEntry
        {
            public XrealSecureSurfaceSpike Host;
            public string Package;
            public string Uri;
            public string Label;
            public bool Commercial;
        }

        private sealed class DockReorderEntry
        {
            public string Id;
            public Button Button;
            public int Slot;
            public readonly List<RectTransform> Parts = new List<RectTransform>();
        }

        private enum KeyboardTarget
        {
            WebContent,
            Address,
            ProtectedApplication,
        }

        private static readonly Color Glass = new Color(.12f, .13f, .16f, .90f);
        private static readonly Color GlassHover = new Color(.24f, .26f, .31f, .96f);
        private static readonly Color Ink = new Color(.96f, .97f, 1f, .98f);
        private static readonly Color Muted = new Color(.72f, .75f, .82f, .92f);

        private readonly List<XrLabBrowserWindow> _windows =
            new List<XrLabBrowserWindow>();
        private Camera _camera;
        private WorldCreatorController _creator;
        private RectTransform _dock;
        private XrLabBrowserWindow _activeBrowser;
        private Canvas _keyboardCanvas;
        private RectTransform _keyboardRect;
        private TextMeshProUGUI _keyboardPreview;
        private KeyboardTarget _keyboardTarget = KeyboardTarget.WebContent;
        private string _addressBuffer = string.Empty;
        private bool _uppercase;
        private XrLabFirstPersonRecorder _recorder;
        private readonly List<ProtectedHostEntry> _protectedHosts =
            new List<ProtectedHostEntry>();
        private readonly Dictionary<XrealSecureSurfaceSpike, string>
            _protectedInputBuffers =
                new Dictionary<XrealSecureSurfaceSpike, string>();
        private ProtectedHostEntry _activeProtectedHost;
        private int _genericWindowSerial;
        private GameObject _resumeOffer;
        private RectTransform _resumeOfferRect;
        private bool _cleanExitSaved;
        private readonly List<DockReorderEntry> _dockEntries =
            new List<DockReorderEntry>();
        private bool _dockReorderMode;
        private Image _dockDepthBar;
        private DockReorderEntry _draggedDockEntry;
        private Vector2 _dockDragStartLocal;
        private readonly Dictionary<RectTransform, Vector2> _dockDragPartStarts =
            new Dictionary<RectTransform, Vector2>();
        private float _dockDepthDragStartX;
        private float _dockDepthDragStart;
        private float _frameHealthStartedAt;
        private float _frameHealthMaxDelta;
        private int _frameHealthFrames;
        private int _frameHealthOver22Ms;
        private int _frameHealthOver33Ms;
        private const string SessionStatePreference =
            "mlomega.xr.lab.browser_session.v1";
        private const string DockOrderPreference =
            "mlomega.xr.lab.dock_order.v1";

        private IEnumerator Start()
        {
            XrealSecureSurfaceSpike.CinemaReturnCompleted -=
                RestartGesturesAfterCinemaReturn;
            XrealSecureSurfaceSpike.CinemaReturnCompleted +=
                RestartGesturesAfterCinemaReturn;
            ConfigureFramePacing();
            // Reap interrupted Shizuku displays and re-assert DeX-off before any
            // Android app window is opened. This is intentionally Lab-only.
            XrLabAndroidRuntimeBridge.PrepareRuntime();
            // Lab-only inference copy. MediaPipe resizes again for its model;
            // 512 px preserves landmark detail while removing most of the
            // measured 768 px YUV/RGBA CPU cost during Moonlight.
            FindFirstObjectByType<GestureBridge>()?.SetInferenceLongEdge(512);
            DisableSensitiveSdkTrackingPopup();
            _camera = Camera.main;
            if (_camera == null)
                _camera = FindFirstObjectByType<Camera>();
            if (_camera == null)
            {
                Debug.LogError("[XrLab] XR camera unavailable.");
                yield break;
            }
            _creator = FindFirstObjectByType<WorldCreatorController>();
            if (_creator == null)
            {
                Debug.LogError("[XrLab] validated Atelier controller unavailable.");
                yield break;
            }

            if (FindFirstObjectByType<BrowserManager>() == null)
                gameObject.AddComponent<BrowserManager>();
            _recorder = gameObject.AddComponent<XrLabFirstPersonRecorder>();
            _recorder.SetMicrophoneEnabled(false);
            _recorder.StateChanged += OnRecordingStateChanged;

            // WorldCreatorController constructs the proven dock in Start().
            // Wait for it instead of changing its stable construction path.
            for (int frame = 0; frame < 120 && _dock == null; frame++)
            {
                GameObject dockObject = GameObject.Find("Atelier Vision Dock");
                if (dockObject != null)
                    _dock = dockObject.GetComponent<RectTransform>();
                if (_dock == null) yield return null;
            }
            if (_dock == null)
            {
                Debug.LogError("[XrLab] validated Atelier dock unavailable.");
                yield break;
            }

            ExtendDock();
            BuildDockReorderSystem();
            BuildKeyboard();
            _creator.RegisterLabSettingsActions(
                SaveSessionAndQuit,
                ToggleKeyboardFromSettings,
                () => _keyboardCanvas != null && _keyboardCanvas.gameObject.activeSelf,
                enabled => Debug.Log("[XrLab] reserved VR mode=" + enabled),
                _recorder.Toggle,
                () => _recorder != null && _recorder.IsRecording,
                () => _recorder != null && _recorder.IsBusy,
                () => _recorder == null ? 0f : _recorder.ElapsedSeconds,
                () => _recorder == null ? string.Empty : _recorder.UiStatus,
                ToggleDockReorderMode,
                () => _dockReorderMode);
            OfferSavedSessionIfAvailable();
            RaiseInteractionCursor();
            Debug.Log(
                "[XrLab] spatial browser ready; Android foreground remains Unity.");
        }

        private static void DisableSensitiveSdkTrackingPopup()
        {
            // Keep our debounced tracking gauge, remove only XREAL's intrusive
            // one-edge "look around" dialog which flashes on brief relocalisation.
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            int disabled = 0;
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null ||
                    behaviour.GetType().Name != "XREALSlamStateNotification")
                    continue;
                behaviour.enabled = false;
                disabled++;
            }
            Debug.Log("[XrLab] sensitive XREAL tracking popups disabled=" + disabled);
        }

        private static void ConfigureFramePacing()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android treats targetFrameRate=-1 as its platform default, which
            // hardware receipts measured falling to ~30 fps while the One Pro
            // compositor remained at 60 Hz. Keep Unity and XREAL on one explicit
            // cadence; vSync stays disabled because the XR compositor owns scanout.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            try
            {
                XREALPlugin.SetTargetFrameRate(60);
                Debug.Log(
                    "[XR-FRAME-PACING] unity=" + Application.targetFrameRate +
                    " xreal=" + XREALPlugin.GetTargetFrameRate());
            }
            catch (Exception error)
            {
                Debug.LogWarning(
                    "[XR-FRAME-PACING] XREAL target unavailable: " +
                    error.Message);
            }
#endif
        }

        private void Update()
        {
            // Lightweight device receipt: distinguish Unity pacing hitches from
            // native XREAL/Android composition artefacts without enabling the
            // heavyweight Unity profiler on the S24.
            float now = Time.unscaledTime;
            if (_frameHealthStartedAt <= 0f) _frameHealthStartedAt = now;
            float delta = Time.unscaledDeltaTime;
            _frameHealthFrames++;
            _frameHealthMaxDelta = Mathf.Max(_frameHealthMaxDelta, delta);
            if (delta > .022f) _frameHealthOver22Ms++;
            if (delta > .033f) _frameHealthOver33Ms++;
            float elapsed = now - _frameHealthStartedAt;
            if (elapsed < 5f) return;
            Debug.Log(
                "[XR-FRAME-HEALTH] fps=" +
                (_frameHealthFrames / Mathf.Max(.001f, elapsed)).ToString("F1") +
                " over22=" + _frameHealthOver22Ms +
                " over33=" + _frameHealthOver33Ms +
                " maxMs=" + (_frameHealthMaxDelta * 1000f).ToString("F1") +
                " androidWindows=" + _protectedHosts.Count +
                " webWindows=" + _windows.Count);
            _frameHealthStartedAt = now;
            _frameHealthMaxDelta = 0f;
            _frameHealthFrames = 0;
            _frameHealthOver22Ms = 0;
            _frameHealthOver33Ms = 0;
        }

        private static void RaiseInteractionCursor()
        {
            // The browser is an opaque world-space RawImage. Keep the already
            // validated eye ray/ring above that Lab-only surface without
            // changing its geometry, aiming or Product/Atelier configuration.
            Renderer[] renderers = FindObjectsByType<Renderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                string objectName = renderer.gameObject.name;
                if (
                    objectName != "XREAL Hand Ray" &&
                    objectName != "XREAL Vision Cursor Ring" &&
                    objectName != "XREAL Vision Cursor Dot")
                    continue;
                renderer.sortingOrder = 500;
            }
        }

        private void ExtendDock()
        {
            // Explicit 3-4-3 visionOS grid requested for the ten Lab apps.
            _dock.sizeDelta = new Vector2(720f, 520f);
            Button[] existing = _dock.GetComponentsInChildren<Button>(true);
            Array.Sort(existing, (left, right) =>
                ((RectTransform)left.transform).anchoredPosition.x.CompareTo(
                    ((RectTransform)right.transform).anchoredPosition.x));
            if (_osOnlyMode && existing.Length >= 1)
            {
                MoveExistingDockGroup(existing[0], new Vector2(-150f, 150f));
                RestyleExistingDockButton(existing[0], false);
            }
            else if (existing.Length >= 2)
            {
                MoveExistingDockGroup(existing[0], new Vector2(-150f, 150f));
                MoveExistingDockGroup(existing[1], new Vector2(0f, 150f));
                RestyleExistingDockButton(existing[0], true);
                RestyleExistingDockButton(existing[1], false);
            }

            MakeDockButton(
                "Moonlight",
                _osOnlyMode ? new Vector2(0f, 150f) : new Vector2(150f, 150f),
                "com.limelight",
                "◎",
                () => OpenOrFocusProtectedApplication(
                    "com.limelight",
                    string.Empty,
                    "PC Moonlight"));
            if (_osOnlyMode)
            {
                MakeDockButton(
                    "Files",
                    new Vector2(150f, 150f),
                    "com.sec.android.app.myfiles",
                    "▣",
                    () => OpenOrFocusProtectedApplication(
                        "com.sec.android.app.myfiles",
                        string.Empty,
                        "Files"));
            }
            MakeDockButton(
                "Google",
                new Vector2(-225f, 0f),
                "com.android.chrome",
                "G",
                () => OpenOrFocusProtectedApplication(
                    "com.android.chrome",
                    "https://www.google.com",
                    "Google"));
            MakeDockButton(
                "YouTube",
                new Vector2(-75f, 0f),
                "com.google.android.youtube",
                "▶",
                () => OpenOrFocusProtectedApplication(
                    "com.google.android.youtube",
                    "https://www.youtube.com/",
                    "YouTube"));
            MakeDockButton(
                "Netflix",
                new Vector2(75f, 0f),
                "com.netflix.mediaclient",
                "N",
                () => OpenOrFocusProtectedApplication(
                    "com.netflix.mediaclient",
                    "https://www.netflix.com/browse",
                    "Netflix"));
            MakeDockButton(
                "Spotify",
                new Vector2(225f, 0f),
                "com.spotify.music",
                "S",
                () => OpenOrFocusProtectedApplication(
                    "com.spotify.music",
                    string.Empty,
                    "Spotify"));
            MakeDockButton(
                "Reddit",
                new Vector2(-150f, -150f),
                "com.reddit.frontpage",
                "r/",
                () => OpenOrFocusProtectedApplication(
                    "com.reddit.frontpage",
                    "https://www.reddit.com/",
                    "Reddit"));
            MakeDockButton(
                "Prime Video",
                new Vector2(0f, -150f),
                "com.amazon.avod.thirdpartyclient",
                "P",
                () => OpenOrFocusProtectedApplication(
                    "com.amazon.avod.thirdpartyclient",
                    "https://www.primevideo.com/",
                    "Prime Video"));
            MakeDockButton(
                "Clavier",
                new Vector2(150f, -150f),
                "com.samsung.android.honeyboard",
                "⌨",
                () => ShowKeyboard(KeyboardTarget.WebContent));
        }

        private void MoveExistingDockGroup(Button button, Vector2 newPosition)
        {
            var buttonRect = button.transform as RectTransform;
            if (buttonRect == null) return;
            Vector2 oldPosition = buttonRect.anchoredPosition;
            Vector2 delta = newPosition - oldPosition;
            for (int index = 0; index < _dock.childCount; index++)
            {
                var child = _dock.GetChild(index) as RectTransform;
                if (child == null ||
                    Mathf.Abs(child.anchoredPosition.x - oldPosition.x) > 2f)
                    continue;
                child.anchoredPosition += delta;
            }
        }

        private void RestyleExistingDockButton(Button button, bool workspace)
        {
            if (button == null) return;
            Image surface = button.GetComponent<Image>();
            Color baseColor = workspace ? Color.white : Color.clear;
            if (surface != null) surface.color = baseColor;
            Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == surface) continue;
                graphics[i].color = workspace
                    ? new Color(.035f, .04f, .05f, 1f)
                    : Ink;
            }
            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect != null)
            {
                for (int i = 0; i < _dock.childCount; i++)
                {
                    Image image = _dock.GetChild(i).GetComponent<Image>();
                    if (image == null ||
                        !image.gameObject.name.StartsWith(
                            "Dock rim ", StringComparison.Ordinal) ||
                        Vector2.Distance(
                            image.rectTransform.anchoredPosition,
                            buttonRect.anchoredPosition) > 2f)
                        continue;
                    image.color = Color.clear;
                }
            }
            VisionSpatialControlFeedback feedback =
                button.GetComponent<VisionSpatialControlFeedback>();
            if (feedback != null && surface != null)
                feedback.Configure(
                    surface,
                    baseColor,
                    workspace
                        ? new Color(.86f, .89f, .94f, 1f)
                        : new Color(.34f, .36f, .42f, .24f),
                    new Color(.78f, .82f, .92f, .96f),
                    workspace ? new Color(.035f, .04f, .05f, 1f) : Ink);
        }

        private void MakeDockButton(
            string label,
            Vector2 position,
            string androidPackage,
            string fallback,
            UnityEngine.Events.UnityAction action)
        {
            Image rim = MakeImage(
                _dock,
                "Lab dock rim " + label,
                position,
                new Vector2(112f, 112f),
                Color.clear,
                true);
            rim.raycastTarget = false;
            Image surface = MakeImage(
                _dock,
                "Lab dock orb " + label,
                position,
                new Vector2(98f, 98f),
                Color.clear,
                true);
            Button button = MakeClickable(surface, action, new Vector3(112f, 112f, 18f));
            AddFeedback(
                button,
                surface,
                Color.clear,
                new Color(.30f, .33f, .40f, .34f));
            ApplyInstalledIcon(surface.transform, androidPackage, fallback);
            MakeText(
                _dock,
                label,
                position + new Vector2(0f, -68f),
                new Vector2(135f, 24f),
                13f,
                Muted,
                FontStyles.Bold);
        }

        private void ApplyInstalledIcon(
            Transform parent,
            string packageName,
            string fallback)
        {
            // The validated Atelier icons are line-built children. In the Lab,
            // replace (do not stack over) that artwork with the installed app's
            // authentic Android icon.
            for (int index = parent.childCount - 1; index >= 0; index--)
                parent.GetChild(index).gameObject.SetActive(false);
            Sprite icon = XrLabAndroidIconLoader.TryLoad(packageName);
            if (icon != null)
            {
                Image image = MakeImage(
                    parent,
                    "Official app icon " + packageName,
                    Vector2.zero,
                    new Vector2(84f, 84f),
                    Color.white,
                    false);
                image.sprite = icon;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                image.raycastTarget = false;
                return;
            }
            MakeText(
                parent,
                fallback,
                Vector2.zero,
                new Vector2(66f, 66f),
                36f,
                Ink,
                FontStyles.Bold);
        }

        private static readonly Vector2[] DockSlotPositions =
        {
            new Vector2(-150f, 150f),
            new Vector2(0f, 150f),
            new Vector2(150f, 150f),
            new Vector2(-225f, 0f),
            new Vector2(-75f, 0f),
            new Vector2(75f, 0f),
            new Vector2(225f, 0f),
            new Vector2(-150f, -150f),
            new Vector2(0f, -150f),
            new Vector2(150f, -150f),
        };

        private void BuildDockReorderSystem()
        {
            if (_dock == null || _dockEntries.Count > 0) return;
            Button[] buttons = _dock.GetComponentsInChildren<Button>(true);
            Array.Sort(buttons, (left, right) =>
            {
                Vector2 a = ((RectTransform)left.transform).anchoredPosition;
                Vector2 b = ((RectTransform)right.transform).anchoredPosition;
                int row = -a.y.CompareTo(b.y);
                return row != 0 ? row : a.x.CompareTo(b.x);
            });
            for (int i = 0; i < buttons.Length && i < DockSlotPositions.Length; i++)
            {
                string id = buttons[i].gameObject.name;
                _dockEntries.Add(new DockReorderEntry
                {
                    Id = id,
                    Button = buttons[i],
                    Slot = i,
                });
                XrLabDockReorderItem item =
                    buttons[i].gameObject.AddComponent<XrLabDockReorderItem>();
                item.Configure(this, id);
            }

            // Rims and labels are siblings of the clickable orb. Assign every
            // direct dock child to the nearest orb so they travel as one icon.
            for (int childIndex = 0; childIndex < _dock.childCount; childIndex++)
            {
                RectTransform child = _dock.GetChild(childIndex) as RectTransform;
                if (child == null) continue;
                DockReorderEntry nearest = null;
                float nearestDistance = float.MaxValue;
                for (int i = 0; i < _dockEntries.Count; i++)
                {
                    RectTransform buttonRect =
                        _dockEntries[i].Button.transform as RectTransform;
                    float distance = Vector2.Distance(
                        child.anchoredPosition, buttonRect.anchoredPosition);
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearest = _dockEntries[i];
                }
                if (nearest != null && nearestDistance <= 82f)
                    nearest.Parts.Add(child);
            }

            RestoreDockOrder();
            _dockDepthBar = MakeImage(
                _dock,
                "Dock depth edit bar",
                new Vector2(0f, -246f),
                new Vector2(180f, 10f),
                new Color(.78f, .80f, .86f, .76f),
                false);
            _dockDepthBar.raycastTarget = true;
            var collider = _dockDepthBar.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(220f, 40f, 18f);
            _dockDepthBar.gameObject.AddComponent<XrLabDockDepthHandle>()
                .Configure(this);
            _dockDepthBar.gameObject.SetActive(false);
            _creator?.RefreshWindowDockHitTargets();
        }

        private void RestoreDockOrder()
        {
            string saved = PlayerPrefs.GetString(DockOrderPreference, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                string[] ids = saved.Split('|');
                for (int slot = 0; slot < ids.Length; slot++)
                {
                    DockReorderEntry entry = _dockEntries.Find(
                        candidate => candidate.Id == ids[slot]);
                    if (entry != null) entry.Slot = slot;
                }
            }
            LayoutDockEntries();
        }

        private void ToggleDockReorderMode()
        {
            if (_dockEntries.Count == 0) BuildDockReorderSystem();
            _dockReorderMode = !_dockReorderMode;
            if (_dock == null) return;
            if (!_dock.gameObject.activeSelf)
                _creator?.OpenWindowDockFromTwoPalms();
            foreach (DockReorderEntry entry in _dockEntries)
                if (entry.Button != null)
                    // Keep the Button raycastable. XrLabDockReorderItem claims
                    // pointer-down and clears eligibleForClick while editing,
                    // so apps cannot launch but the hand cursor/drag still work.
                    entry.Button.interactable = true;
            if (_dockDepthBar != null)
                _dockDepthBar.gameObject.SetActive(_dockReorderMode);
            if (!_dockReorderMode)
            {
                LayoutDockEntries();
                SaveDockOrder();
            }
            Debug.Log("[XrLab] dock reorder=" + _dockReorderMode);
        }

        internal bool BeginDockItemDrag(string id, Vector3 world)
        {
            if (!_dockReorderMode || _dock == null) return false;
            _draggedDockEntry = _dockEntries.Find(entry => entry.Id == id);
            if (_draggedDockEntry == null) return false;
            _dockDragStartLocal = _dock.InverseTransformPoint(world);
            _dockDragPartStarts.Clear();
            foreach (RectTransform part in _draggedDockEntry.Parts)
                if (part != null)
                    _dockDragPartStarts[part] = part.anchoredPosition;
            return true;
        }

        internal void DragDockItem(Vector3 world, PointerEventData eventData)
        {
            if (_draggedDockEntry == null || _dock == null) return;
            Vector2 current = _dock.InverseTransformPoint(world);
            Vector2 delta = current - _dockDragStartLocal;
            foreach (KeyValuePair<RectTransform, Vector2> pair in _dockDragPartStarts)
                if (pair.Key != null)
                    pair.Key.anchoredPosition = pair.Value + delta;
            if (eventData != null) eventData.eligibleForClick = false;
        }

        internal void EndDockItemDrag(Vector3 world)
        {
            if (_draggedDockEntry == null || _dock == null) return;
            Vector2 local = _dock.InverseTransformPoint(world);
            int nearestSlot = 0;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < DockSlotPositions.Length; i++)
            {
                float distance = Vector2.Distance(local, DockSlotPositions[i]);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestSlot = i;
            }
            DockReorderEntry displaced = _dockEntries.Find(
                entry => entry != _draggedDockEntry && entry.Slot == nearestSlot);
            int previousSlot = _draggedDockEntry.Slot;
            _draggedDockEntry.Slot = nearestSlot;
            if (displaced != null) displaced.Slot = previousSlot;
            _draggedDockEntry = null;
            _dockDragPartStarts.Clear();
            LayoutDockEntries();
            SaveDockOrder();
        }

        private void LayoutDockEntries()
        {
            foreach (DockReorderEntry entry in _dockEntries)
            {
                if (entry.Button == null) continue;
                Vector2 target = DockSlotPositions[Mathf.Clamp(
                    entry.Slot, 0, DockSlotPositions.Length - 1)];
                RectTransform buttonRect = entry.Button.transform as RectTransform;
                Vector2 delta = target - buttonRect.anchoredPosition;
                foreach (RectTransform part in entry.Parts)
                    if (part != null) part.anchoredPosition += delta;
            }
        }

        private void SaveDockOrder()
        {
            _dockEntries.Sort((left, right) => left.Slot.CompareTo(right.Slot));
            PlayerPrefs.SetString(DockOrderPreference,
                string.Join("|", _dockEntries.ConvertAll(entry => entry.Id)));
            PlayerPrefs.Save();
        }

        internal void BeginDockDepthDrag(Vector3 world)
        {
            if (!_dockReorderMode || _dock == null) return;
            _dockDepthDragStartX = _dock.InverseTransformPoint(world).x;
            _dockDepthDragStart = _creator?.WindowDockDepth ?? 1.08f;
        }

        internal void DragDockDepth(Vector3 world, PointerEventData eventData)
        {
            if (!_dockReorderMode || _dock == null || _creator == null) return;
            float x = _dock.InverseTransformPoint(world).x;
            _creator.SetWindowDockDepth(
                _dockDepthDragStart + (x - _dockDepthDragStartX) * .0018f);
            if (eventData != null) eventData.eligibleForClick = false;
        }

        private void OpenBrowser(string title, string url)
        {
            // Three simultaneous web surfaces are a deliberate S24 thermal/RAM
            // ceiling. Closing the oldest is explicit and deterministic.
            if (_windows.Count >= 3)
                CloseWindow(_windows[0]);
            XrLabBrowserWindow window = XrLabBrowserWindow.Create(
                this,
                _creator,
                _camera,
                title,
                url);
            _windows.Add(window);
            PlaceNewBrowserWindow(window, _windows.Count - 1);
            window.RegisterSpatialWindow();
            SetActiveBrowser(window);
            _creator.DismissWindowDock();
        }

        private void OpenOrFocusProtectedApplication(
            string packageName,
            string uri,
            string label)
        {
            ProtectedHostEntry existing = FindProtectedHost(packageName);
            if (existing?.Host != null && existing.Host.IsWindowVisible)
            {
                SetActiveProtectedHost(existing);
                _creator?.DismissWindowDock();
                return;
            }

            bool commercial = XrealSecureSurfaceSpike.IsCommercialPackage(packageName);
            if (commercial)
            {
                ProtectedHostEntry previousCinema = _protectedHosts.Find(
                    entry => entry.Commercial);
                previousCinema?.Host?.CloseHostedWindow();
            }
            else
            {
                List<ProtectedHostEntry> ordinary = _protectedHosts.FindAll(
                    entry => !entry.Commercial);
                if (ordinary.Count >= 3)
                    ordinary[0].Host?.CloseHostedWindow();
            }

            int initialSlot = _protectedHosts.Count;
            var entry = new ProtectedHostEntry
            {
                Host = gameObject.AddComponent<XrealSecureSurfaceSpike>(),
                Package = packageName,
                Uri = uri ?? string.Empty,
                Label = label,
                Commercial = commercial,
            };
            _protectedHosts.Add(entry);
            entry.Host.ConfigureLabApplication(
                entry.Package,
                entry.Uri,
                entry.Label,
                initialSlot);
            entry.Host.Closed += OnProtectedApplicationClosed;
            entry.Host.Focused += OnProtectedApplicationFocused;
            _activeProtectedHost = entry;
            _creator?.DismissWindowDock();
            RefreshKeyboardPreview();
            Debug.Log("[XrLab] " + label +
                      " independent spatial host requested; count=" +
                      _protectedHosts.Count);
        }

        private ProtectedHostEntry FindProtectedHost(string packageName) =>
            _protectedHosts.Find(entry =>
                string.Equals(entry.Package, packageName, StringComparison.Ordinal));

        private ProtectedHostEntry EntryFor(XrealSecureSurfaceSpike host) =>
            _protectedHosts.Find(entry => entry.Host == host);

        private void SetActiveProtectedHost(ProtectedHostEntry entry)
        {
            if (entry?.Host == null) return;
            _activeProtectedHost = entry;
            _activeBrowser = null;
            _keyboardTarget = KeyboardTarget.ProtectedApplication;
            _creator?.FocusExternalSpatialWindow(entry.Host.WindowRect);
            RefreshKeyboardPreview();
        }

        private void OnProtectedApplicationFocused(XrealSecureSurfaceSpike host) =>
            SetActiveProtectedHost(EntryFor(host));

        private void OnProtectedApplicationClosed(XrealSecureSurfaceSpike host)
        {
            ProtectedHostEntry entry = EntryFor(host);
            if (entry == null) return;
            host.Closed -= OnProtectedApplicationClosed;
            host.Focused -= OnProtectedApplicationFocused;
            _protectedInputBuffers.Remove(host);
            _protectedHosts.Remove(entry);
            if (_activeProtectedHost == entry)
                _activeProtectedHost = _protectedHosts.Count == 0
                    ? null
                    : _protectedHosts[_protectedHosts.Count - 1];
            RefreshKeyboardPreview();
            Debug.Log("[XrLab] " + entry.Label +
                      " host closed; remaining=" + _protectedHosts.Count);
        }

        private void PlaceNewBrowserWindow(XrLabBrowserWindow window, int slot)
        {
            if (window == null || _camera == null) return;
            Vector3 forward = _camera.transform.forward.normalized;
            float[] offsets = { 0f, .62f, -.62f };
            float x = offsets[Mathf.Clamp(slot, 0, offsets.Length - 1)];
            Vector3 position =
                _camera.transform.position +
                forward * (1.18f + Mathf.Abs(x) * .10f) +
                _camera.transform.right * x +
                Vector3.up * .03f;
            window.SetSpatialPose(
                position,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        internal void SetActiveBrowser(XrLabBrowserWindow window)
        {
            if (window == null) return;
            _activeBrowser = window;
            _activeProtectedHost = null;
            _keyboardTarget = KeyboardTarget.WebContent;
            for (int i = 0; i < _windows.Count; i++)
                _windows[i].SetFocused(_windows[i] == window);
            _creator?.FocusExternalSpatialWindow(window.WindowRect);
            window.transform.SetAsLastSibling();
            RefreshKeyboardPreview();
        }

        internal void BeginAddressEntry(XrLabBrowserWindow window)
        {
            SetActiveBrowser(window);
            _keyboardTarget = KeyboardTarget.Address;
            _addressBuffer = window.CurrentUrl ?? string.Empty;
            ShowKeyboard(KeyboardTarget.Address);
        }

        internal void BeginWebContentEntry(XrLabBrowserWindow window)
        {
            SetActiveBrowser(window);
            ShowKeyboard(KeyboardTarget.WebContent);
        }

        private void ToggleKeyboardFromSettings()
        {
            if (_keyboardCanvas == null) return;
            if (_keyboardCanvas.gameObject.activeSelf)
            {
                _keyboardCanvas.gameObject.SetActive(false);
                return;
            }
            ShowKeyboard(KeyboardTarget.WebContent);
        }

        private void SaveSessionAndQuit()
        {
            if (_recorder != null && (_recorder.IsRecording || _recorder.IsBusy))
            {
                StartCoroutine(StopRecordingThenQuit());
                return;
            }
            SaveSessionState();
            _cleanExitSaved = true;
            ReleaseAllProtectedApplications();
            Application.Quit();
        }

        private void OnRecordingStateChanged()
        {
            _creator?.RefreshLabSettingsActions();
        }

        private IEnumerator StopRecordingThenQuit()
        {
            _recorder?.RequestStop();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (
                _recorder != null &&
                (_recorder.IsRecording || _recorder.IsBusy) &&
                Time.realtimeSinceStartup < deadline)
                yield return null;
            SaveSessionState();
            _cleanExitSaved = true;
            ReleaseAllProtectedApplications();
            Application.Quit();
        }

        private void OnApplicationQuit()
        {
            XrealSecureSurfaceSpike.CinemaReturnCompleted -=
                RestartGesturesAfterCinemaReturn;
            if (!_cleanExitSaved) SaveSessionState();
            ReleaseAllProtectedApplications();
        }

        private void OnApplicationPause(bool paused)
        {
            // Android may reclaim the process without calling OnApplicationQuit.
            // Persist the shell as soon as it loses foreground; protected apps
            // are only force-stopped by the explicit quit/close paths.
            if (paused && !_cleanExitSaved) SaveSessionState();
        }

        private void RestartGesturesAfterCinemaReturn()
        {
            GestureBridge gestures = FindFirstObjectByType<GestureBridge>();
            gestures?.RestartAfterExternalCameraResume();
        }

        private void ReleaseAllProtectedApplications()
        {
            ProtectedHostEntry[] entries = _protectedHosts.ToArray();
            for (int i = 0; i < entries.Length; i++)
                entries[i]?.Host?.ReleaseHostedApplication();
        }

        private void SaveSessionState()
        {
            _creator?.SaveLabWindowLayoutsForExit();
            var entries = new BrowserSessionEntry[_windows.Count];
            for (int i = 0; i < _windows.Count; i++)
            {
                entries[i] = new BrowserSessionEntry
                {
                    title = _windows[i].Title,
                    url = _windows[i].CurrentUrl,
                };
            }
            var protectedEntries = new ProtectedSessionEntry[_protectedHosts.Count];
            for (int i = 0; i < _protectedHosts.Count; i++)
            {
                protectedEntries[i] = new ProtectedSessionEntry
                {
                    packageName = _protectedHosts[i].Package,
                    uri = _protectedHosts[i].Uri,
                    label = _protectedHosts[i].Label,
                };
            }
            ProtectedHostEntry activeProtected = _activeProtectedHost ??
                (_protectedHosts.Count == 0 ? null : _protectedHosts[0]);
            var state = new BrowserSessionState
            {
                windows = entries,
                keyboardVisible =
                    _keyboardCanvas != null && _keyboardCanvas.gameObject.activeSelf,
                workspaceVisible = _creator?.IsLabWorkspaceVisible == true,
                netflixVisible = _protectedHosts.Exists(entry =>
                    entry.Package == "com.netflix.mediaclient" &&
                    entry.Host != null && entry.Host.IsWindowVisible),
                protectedPackage = activeProtected?.Package ?? string.Empty,
                protectedUri = activeProtected?.Uri ?? string.Empty,
                protectedLabel = activeProtected?.Label ?? string.Empty,
                protectedApps = protectedEntries,
            };
            if (entries.Length == 0 && !state.keyboardVisible &&
                !state.workspaceVisible && !state.netflixVisible &&
                protectedEntries.Length == 0)
                PlayerPrefs.DeleteKey(SessionStatePreference);
            else
                PlayerPrefs.SetString(SessionStatePreference, JsonUtility.ToJson(state));
            PlayerPrefs.Save();
            Debug.Log("[XrLab] clean session saved; windows=" + entries.Length);
        }

        private void OfferSavedSessionIfAvailable()
        {
            if (_dock == null || !PlayerPrefs.HasKey(SessionStatePreference)) return;
            string json = PlayerPrefs.GetString(SessionStatePreference, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return;
            var state = JsonUtility.FromJson<BrowserSessionState>(json);
            if (state == null ||
                ((state.windows == null || state.windows.Length == 0) &&
                 !state.keyboardVisible && !state.workspaceVisible &&
                 !state.netflixVisible &&
                  string.IsNullOrWhiteSpace(state.protectedPackage) &&
                  (state.protectedApps == null || state.protectedApps.Length == 0)))
                return;

            _resumeOffer = new GameObject("XR Lab saved-session choice");
            Canvas canvas = _resumeOffer.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _camera;
            canvas.sortingOrder = 240;
            _resumeOffer.AddComponent<GraphicRaycaster>();
            RectTransform root = _resumeOffer.GetComponent<RectTransform>();
            _resumeOfferRect = root;
            root.sizeDelta = new Vector2(520f, 184f);
            root.localScale = Vector3.one * .00068f;
            Vector3 forward = _camera.transform.forward.normalized;
            root.SetPositionAndRotation(
                _camera.transform.position + forward * .94f,
                Quaternion.LookRotation(forward, Vector3.up));
            Image panel = MakeImage(
                root,
                "Saved session glass",
                Vector2.zero,
                root.sizeDelta,
                new Color(.07f, .075f, .09f, .94f),
                false);
            panel.raycastTarget = false;
            MakeText(
                root,
                "Reprendre la session precedente ?",
                new Vector2(0f, 43f),
                new Vector2(430f, 30f),
                17f,
                Ink,
                FontStyles.Normal);
            MakeButton(
                root,
                "Reprendre",
                new Vector2(-112f, -30f),
                new Vector2(188f, 48f),
                () => RestoreSavedSession(state));
            MakeButton(
                root,
                "Dock",
                new Vector2(112f, -30f),
                new Vector2(188f, 48f),
                StartWithCleanDock);
            _creator.DismissWindowDock();
            _creator.RegisterExternalSpatialWindow(
                root,
                "lab.resume_choice",
                StartWithCleanDock);
        }

        private void RestoreSavedSession(BrowserSessionState state)
        {
            RemoveResumeOffer();
            PlayerPrefs.DeleteKey(SessionStatePreference);
            PlayerPrefs.Save();
            BrowserSessionEntry[] entries = state?.windows ??
                Array.Empty<BrowserSessionEntry>();
            for (int i = 0; i < entries.Length && i < 3; i++)
            {
                BrowserSessionEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.url)) continue;
                if (string.Equals(entry.title, "Google", StringComparison.Ordinal))
                {
                    OpenOrFocusProtectedApplication(
                        "com.android.chrome",
                        "https://www.google.com",
                        "Google");
                    continue;
                }
                if (string.Equals(entry.title, "YouTube", StringComparison.Ordinal))
                {
                    OpenOrFocusProtectedApplication(
                        "com.google.android.youtube",
                        "https://www.youtube.com/",
                        "YouTube");
                    continue;
                }
                OpenBrowser(
                    string.IsNullOrWhiteSpace(entry.title)
                        ? "Navigateur " + (++_genericWindowSerial)
                        : entry.title,
                    entry.url);
            }
            ProtectedSessionEntry[] protectedEntries = state?.protectedApps;
            if (protectedEntries != null && protectedEntries.Length > 0)
            {
                for (int i = 0; i < protectedEntries.Length && i < 4; i++)
                {
                    ProtectedSessionEntry entry = protectedEntries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.packageName))
                        continue;
                    OpenOrFocusProtectedApplication(
                        MigrateProtectedPackage(entry.packageName),
                        entry.uri ?? string.Empty,
                        string.IsNullOrWhiteSpace(entry.label)
                            ? "Application"
                            : entry.label);
                }
            }
            else if (!string.IsNullOrWhiteSpace(state?.protectedPackage))
            {
                OpenOrFocusProtectedApplication(
                    MigrateProtectedPackage(state.protectedPackage),
                    string.IsNullOrWhiteSpace(state.protectedUri)
                        ? "https://www.google.com"
                        : state.protectedUri,
                    string.IsNullOrWhiteSpace(state.protectedLabel)
                        ? "Application"
                        : state.protectedLabel);
            }
            else if (state?.netflixVisible == true)
            {
                OpenOrFocusProtectedApplication(
                    "com.netflix.mediaclient",
                    "https://www.netflix.com/browse",
                    "Netflix");
            }
            if (state != null && state.keyboardVisible)
                ShowKeyboard(KeyboardTarget.WebContent);
            _creator?.RestoreLabWorkspaceForSession(
                state != null && state.workspaceVisible);
        }

        private static string MigrateProtectedPackage(string packageName) =>
            string.Equals(
                packageName,
                "com.google.android.googlequicksearchbox",
                StringComparison.Ordinal)
                ? "com.android.chrome"
                : packageName;

        private void StartWithCleanDock()
        {
            PlayerPrefs.DeleteKey(SessionStatePreference);
            PlayerPrefs.Save();
            RemoveResumeOffer();
            XrLabBrowserWindow[] windows = _windows.ToArray();
            for (int i = 0; i < windows.Length; i++) CloseWindow(windows[i]);
            XrealSecureSurfaceSpike[] protectedHosts = _protectedHosts
                .ConvertAll(entry => entry.Host)
                .ToArray();
            for (int i = 0; i < protectedHosts.Length; i++)
                protectedHosts[i]?.CloseHostedWindow();
            if (_keyboardCanvas != null) _keyboardCanvas.gameObject.SetActive(false);
            _creator?.RestoreLabWorkspaceForSession(false);
            _creator?.OpenWindowDockFromTwoPalms();
        }

        private void RemoveResumeOffer()
        {
            if (_resumeOfferRect != null)
                _creator?.UnregisterExternalSpatialWindow(_resumeOfferRect);
            if (_resumeOffer != null) Destroy(_resumeOffer);
            _resumeOffer = null;
            _resumeOfferRect = null;
        }

        internal void CloseWindow(XrLabBrowserWindow window)
        {
            if (window == null) return;
            int index = _windows.IndexOf(window);
            if (index < 0) return;
            _windows.RemoveAt(index);
            if (_activeBrowser == window)
                _activeBrowser = _windows.Count == 0
                    ? null
                    : _windows[_windows.Count - 1];
            _creator?.UnregisterExternalSpatialWindow(window.WindowRect);
            Destroy(window.gameObject);
        }

        private void BuildKeyboard()
        {
            var root = new GameObject("XR Lab Keyboard");
            _keyboardCanvas = root.AddComponent<Canvas>();
            _keyboardCanvas.renderMode = RenderMode.WorldSpace;
            _keyboardCanvas.worldCamera = _camera;
            _keyboardCanvas.sortingOrder = 220;
            root.AddComponent<GraphicRaycaster>();
            _keyboardRect = root.GetComponent<RectTransform>();
            _keyboardRect.sizeDelta = new Vector2(1040f, 500f);
            _keyboardRect.localScale = Vector3.one * .00065f;
            Vector3 keyboardForward = _camera.transform.forward.normalized;
            _keyboardRect.SetPositionAndRotation(
                _camera.transform.position + keyboardForward * 1.02f - Vector3.up * .32f,
                Quaternion.LookRotation(keyboardForward, Vector3.up));

            MakeImage(
                _keyboardRect,
                "Keyboard glass",
                Vector2.zero,
                _keyboardRect.sizeDelta,
                new Color(.075f, .08f, .10f, .94f),
                false).raycastTarget = false;
            _keyboardPreview = MakeText(
                _keyboardRect,
                "Saisir dans la page",
                new Vector2(0f, 205f),
                new Vector2(820f, 48f),
                21f,
                Ink,
                FontStyles.Normal);
            string[] rows = { "1234567890", "AZERTYUIOP", "QSDFGHJKLM", "WXCVBN,.;:" };
            float[] starts = { -405f, -405f, -405f, -360f };
            float[] ys = { 130f, 55f, -20f, -95f };
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    char key = rows[row][column];
                    MakeButton(
                        _keyboardRect,
                        key.ToString(),
                        new Vector2(starts[row] + column * 90f, ys[row]),
                        new Vector2(76f, 62f),
                        () => ReceiveCharacter(key));
                }
            }

            MakeButton(
                _keyboardRect,
                "MAJ",
                new Vector2(-445f, -175f),
                new Vector2(92f, 62f),
                ToggleShift);
            MakeButton(
                _keyboardRect,
                "ESPACE",
                new Vector2(-210f, -175f),
                new Vector2(350f, 62f),
                () => ReceiveCharacter(' '));
            MakeButton(
                _keyboardRect,
                "SUPPR",
                new Vector2(32f, -175f),
                new Vector2(116f, 62f),
                ReceiveBackspace);
            MakeButton(
                _keyboardRect,
                "TOUT",
                new Vector2(157f, -175f),
                new Vector2(112f, 62f),
                ReceiveClearAll);
            MakeButton(
                _keyboardRect,
                "ENTRÉE",
                new Vector2(350f, -175f),
                new Vector2(250f, 62f),
                ReceiveEnter);
            _creator.RegisterExternalSpatialWindow(
                _keyboardRect,
                "lab.keyboard",
                () => _keyboardCanvas.gameObject.SetActive(false));
            _keyboardCanvas.gameObject.SetActive(false);
        }

        private void ShowKeyboard(KeyboardTarget target)
        {
            if (_keyboardCanvas == null || _camera == null) return;
            if (target == KeyboardTarget.WebContent && _activeProtectedHost != null)
                target = KeyboardTarget.ProtectedApplication;
            if (_activeBrowser == null && _windows.Count > 0)
                _activeBrowser = _windows[_windows.Count - 1];
            _keyboardTarget = target;
            _keyboardCanvas.gameObject.SetActive(true);
            _creator.FocusExternalSpatialWindow(_keyboardRect);
            RefreshKeyboardPreview();
        }

        private void ToggleShift()
        {
            _uppercase = !_uppercase;
            RefreshKeyboardPreview();
        }

        private void ReceiveCharacter(char value)
        {
            char key = _uppercase ? char.ToUpperInvariant(value) : char.ToLowerInvariant(value);
            if (_keyboardTarget == KeyboardTarget.Address)
            {
                _addressBuffer += key;
                RefreshKeyboardPreview();
                return;
            }
            if (_keyboardTarget == KeyboardTarget.ProtectedApplication)
            {
                XrealSecureSurfaceSpike host = _activeProtectedHost?.Host;
                if (host != null && host.SendHostedText(key.ToString()))
                {
                    _protectedInputBuffers[host] = ProtectedInputBuffer(host) + key;
                    RefreshKeyboardPreview();
                }
            }
            else
                _activeBrowser?.SendCharacter(key);
        }

        private void ReceiveBackspace()
        {
            if (_keyboardTarget == KeyboardTarget.Address)
            {
                if (_addressBuffer.Length > 0)
                    _addressBuffer = _addressBuffer.Substring(0, _addressBuffer.Length - 1);
                RefreshKeyboardPreview();
                return;
            }
            if (_keyboardTarget == KeyboardTarget.ProtectedApplication)
            {
                XrealSecureSurfaceSpike host = _activeProtectedHost?.Host;
                if (host != null && host.SendHostedKey(67))
                {
                    string current = ProtectedInputBuffer(host);
                    if (current.Length > 0)
                        _protectedInputBuffers[host] =
                            current.Substring(0, current.Length - 1);
                    RefreshKeyboardPreview();
                }
            }
            else
                _activeBrowser?.SendKeyCode(67);
        }

        private void ReceiveClearAll()
        {
            if (_keyboardTarget == KeyboardTarget.Address)
            {
                _addressBuffer = string.Empty;
                RefreshKeyboardPreview();
                return;
            }
            if (_keyboardTarget == KeyboardTarget.ProtectedApplication)
            {
                XrealSecureSurfaceSpike host = _activeProtectedHost?.Host;
                if (host != null && host.ClearHostedText())
                {
                    _protectedInputBuffers[host] = string.Empty;
                    RefreshKeyboardPreview();
                }
            }
            else
                _activeBrowser?.ClearFocusedInput();
        }

        private void ReceiveEnter()
        {
            if (_keyboardTarget == KeyboardTarget.Address)
            {
                if (_activeBrowser != null)
                    _activeBrowser.Navigate(NormalizeAddress(_addressBuffer));
                _keyboardTarget = KeyboardTarget.WebContent;
                RefreshKeyboardPreview();
                return;
            }
            if (_keyboardTarget == KeyboardTarget.ProtectedApplication)
                _activeProtectedHost?.Host?.SendHostedKey(66);
            else
                _activeBrowser?.SendKeyCode(66);
        }

        private static string NormalizeAddress(string raw)
        {
            string value = (raw ?? string.Empty).Trim();
            if (value.Length == 0) return "https://www.google.com";
            if (value.Contains(" "))
                return "https://www.google.com/search?q=" + Uri.EscapeDataString(value);
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                value = "https://" + value;
            return value;
        }

        private void RefreshKeyboardPreview()
        {
            if (_keyboardPreview == null) return;
            if (_keyboardTarget == KeyboardTarget.Address)
                _keyboardPreview.text = string.IsNullOrEmpty(_addressBuffer)
                    ? "Adresse ou recherche"
                    : _addressBuffer;
            else if (_keyboardTarget == KeyboardTarget.ProtectedApplication)
            {
                if (_activeProtectedHost == null)
                {
                    _keyboardPreview.text = "Choisis une application";
                }
                else
                {
                    string typed = ProtectedInputBuffer(_activeProtectedHost.Host);
                    if (typed.Length > 54)
                        typed = "…" + typed.Substring(typed.Length - 53);
                    _keyboardPreview.text = string.IsNullOrEmpty(typed)
                        ? "Saisie → " + _activeProtectedHost.Label +
                          (_uppercase ? "  •  MAJ" : string.Empty)
                        : typed + (_uppercase ? "  •  MAJ" : string.Empty);
                }
            }
            else
                _keyboardPreview.text = _activeBrowser == null
                    ? "Ouvre d’abord une fenêtre Web"
                    : "Saisie → " + _activeBrowser.Title +
                      (_uppercase ? "  •  MAJ" : string.Empty);
        }

        private string ProtectedInputBuffer(XrealSecureSurfaceSpike host)
        {
            if (host == null) return string.Empty;
            return _protectedInputBuffers.TryGetValue(host, out string value)
                ? value ?? string.Empty
                : string.Empty;
        }

        private static Image MakeImage(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            Color color,
            bool circle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.sprite = circle
                ? XrLabSprites.Circle
                : XrLabSprites.Rounded;
            image.type = circle ? Image.Type.Simple : Image.Type.Sliced;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return image;
        }

        private static TextMeshProUGUI MakeText(
            Transform parent,
            string text,
            Vector2 position,
            Vector2 size,
            float fontSize,
            Color color,
            FontStyles style)
        {
            var go = new GameObject("Text " + text);
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return label;
        }

        internal static Button MakeButton(
            Transform parent,
            string label,
            Vector2 position,
            Vector2 size,
            UnityEngine.Events.UnityAction action)
        {
            Image surface = MakeImage(
                parent,
                "Lab button " + label,
                position,
                size,
                Glass,
                false);
            Button button = MakeClickable(
                surface,
                action,
                new Vector3(size.x, size.y, 18f));
            MakeText(
                surface.transform,
                label,
                Vector2.zero,
                size - new Vector2(8f, 6f),
                Mathf.Min(21f, size.y * .34f),
                Ink,
                FontStyles.Bold);
            AddFeedback(button, surface, Glass, GlassHover);
            return button;
        }

        private static Button MakeClickable(
            Image image,
            UnityEngine.Events.UnityAction action,
            Vector3 colliderSize)
        {
            Button button = image.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(action);
            var collider = image.gameObject.AddComponent<BoxCollider>();
            collider.size = colliderSize;
            return button;
        }

        private static void AddFeedback(
            Button button,
            Image image,
            Color normal,
            Color hover)
        {
            var feedback = button.gameObject.AddComponent<VisionSpatialControlFeedback>();
            feedback.Configure(image, normal, hover, Color.white, Ink);
        }
    }

    internal sealed class XrLabDockReorderItem : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        private WorldCreatorLabShell _shell;
        private string _id;
        private bool _dragging;

        public void Configure(WorldCreatorLabShell shell, string id)
        {
            _shell = shell;
            _id = id;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _dragging = _shell != null && _shell.BeginDockItemDrag(
                _id, eventData.pointerCurrentRaycast.worldPosition);
            if (_dragging) eventData.eligibleForClick = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _shell.DragDockItem(
                eventData.pointerCurrentRaycast.worldPosition, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;
            _shell.EndDockItemDrag(eventData.pointerCurrentRaycast.worldPosition);
        }
    }

    internal sealed class XrLabDockDepthHandle : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        IDragHandler, IPointerClickHandler
    {
        private WorldCreatorLabShell _shell;

        public void Configure(WorldCreatorLabShell shell) => _shell = shell;

        public void OnPointerEnter(PointerEventData eventData) { }
        public void OnPointerExit(PointerEventData eventData) { }

        public void OnPointerDown(PointerEventData eventData) =>
            _shell?.BeginDockDepthDrag(eventData.pointerCurrentRaycast.worldPosition);

        public void OnDrag(PointerEventData eventData) =>
            _shell?.DragDockDepth(
                eventData.pointerCurrentRaycast.worldPosition, eventData);

        // Marker used by the shared world-space resolver. Depth is performed by
        // pointer-down/drag; a stationary release intentionally has no action.
        public void OnPointerClick(PointerEventData eventData) { }
    }

    public sealed class XrLabWebView : WebView
    {
        private int _targetFps = 15;
        private bool _nativeConfigured;
        private int _warmInvalidationsRemaining = 12;
        private float _nextWarmInvalidation;
        private int _pageLoadVersion;
        private int _scaleScheduledVersion = -1;
        private int _xrViewportWidth = 1080;
        private bool _eventPumpFailed;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _nativeWebView;
#endif

        public void Configure(RawImage output, string url)
        {
            m_rawImage = output;
            // HardwareBuffer is loaded by Chromium but does not share a usable
            // texture with XREAL's GLES eye context on the S24. ByteBuffer is a
            // deterministic CPU copy and renders the real page instead of the
            // permanent white loading texture.
            m_captureMode = CaptureMode.ByteBuffer;
            _targetFps = 15;
            // Android 16 stops drawing TLab's WebView when its root is wholly
            // outside the physical phone display. That leaves the XR texture
            // white until the S24 is manually rotated and the root intersects
            // the wider landscape display. Keep the renderer on display 0 at
            // x=0; it is only a render host, while all interaction remains XR
            // gaze/pinch/keyboard. View and texture stay byte-compatible.
            m_screenFullRes = Vector2Int.zero;
            Init(
                new Vector2Int(1080, 608),
                new Vector2Int(1080, 608),
                url,
                _targetFps,
                new Download.Option());
        }

        private void Update()
        {
            // The native WebView loads independently. TLab expects its host to
            // pump the shared texture; without this, Unity displays the white
            // loading texture even though Chromium has finished the page.
            if (state != State.Initialized) return;
            ConfigureNativeWebViewOnce();
            PumpPageEvents();
            if (_warmInvalidationsRemaining > 0 &&
                Time.unscaledTime >= _nextWarmInvalidation)
            {
                _nextWarmInvalidation = Time.unscaledTime + .35f;
                _warmInvalidationsRemaining--;
                RequestNativeRedraw();
            }
            UpdateFrame();
        }

        public void RequestNativeRedraw()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_nativeWebView == null) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    _nativeWebView?.Call("postInvalidateOnAnimation");
                    _nativeWebView?.Call("requestLayout");
                }));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[XrLab] WebView redraw failed: " + exception.Message);
            }
#endif
        }

        public void SetXrViewportWidth(int pixelWidth)
        {
            _xrViewportWidth = Mathf.Clamp(pixelWidth, 640, 1920);
            if (state == State.Initialized) ApplyXrViewportConstraint();
        }

        private void ApplyXrViewportConstraint()
        {
            int width = _xrViewportWidth;
            EvaluateJS(
                "(function(){var w=" + width + ",d=window.devicePixelRatio||1;" +
                "var m=document.querySelector('meta[name=viewport]');" +
                "if(!m){m=document.createElement('meta');m.name='viewport';" +
                "(document.head||document.documentElement).appendChild(m);}" +
                "m.setAttribute('content','width='+w+',initial-scale='+(1/d)+" +
                "',minimum-scale=0.1,maximum-scale=10,user-scalable=yes');" +
                "document.documentElement.style.setProperty('min-width',w+'px','important');" +
                "if(document.body)document.body.style.setProperty('min-width',w+'px','important');" +
                "})();");
        }

        private void ConfigureNativeWebViewOnce()
        {
            if (_nativeConfigured) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using AndroidJavaObject pluginClass =
                    m_NativePlugin.Call<AndroidJavaObject>("getClass");
                using AndroidJavaObject field =
                    pluginClass.Call<AndroidJavaObject>("getDeclaredField", "mWebView");
                field.Call("setAccessible", true);
                _nativeWebView = field.Call<AndroidJavaObject>("get", m_NativePlugin);
                if (_nativeWebView == null) return;

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    // The Lab owns text entry through its XR keyboard. Prevent
                    // Android's phone IME from resizing/reparenting the hidden
                    // off-screen WebView when an HTML input receives focus.
                    _nativeWebView.Call("setShowSoftInputOnFocus", false);
                    // Chromium's native scroll indicators are captured inside
                    // the XR texture as thin white seams (especially visible
                    // over YouTube video). XR already owns its scroll feedback.
                    _nativeWebView.Call("setVerticalScrollBarEnabled", false);
                    _nativeWebView.Call("setHorizontalScrollBarEnabled", false);
                    _nativeWebView.Call("setOverScrollMode", 2); // OVER_SCROLL_NEVER
                    using AndroidJavaObject settings =
                        _nativeWebView.Call<AndroidJavaObject>("getSettings");
                    if (settings != null)
                    {
                        settings.Call("setOffscreenPreRaster", false);
                        // TLab already enables wide viewport + overview before
                        // the first URL. The S24 default scale still applies its
                        // ~2.8 device density, making 1080 physical pixels look
                        // like a ~390 CSS-pixel phone viewport. Android documents
                        // setInitialScale as density-independent: 100 therefore
                        // restores a true desktop 1:1 CSS layout. Set it before
                        // changing the UA; that change reloads the current page.
                        _nativeWebView.Call("setInitialScale", 100);
                        string ua = settings.Call<string>("getUserAgentString") ?? "";
                        int chromeStart = ua.IndexOf("Chrome/", StringComparison.Ordinal);
                        int chromeEnd = chromeStart < 0
                            ? -1
                            : ua.IndexOf(' ', chromeStart);
                        string chrome = chromeStart < 0
                            ? "Chrome/138.0.0.0"
                            : ua.Substring(
                                chromeStart,
                                (chromeEnd < 0 ? ua.Length : chromeEnd) - chromeStart);
                        settings.Call(
                            "setUserAgentString",
                            "Mozilla/5.0 (X11; Linux x86_64) " +
                            "AppleWebKit/537.36 (KHTML, like Gecko) " +
                            chrome + " Safari/537.36");
                    }
                    using AndroidJavaObject window =
                        activity.Call<AndroidJavaObject>("getWindow");
                    window?.Call("setSoftInputMode", 51); // hidden + adjustNothing
                    _nativeWebView.Call("postInvalidateOnAnimation");
                }));
                _nativeConfigured = true;
                Debug.Log(
                    "[XrLab] Android 16 WebView configured on display 0: " +
                    "view/texture 1080x608, desktop scale 100%, XR keyboard.");
            }
            catch (Exception exception)
            {
                // Reflection is Lab-only and failure must not break browsing.
                Debug.LogWarning(
                    "[XrLab] native WebView configuration unavailable: " +
                    exception.Message);
                _nativeConfigured = true;
            }
#else
            _nativeConfigured = true;
#endif
        }

        private void PumpPageEvents()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_eventPumpFailed || m_NativePlugin == null) return;
            try
            {
                string[] messages =
                    m_NativePlugin.Call<string[]>("DispatchMessageQueue");
                if (messages == null) return;
                foreach (string json in messages)
                {
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    var message = new EventCallback.Message(json);
                    if ((EventCallback.Type)message.type == EventCallback.Type.OnPageStart)
                    {
                        _pageLoadVersion++;
                    }
                    else if ((EventCallback.Type)message.type == EventCallback.Type.OnPageFinish)
                    {
                        int version = _pageLoadVersion;
                        if (version != _scaleScheduledVersion)
                        {
                            _scaleScheduledVersion = version;
                            StartCoroutine(NormalizePageScaleAfterFinish(version, message.payload));
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                _eventPumpFailed = true;
                Debug.LogWarning("[XrLab] page event pump unavailable: " + exception.Message);
            }
#endif
        }

        private IEnumerator NormalizePageScaleAfterFinish(int version, string url)
        {
            // Chromium finalises the page scale just after onPageFinished.
            // Waiting one rendered frame interval avoids WebView.getScale's
            // documented UI/render-thread race without guessing load time.
            yield return new WaitForSecondsRealtime(.30f);
            if (version != _pageLoadVersion) yield break;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_nativeWebView == null) yield break;
            // Mobile pages commonly lock their viewport to device-width with a
            // minimum scale of 1. On the S24 that means ~390 CSS pixels enlarged
            // by density 3 across our 1080-pixel XR surface, and WebView.zoomBy
            // is silently clamped. Replace that page-owned constraint with an
            // XR viewport before applying the native scale.
            ApplyXrViewportConstraint();
            InstallXrPageBehaviors();
            yield return new WaitForSecondsRealtime(.20f);
            if (version != _pageLoadVersion) yield break;
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity =
                player.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    float currentScale = _nativeWebView.Call<float>("getScale");
                    if (currentScale > 1.05f)
                    {
                        float factor = Mathf.Clamp(1f / currentScale, .01f, 100f);
                        _nativeWebView.Call("zoomBy", factor);
                        _nativeWebView.Call<bool>(
                            "postDelayed",
                            new AndroidJavaRunnable(() =>
                            {
                                float verified = _nativeWebView.Call<float>("getScale");
                                Debug.Log(
                                    $"[XrLab] desktop page scale {currentScale:F2} -> " +
                                    $"{verified:F2} ({url}).");
                            }),
                            250L);
                    }
                    else
                    {
                        Debug.Log(
                            $"[XrLab] desktop page scale already {currentScale:F2} " +
                            $"({url}).");
                    }
                    _nativeWebView.Call("postInvalidateOnAnimation");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[XrLab] page scale normalisation failed: " +
                        exception.Message);
                }
            }));
#endif
        }

        private void InstallXrPageBehaviors()
        {
            // Native Android fullscreen moves video to a CustomView outside the
            // texture captured by Unity. Keep that site control hidden: the Lab
            // header owns XR presentation and never invokes the native API.
            EvaluateJS(
                "(function(){if(window.__mlomegaXrPageBehaviors)return;" +
                "window.__mlomegaXrPageBehaviors=true;" +
                "var s=document.createElement('style');s.id='mlomega-xr-style';" +
                "s.textContent='.ytp-fullscreen-button{display:none!important;}" +
                "html.mlomega-xr-clean,body.mlomega-xr-clean{" +
                "overflow:hidden!important;background:#000!important;}" +
                "body.mlomega-xr-clean>*{visibility:hidden!important;}" +
                "body.mlomega-xr-clean .mlomega-xr-player," +
                "body.mlomega-xr-clean .mlomega-xr-player *{" +
                "visibility:visible!important;} .mlomega-xr-player{" +
                "position:fixed!important;inset:0!important;width:100vw!important;" +
                "height:100vh!important;max-width:none!important;max-height:none!important;" +
                "transform:none!important;z-index:2147483647!important;background:#000!important;}" +
                ".mlomega-xr-player video{width:100%!important;height:100%!important;" +
                "object-fit:contain!important;}';" +
                "(document.head||document.documentElement).appendChild(s);})();");
        }

        public void SetTargetFps(int fps)
        {
            _targetFps = Mathf.Clamp(fps, 2, 30);
            if (state == State.Initialized)
                SetFps(_targetFps);
            else
                m_fps = _targetFps;
        }

        public void ScrollAt(Vector2Int webPoint, int deltaY)
        {
            if (deltaY == 0 || viewSize.x <= 0 || viewSize.y <= 0) return;
            string x = Mathf.Clamp01(webPoint.x / (float)viewSize.x)
                .ToString("R", CultureInfo.InvariantCulture);
            string y = Mathf.Clamp01(webPoint.y / (float)viewSize.y)
                .ToString("R", CultureInfo.InvariantCulture);
            // WebView.ScrollBy always scrolls the document. Consent dialogs and
            // other web popups instead own a nested scroll container; select the
            // nearest scrollable ancestor under the XR gaze, then fall back to
            // the page. This preserves normal pages and makes modal content usable.
            EvaluateJS(
                "(function(){var e=document.elementFromPoint(window.innerWidth*" + x +
                ",window.innerHeight*" + y + "),s=e;" +
                "while(s&&s!==document.documentElement){var c=getComputedStyle(s);" +
                "if(s.scrollHeight>s.clientHeight+2&&" +
                "/(auto|scroll|overlay)/.test(c.overflowY)){s.scrollBy(0," + deltaY +
                ");return;}s=s.parentElement;}window.scrollBy(0," + deltaY + ");})();");
        }

        public void SetXrContentMode(bool enabled)
        {
            InstallXrPageBehaviors();
            string flag = enabled ? "true" : "false";
            EvaluateJS(
                "(function(on){var old=window.__mlomegaXrPlayer;" +
                "if(old)old.classList.remove('mlomega-xr-player');" +
                "document.documentElement.classList.remove('mlomega-xr-clean');" +
                "if(document.body)document.body.classList.remove('mlomega-xr-clean');" +
                "if(!on){window.__mlomegaXrPlayer=null;return;}" +
                "var p=document.querySelector('#movie_player,.html5-video-player');" +
                "if(!p){var videos=[].slice.call(document.querySelectorAll('video'));" +
                "var v=videos.find(function(x){return !x.paused&&x.offsetWidth>0;})||" +
                "videos.find(function(x){return x.offsetWidth>0;});" +
                "p=v&&((v.closest&&v.closest('[data-mlomega-video],.player,.video-player'))||v);}" +
                "if(!p)return;p.classList.add('mlomega-xr-player');" +
                "document.documentElement.classList.add('mlomega-xr-clean');" +
                "if(document.body)document.body.classList.add('mlomega-xr-clean');" +
                "window.__mlomegaXrPlayer=p;window.scrollTo(0,0);})(" + flag + ");");
        }
    }

    public sealed class XrLabBrowserWindow : MonoBehaviour
    {
        [Serializable]
        private sealed class XrCropResult
        {
            public bool ok;
            public float x;
            public float y;
            public float w;
            public float h;
            public float vw;
            public float vh;
            public string kind;
            public string detail;
        }

        private WorldCreatorLabShell _shell;
        private WorldCreatorController _creator;
        private Camera _camera;
        private RectTransform _rect;
        private XrLabWebView _browser;
        private TextMeshProUGUI _urlLabel;
        private Image _frame;
        private Image _header;
        private Image _address;
        private RawImage _raw;
        private RectTransform _rawRect;
        private BoxCollider _addressCollider;
        private BoxCollider _viewportCollider;
        private Button _backButton;
        private Button _forwardButton;
        private Button _upButton;
        private Button _downButton;
        private Button _keyboardButton;
        private Button _xrButton;
        private XrLabVolumeSlider _volumeSlider;
        private bool _xrMode;
        private bool _xrTransition;
        private Rect _xrRestoreUv;
        private Vector2 _xrRestoreSize;
        private Vector3 _xrRestorePosition;
        private Quaternion _xrRestoreRotation;
        private Vector3 _xrRestoreScale;
        private float _nextUrlRefresh;
        private string _currentUrl;
        private int _focusProbeVersion;

        public string Title { get; private set; }
        public string CurrentUrl => _currentUrl;
        public RectTransform WindowRect => _rect;

        public static XrLabBrowserWindow Create(
            WorldCreatorLabShell shell,
            WorldCreatorController creator,
            Camera camera,
            string title,
            string url)
        {
            var root = new GameObject("XR Browser " + title);
            var window = root.AddComponent<XrLabBrowserWindow>();
            window._shell = shell;
            window._creator = creator;
            window._camera = camera;
            window.Title = title;
            window._currentUrl = url;
            window.Build();
            return window;
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _camera;
            canvas.sortingOrder = 180;
            gameObject.AddComponent<GraphicRaycaster>();
            _rect = GetComponent<RectTransform>();
            _rect.sizeDelta = new Vector2(1120f, 760f);
            _rect.localScale = Vector3.one * .00068f;

            _frame = MakeSurface(
                _rect,
                "Browser frame",
                Vector2.zero,
                _rect.sizeDelta,
                new Color(.055f, .06f, .075f, .96f));
            _frame.raycastTarget = false;
            _header = MakeSurface(
                _rect,
                "Browser header",
                new Vector2(0f, 334f),
                new Vector2(1080f, 72f),
                new Color(.13f, .14f, .17f, .97f));
            _header.raycastTarget = false;

            _backButton = MakeHeaderButton("‹", -500f, () => _browser.GoBack());
            _forwardButton = MakeHeaderButton("›", -440f, () => _browser.GoForward());
            _volumeSlider = MakeVolumeSlider(266f);
            _xrButton = MakeHeaderButton("XR", 314f, ToggleXrVideoMode);
            _upButton = MakeHeaderButton("↑", 326f, () => _browser.PageUp(false));
            _downButton = MakeHeaderButton("↓", 386f, () => _browser.PageDown(false));
            // The address bar itself remains the URL/search entry point. This
            // explicit keyboard button is essential on sites (notably YouTube)
            // whose scripted search control does not reliably expose focus.
            _keyboardButton = MakeHeaderButton("ABC", 446f, OpenContentKeyboard);

            _address = MakeSurface(
                _rect,
                "Address " + Title,
                new Vector2(-38f, 334f),
                new Vector2(690f, 50f),
                new Color(.075f, .08f, .10f, .94f));
            _address.gameObject.AddComponent<RectMask2D>();
            Button addressButton = _address.gameObject.AddComponent<Button>();
            addressButton.transition = Selectable.Transition.None;
            addressButton.onClick.AddListener(() => _shell.BeginAddressEntry(this));
            _addressCollider = _address.gameObject.AddComponent<BoxCollider>();
            _addressCollider.size = new Vector3(690f, 50f, 16f);
            _urlLabel = MakeLabel(
                _address.transform,
                Title + "  •  " + _currentUrl,
                Vector2.zero,
                new Vector2(650f, 42f),
                18f,
                new Color(.86f, .88f, .94f, .96f),
                TextAlignmentOptions.Left);
            _urlLabel.overflowMode = TextOverflowModes.Ellipsis;

            var viewport = new GameObject("Web surface " + Title);
            viewport.transform.SetParent(_rect, false);
            _raw = viewport.AddComponent<RawImage>();
            _raw.color = Color.white;
            _raw.raycastTarget = true;
            // The native Android render host occasionally contributes its
            // one-pixel top/left boundary to the captured texture. Crop two
            // pixels on every edge; content and pointer mapping remain unchanged.
            _raw.uvRect = new Rect(
                2f / 1080f,
                2f / 608f,
                1f - 4f / 1080f,
                1f - 4f / 608f);
            _rawRect = _raw.rectTransform;
            _rawRect.anchorMin = _rawRect.anchorMax = new Vector2(.5f, .5f);
            _rawRect.anchoredPosition = new Vector2(0f, -42f);
            _rawRect.sizeDelta = new Vector2(1060f, 596f);
            _viewportCollider = viewport.AddComponent<BoxCollider>();
            _viewportCollider.size = new Vector3(1060f, 596f, 14f);
            _browser = viewport.AddComponent<XrLabWebView>();
            var pointer = viewport.AddComponent<XrLabBrowserPointer>();
            pointer.Configure(this, _browser, _rawRect);
            _browser.Configure(_raw, _currentUrl);
            ApplyWindowSize(_rect.sizeDelta, false);
        }

        public void RegisterSpatialWindow()
        {
            _creator?.RegisterExternalSpatialWindow(
                _rect,
                "lab.browser." + Title,
                () => _shell.CloseWindow(this),
                ApplyWindowSize,
                ApplyWindowCrop);
        }

        private void ApplyWindowCrop(Vector4 normalizedInsets, bool final)
        {
            // The shared crop viewport clips this already-rendered WebView while
            // leaving its texture size, DOM layout and pointer coordinate space
            // untouched. This is intentionally not a browser resize.
            if (final) _browser?.RequestNativeRedraw();
        }

        private void ApplyWindowSize(Vector2 requested, bool final)
        {
            if (_rect == null) return;
            Vector2 size = new Vector2(
                Mathf.Clamp(requested.x, 620f, 2600f),
                Mathf.Clamp(requested.y, 360f, 1200f));
            _rect.sizeDelta = size;
            float halfWidth = size.x * .5f;
            float halfHeight = size.y * .5f;
            const float sideGutter = 30f;
            const float verticalGutter = 48f;
            float visibleWidth = _xrMode
                ? size.x
                : size.x - sideGutter * 2f;
            float visibleHeight = _xrMode
                ? size.y
                : size.y - verticalGutter * 2f;
            float surfaceBottom = -halfHeight + verticalGutter;
            float headerY = halfHeight - verticalGutter - 36f;
            // Reserve navigation on the left and XR/media/page controls on the
            // right. The URL is clipped by its own mask, never over the buttons.
            float addressWidth = Mathf.Max(150f, size.x - 480f);
            Vector2 contentSize = _xrMode
                ? new Vector2(
                    Mathf.Max(520f, size.x - 80f),
                    Mathf.Min(
                        Mathf.Max(220f, size.y - 120f),
                        Mathf.Max(520f, size.x - 80f) * 9f / 16f))
                : new Vector2(
                    Mathf.Max(520f, visibleWidth - 20f),
                    Mathf.Max(220f, visibleHeight - 92f));
            float contentY = _xrMode
                ? 0f
                : surfaceBottom + 10f + contentSize.y * .5f;

            LayoutSurface(_frame, Vector2.zero,
                new Vector2(visibleWidth, visibleHeight));
            _frame.gameObject.SetActive(!_xrMode);
            LayoutSurface(
                _header,
                new Vector2(0f, headerY),
                new Vector2(visibleWidth, 72f));
            _header.gameObject.SetActive(!_xrMode);
            LayoutButton(_backButton, new Vector2(-halfWidth + 60f, headerY));
            LayoutButton(_forwardButton, new Vector2(-halfWidth + 120f, headerY));
            LayoutVolumeSlider(
                _volumeSlider,
                new Vector2(halfWidth - 294f, headerY));
            LayoutButton(_xrButton, new Vector2(halfWidth - 246f, headerY));
            LayoutButton(_upButton, new Vector2(halfWidth - 186f, headerY));
            LayoutButton(_downButton, new Vector2(halfWidth - 126f, headerY));
            LayoutButton(_keyboardButton, new Vector2(halfWidth - 66f, headerY));
            _xrButton.gameObject.SetActive(!_xrMode);
            _volumeSlider.gameObject.SetActive(!_xrMode);
            _backButton.gameObject.SetActive(!_xrMode);
            _forwardButton.gameObject.SetActive(!_xrMode);
            _upButton.gameObject.SetActive(!_xrMode);
            _downButton.gameObject.SetActive(!_xrMode);
            _keyboardButton.gameObject.SetActive(!_xrMode);
            _address.gameObject.SetActive(!_xrMode);
            LayoutSurface(
                _address,
                new Vector2(-90f, headerY),
                new Vector2(addressWidth, 50f));
            if (_addressCollider != null)
                _addressCollider.size = new Vector3(addressWidth, 50f, 16f);
            if (_urlLabel != null)
                _urlLabel.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(160f, addressWidth - 40f), 42f);
            if (_rawRect != null)
            {
                _rawRect.anchoredPosition = new Vector2(0f, contentY);
                _rawRect.sizeDelta = contentSize;
            }
            if (_viewportCollider != null)
                _viewportCollider.size =
                    new Vector3(contentSize.x, contentSize.y, 14f);

            if (!final || _browser == null ||
                _browser.state != FragmentCapture.State.Initialized)
                return;
            // Entering XR must never recreate the native WebView surface: that
            // can pause a playing video. The existing 1080-wide texture is only
            // presented larger while DOM isolation hides the rest of the page.
            if (_xrMode) return;
            float contentAspect = contentSize.x / contentSize.y;
            int textureWidth;
            int textureHeight;
            if (contentAspect >= 1f)
            {
                // The backing page now grows with the physical XR window instead
                // of leaving a larger shell around a fixed 1080-wide page.
                textureWidth = Mathf.Clamp(
                    Mathf.RoundToInt(contentSize.x), 1080, 1920);
                textureHeight = Mathf.Clamp(
                    Mathf.RoundToInt(textureWidth / contentAspect), 240, 1080);
            }
            else
            {
                textureHeight = Mathf.Clamp(
                    Mathf.RoundToInt(contentSize.y), 1080, 1600);
                textureWidth = Mathf.Clamp(
                    Mathf.RoundToInt(textureHeight * contentAspect), 360, 1920);
            }
            var resolution = new Vector2Int(textureWidth, textureHeight);
            _browser.Resize(resolution, resolution);
            _browser.SetXrViewportWidth(textureWidth);
            if (_raw != null)
                _raw.uvRect = new Rect(
                    2f / textureWidth,
                    2f / textureHeight,
                    1f - 4f / textureWidth,
                    1f - 4f / textureHeight);
            _browser.RequestNativeRedraw();
        }

        private void Update()
        {
            if (_browser == null || _browser.state != FragmentCapture.State.Initialized)
                return;
            if (Time.unscaledTime < _nextUrlRefresh) return;
            _nextUrlRefresh = Time.unscaledTime + .75f;
            string current = _browser.GetUrl();
            if (!string.IsNullOrWhiteSpace(current)) _currentUrl = current;
            if (_urlLabel != null)
                _urlLabel.text = Title + "  •  " + Shorten(_currentUrl, 54);
        }

        public void SetSpatialPose(Vector3 position, Quaternion rotation)
        {
            _rect.SetPositionAndRotation(position, rotation);
        }

        public void SetFocused(bool focused)
        {
            _browser?.SetTargetFps(focused ? 15 : 3);
        }

        public void Navigate(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            _currentUrl = url;
            _browser?.LoadUrl(url);
        }

        public void SendCharacter(char key) => _browser?.KeyEvent(key);

        public void SendKeyCode(int keyCode) => _browser?.KeyEvent(keyCode);

        public void SendText(string text)
        {
            if (_browser == null || string.IsNullOrEmpty(text)) return;
            foreach (char key in text) _browser.KeyEvent(key);
        }

        public void ClearFocusedInput()
        {
            _browser?.EvaluateJS(
                "var e=document.activeElement;" +
                "if(e&&('value' in e)){e.value='';" +
                 "e.dispatchEvent(new Event('input',{bubbles:true}));}");
        }

        private void OpenContentKeyboard()
        {
            if (_browser == null) return;
            // Preserve the site's currently focused editor. If it lost focus,
            // prefer the first visible editable field instead of opening an XR
            // keyboard whose keystrokes have nowhere to go.
            _browser.EvaluateJS(
                "(function(){var ok=function(e){return !!e&&(e.tagName==='INPUT'||" +
                "e.tagName==='TEXTAREA'||e.isContentEditable);};var e=document.activeElement;" +
                "if(!ok(e)){var a=document.querySelectorAll('input:not([type=hidden])," +
                "textarea,[contenteditable=true]');for(var i=0;i<a.length;i++){var r=a[i].getBoundingClientRect();" +
                "if(r.width>0&&r.height>0&&r.bottom>0&&r.top<innerHeight){e=a[i];break;}}}" +
                "if(ok(e))e.focus();})();");
            _shell.BeginWebContentEntry(this);
        }

        private void ToggleXrVideoMode()
        {
            if (_rect == null || _camera == null) return;
            if (_xrTransition) return;
            Debug.Log($"[XrLab] XR header activated mode={_xrMode} url={_currentUrl}");
            if (!_xrMode)
            {
                StartCoroutine(EnterXrCropMode());
            }
            else
            {
                _xrMode = false;
                ApplyWindowSize(_xrRestoreSize, false);
                _rect.SetPositionAndRotation(
                    _xrRestorePosition,
                    _xrRestoreRotation);
                _rect.localScale = _xrRestoreScale;
                if (_raw != null) _raw.uvRect = _xrRestoreUv;
                _creator?.RefreshExternalSpatialWindow(_rect);
                _browser?.RequestNativeRedraw();
            }
        }

        private IEnumerator EnterXrCropMode()
        {
            if (_browser == null || _raw == null) yield break;
            _xrTransition = true;
            const string script =
                "try{var vw=innerWidth||document.documentElement.clientWidth;" +
                "var vh=innerHeight||document.documentElement.clientHeight;var e=null;" +
                "var a=[].slice.call(document.querySelectorAll('video'));" +
                "for(var i=0;i<a.length;i++){var q=a[i].getBoundingClientRect();" +
                "if(q.width>120&&q.height>70&&q.bottom>0&&q.right>0){e=a[i];if(!a[i].paused)break;}}" +
                "if(!e)e=document.querySelector('#movie_player,.html5-video-player,ytm-player," +
                "ytm-watch-media,#player-control-container,.player-container,.video-player,iframe,canvas');" +
                "if(!e){var p=document.elementFromPoint(vw*.35,vh*.35);" +
                "for(var n=0;p&&n<10;n++,p=p.parentElement){var z=p.getBoundingClientRect();" +
                "var ar=z.height>0?z.width/z.height:0;if(z.width>240&&z.height>120&&ar>1.25&&ar<2.6){e=p;break;}}}" +
                "var host=String(location.hostname||'');var detail='videos='+a.length+',host='+host;" +
                "if(!e&&host.indexOf('youtube.com')>=0){var top=Math.min(48,vh*.08);" +
                "var width=Math.min(vw,vh*1.36);var height=Math.min(vh-top,width*9/16);" +
                "tlab.postResult(xrResultId,JSON.stringify({ok:true,x:0,y:top,w:width,h:height," +
                "vw:vw,vh:vh,kind:'YOUTUBE_GEOMETRY',detail:detail}));}" +
                "else if(!e){tlab.postResult(xrResultId,JSON.stringify({ok:false,detail:detail}));}" +
                "else{var r=e.getBoundingClientRect();var l=Math.max(0,r.left),t=Math.max(0,r.top);" +
                "var rr=Math.min(vw,r.right),bb=Math.min(vh,r.bottom);" +
                "tlab.postResult(xrResultId,JSON.stringify({ok:rr-l>48&&bb-t>32,x:l,y:t,w:rr-l,h:bb-t," +
                "vw:vw,vh:vh,kind:String(e.tagName||'NODE')+'#'+String(e.id||''),detail:detail}));}}" +
                "catch(err){tlab.postResult(xrResultId,JSON.stringify({ok:false," +
                "detail:'js:'+String(err&&err.name)+':'+String(err&&err.message)}));}";

            XrCropResult crop = null;
            foreach (JavaAsyncResult result in
                _browser.EvaluateJSForResult("xrResultId", script))
            {
                if (result == null)
                {
                    yield return null;
                    continue;
                }
                Debug.Log(
                    $"[XrLab] XR probe result status={result.status} " +
                    $"payload={(string.IsNullOrWhiteSpace(result.s) ? "<empty>" : result.s)}");
                if (
                    result.status == JavaAsyncResult.Status.COMPLETE &&
                    !string.IsNullOrWhiteSpace(result.s))
                {
                    try
                    {
                        crop = JsonUtility.FromJson<XrCropResult>(result.s);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[XrLab] XR crop parse failed: " + exception.Message);
                    }
                }
            }

            if (
                crop == null || !crop.ok || crop.vw <= 0f || crop.vh <= 0f ||
                crop.w <= 0f || crop.h <= 0f)
            {
                Debug.LogWarning(
                    "[XrLab] XR crop refused: no visible video/player surface. " +
                    (crop != null ? crop.detail : "no-result"));
                _xrTransition = false;
                yield break;
            }

            _xrRestoreSize = _rect.sizeDelta;
            _xrRestorePosition = _rect.position;
            _xrRestoreRotation = _rect.rotation;
            _xrRestoreScale = _rect.localScale;
            _xrRestoreUv = _raw.uvRect;
            _xrMode = true;
            ApplyWindowSize(new Vector2(1680f, 1040f), false);

            float left = Mathf.Clamp01(crop.x / crop.vw);
            float bottom = Mathf.Clamp01(1f - (crop.y + crop.h) / crop.vh);
            float width = Mathf.Clamp01(crop.w / crop.vw);
            float height = Mathf.Clamp01(crop.h / crop.vh);
            _raw.uvRect = new Rect(left, bottom, width, height);

            Vector3 forward = _camera.transform.forward.normalized;
            _rect.SetPositionAndRotation(
                _camera.transform.position + forward * 1.05f,
                Quaternion.LookRotation(forward, Vector3.up));
            _creator?.RefreshExternalSpatialWindow(_rect);
            _browser.RequestNativeRedraw();
            Debug.Log(
                $"[XrLab] XR crop active kind={crop.kind} " +
                $"rect={crop.x:F0},{crop.y:F0},{crop.w:F0},{crop.h:F0} " +
                $"viewport={crop.vw:F0}x{crop.vh:F0}.");
            _xrTransition = false;
        }

        public void ProbeFocusedEditable(Vector2Int webPoint)
        {
            if (_browser == null ||
                _browser.state != FragmentCapture.State.Initialized)
                return;
            int version = ++_focusProbeVersion;
            float nx = Mathf.Clamp01(webPoint.x / (float)_browser.viewSize.x);
            float ny = Mathf.Clamp01(webPoint.y / (float)_browser.viewSize.y);
            StartCoroutine(ProbeFocusedEditableRoutine(version, nx, ny));
        }

        private IEnumerator ProbeFocusedEditableRoutine(
            int version,
            float normalizedX,
            float normalizedY)
        {
            string x = normalizedX.ToString("R", CultureInfo.InvariantCulture);
            string y = normalizedY.ToString("R", CultureInfo.InvariantCulture);
            string script =
                "var edit=function(e){return !!e&&(e.tagName==='INPUT'||" +
                "e.tagName==='TEXTAREA'||e.isContentEditable);};" +
                "var e=document.activeElement;" +
                "if(!edit(e)){var p=document.elementFromPoint(" +
                "window.innerWidth*" + x + ",window.innerHeight*" + y + ");" +
                "if(p&&p.tagName==='LABEL'&&p.control)p=p.control;" +
                "while(p&&!edit(p))p=p.parentElement;" +
                "if(edit(p)){p.focus();e=p;}}" +
                "tlab.postResult(xrResultId,edit(e));";
            foreach (JavaAsyncResult result in
                _browser.EvaluateJSForResult("xrResultId", script))
            {
                if (version != _focusProbeVersion) yield break;
                if (result == null)
                {
                    yield return null;
                    continue;
                }
                if (result.status == JavaAsyncResult.Status.COMPLETE && result.b)
                {
                    Debug.Log("[XrLab] editable HTML field focused; opening XR keyboard.");
                    _shell.BeginWebContentEntry(this);
                }
                else if (result.status == JavaAsyncResult.Status.COMPLETE)
                {
                    Debug.Log("[XrLab] web tap completed without editable focus.");
                }
            }
        }

        internal void SelectFromContent() => _shell.SetActiveBrowser(this);

        private Button MakeHeaderButton(
            string label,
            float x,
            UnityEngine.Events.UnityAction action)
        {
            Button button = WorldCreatorLabShell.MakeButton(
                _rect,
                label,
                new Vector2(x, 334f),
                new Vector2(48f, 48f),
                action);
            BoxCollider collider = button.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(48f, 48f, 12f);
            return button;
        }

        private XrLabVolumeSlider MakeVolumeSlider(float x)
        {
            Image track = MakeSurface(
                _rect,
                "Browser volume",
                new Vector2(x, 334f),
                new Vector2(42f, 50f),
                new Color(.20f, .21f, .25f, .92f));
            track.raycastTarget = true;
            BoxCollider trackCollider = track.gameObject.AddComponent<BoxCollider>();
            trackCollider.size = new Vector3(42f, 50f, 12f);

            Image speaker = MakeSurface(
                track.transform, "Volume speaker",
                new Vector2(-7f, 0f), new Vector2(23f, 23f),
                new Color(.88f, .90f, .96f, .96f));
            speaker.sprite = XrLabSprites.Speaker;
            speaker.type = Image.Type.Simple;
            speaker.raycastTarget = false;
            Image rail = MakeSurface(
                track.transform, "Volume rail",
                new Vector2(13f, 0f), new Vector2(5f, 36f),
                new Color(.44f, .46f, .54f, .55f));
            rail.raycastTarget = false;
            Image fill = MakeSurface(
                track.transform,
                "Volume level",
                new Vector2(13f, -15f),
                new Vector2(5f, 8f),
                new Color(.92f, .94f, 1f, .98f));
            fill.raycastTarget = false;

            XrLabVolumeSlider slider = track.gameObject.AddComponent<XrLabVolumeSlider>();
            slider.Configure(fill, 13f);
            return slider;
        }

        private static void LayoutSurface(Image image, Vector2 position, Vector2 size)
        {
            if (image == null) return;
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
        }

        private static void LayoutButton(Button button, Vector2 position)
        {
            if (button == null) return;
            RectTransform rect = button.transform as RectTransform;
            if (rect != null) rect.anchoredPosition = position;
        }

        private static void LayoutVolumeSlider(
            XrLabVolumeSlider slider,
            Vector2 position)
        {
            if (slider == null) return;
            RectTransform rect = slider.transform as RectTransform;
            if (rect != null) rect.anchoredPosition = position;
        }

        private static Image MakeSurface(
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
            image.sprite = XrLabSprites.Rounded;
            image.type = Image.Type.Sliced;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return image;
        }

        private static TextMeshProUGUI MakeLabel(
            Transform parent,
            string text,
            Vector2 position,
            Vector2 size,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return label;
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
            return value.Substring(0, max - 1) + "…";
        }
    }

    /// <summary>
    /// Compact media-volume fader used only by browser windows. The same XR
    /// pinch that clicks a button can be held and moved vertically: up raises
    /// Android's music stream, down lowers it. No phone controller is required.
    /// </summary>
    public sealed class XrLabVolumeSlider : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        private Image _track;
        private Image _fill;
        private float _fillX;
        private float _normalized;
        private bool _dragging;

        public void Configure(Image fill, float fillX)
        {
            _track = GetComponent<Image>();
            _fill = fill;
            _fillX = fillX;
            _normalized = ReadNormalizedVolume();
            UpdateVisual(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _normalized = ReadNormalizedVolume();
            UpdateVisual(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_dragging) UpdateVisual(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _dragging = true;
            SetFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData) => SetFromPointer(eventData);

        public void OnPointerUp(PointerEventData eventData)
        {
            SetFromPointer(eventData);
            _dragging = false;
            UpdateVisual(false);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
            UpdateVisual(false);
        }

        public void OnPointerClick(PointerEventData eventData) { }

        private void SetFromPointer(PointerEventData eventData)
        {
            if (!(transform is RectTransform rect) || eventData == null) return;
            Vector3 world = eventData.pointerCurrentRaycast.worldPosition;
            Vector3 local = rect.InverseTransformPoint(world);
            float value = Mathf.InverseLerp(
                rect.rect.yMin + 5f,
                rect.rect.yMax - 5f,
                local.y);
            SetNormalizedVolume(value);
            _normalized = value;
            UpdateVisual(true);
        }

        private void UpdateVisual(bool active)
        {
            if (_fill != null)
            {
                float height = Mathf.Lerp(4f, 36f, Mathf.Clamp01(_normalized));
                _fill.rectTransform.sizeDelta = new Vector2(5f, height);
                _fill.rectTransform.anchoredPosition =
                    new Vector2(_fillX, -18f + height * .5f);
                _fill.color = active
                    ? Color.white
                    : new Color(.82f, .86f, .96f, .96f);
            }
            if (_track != null)
                _track.color = active
                    ? new Color(.40f, .42f, .49f, .96f)
                    : new Color(.22f, .23f, .27f, .90f);
        }

        private static float ReadNormalizedVolume()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unity = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unity.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject audio =
                    activity.Call<AndroidJavaObject>("getSystemService", "audio");
                const int streamMusic = 3;
                int current = audio.Call<int>("getStreamVolume", streamMusic);
                int maximum = audio.Call<int>("getStreamMaxVolume", streamMusic);
                return maximum <= 0 ? 0f : Mathf.Clamp01(current / (float)maximum);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[XrLab] media volume read failed: " + exception.Message);
                return AudioListener.volume;
            }
#else
            return AudioListener.volume;
#endif
        }

        private static void SetNormalizedVolume(float normalized)
        {
            normalized = Mathf.Clamp01(normalized);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unity = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unity.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject audio =
                    activity.Call<AndroidJavaObject>("getSystemService", "audio");
                const int streamMusic = 3;
                int maximum = audio.Call<int>("getStreamMaxVolume", streamMusic);
                int target = Mathf.RoundToInt(normalized * Mathf.Max(1, maximum));
                audio.Call("setStreamVolume", streamMusic, target, 0);
                return;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[XrLab] media volume write failed: " + exception.Message);
            }
#endif
            AudioListener.volume = normalized;
        }
    }

    public sealed class XrLabBrowserPointer : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler,
        IDragHandler,
        IScrollHandler
    {
        private XrLabBrowserWindow _window;
        private XrLabWebView _browser;
        private RectTransform _rect;
        private RawImage _surface;
        private long _downTime;
        private Vector2Int _lastWebPoint;
        private float _dragDistance;
        private bool _scrolling;

        public void Configure(
            XrLabBrowserWindow window,
            XrLabWebView browser,
            RectTransform rect)
        {
            _window = window;
            _browser = browser;
            _rect = rect;
            _surface = rect != null ? rect.GetComponent<RawImage>() : null;
        }

        public void OnPointerEnter(PointerEventData eventData) { }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_downTime == 0) return;
            if (!_scrolling) Send(eventData, 1);
            _downTime = 0;
            _scrolling = false;
            _dragDistance = 0f;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _window?.SelectFromContent();
            _lastWebPoint = WebPoint(eventData);
            _dragDistance = 0f;
            _scrolling = false;
            _downTime = Send(_lastWebPoint, 0);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            bool wasScrolling = _scrolling;
            Vector2Int point = WebPoint(eventData);
            if (!wasScrolling) Send(point, 1);
            _downTime = 0;
            _scrolling = false;
            _dragDistance = 0f;
            if (!wasScrolling) _window?.ProbeFocusedEditable(point);
        }

        public void OnPointerClick(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (_downTime == 0) return;
            Vector2Int current = WebPoint(eventData);
            Vector2Int delta = current - _lastWebPoint;
            _dragDistance += Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            if (!_scrolling && _dragDistance >= 16f)
            {
                // A held pinch must scroll, not trigger Chromium's long-press
                // text selection/magnifier. Cancel the native tap and drive the
                // WebView scroll explicitly from the same proven XR ray.
                Send(current, 3); // MotionEvent.ACTION_CANCEL
                _scrolling = true;
            }
            if (_scrolling)
            {
                // Deliberately no head-driven scrolling. Once a pinched gaze
                // moves far enough, cancel the tap and wait for release. Page
                // motion is owned exclusively by the index-only gesture.
            }
            else
            {
                Send(current, 2);
            }
            _lastWebPoint = current;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_browser == null || eventData == null) return;
            Vector2Int point = WebPoint(eventData);
            int deltaY = Mathf.Clamp(
                Mathf.RoundToInt(-eventData.scrollDelta.y * 14f),
                -520,
                520);
            if (deltaY == 0) return;
            _window?.SelectFromContent();
            _browser.ScrollAt(point, deltaY);
            _browser.RequestNativeRedraw();
        }

        private long Send(PointerEventData eventData, int action)
        {
            return Send(WebPoint(eventData), action);
        }

        private Vector2Int WebPoint(PointerEventData eventData)
        {
            if (_browser == null || _rect == null || eventData == null)
                return Vector2Int.zero;
            Vector3 world = eventData.pointerCurrentRaycast.worldPosition;
            Vector3 local = _rect.InverseTransformPoint(world);
            float nx = Mathf.Clamp01(local.x / _rect.rect.width + _rect.pivot.x);
            float ny = 1f - Mathf.Clamp01(
                local.y / _rect.rect.height + _rect.pivot.y);
            // RawImage may display only the video sub-rectangle in XR mode.
            // Map the pointer through that UV crop so clicks still reach the
            // corresponding place in the untouched WebView.
            Rect uv = _surface != null ? _surface.uvRect : new Rect(0f, 0f, 1f, 1f);
            float u = uv.x + nx * uv.width;
            float v = uv.y + (1f - ny) * uv.height;
            int webX = Mathf.RoundToInt(u * _browser.viewSize.x);
            int webY = Mathf.RoundToInt((1f - v) * _browser.viewSize.y);
            return new Vector2Int(webX, webY);
        }

        private long Send(Vector2Int point, int action)
        {
            if (_browser == null) return 0;
            if (action == 0)
                Debug.Log($"[XrLab] web touch down {point.x},{point.y}");
            long downTime = _browser.TouchEvent(
                point.x,
                point.y,
                action,
                _downTime);
            _browser.RequestNativeRedraw();
            return downTime;
        }
    }

    internal static class XrLabAndroidIconLoader
    {
        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>();

        public static Sprite TryLoad(string packageName)
        {
            if (Cache.TryGetValue(packageName, out Sprite cached)) return cached;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject manager = activity.Call<AndroidJavaObject>(
                    "getPackageManager");
                using AndroidJavaObject drawable = manager.Call<AndroidJavaObject>(
                    "getApplicationIcon",
                    packageName);
                using var bitmapClass = new AndroidJavaClass("android.graphics.Bitmap");
                using var configClass = new AndroidJavaClass("android.graphics.Bitmap$Config");
                using AndroidJavaObject config =
                    configClass.GetStatic<AndroidJavaObject>("ARGB_8888");
                using AndroidJavaObject bitmap = bitmapClass.CallStatic<AndroidJavaObject>(
                    "createBitmap",
                    128,
                    128,
                    config);
                using var canvas = new AndroidJavaObject("android.graphics.Canvas", bitmap);
                drawable.Call("setBounds", 0, 0, 128, 128);
                drawable.Call("draw", canvas);
                using var stream = new AndroidJavaObject(
                    "java.io.ByteArrayOutputStream");
                using var compressFormat = new AndroidJavaClass(
                    "android.graphics.Bitmap$CompressFormat");
                using AndroidJavaObject png = compressFormat.GetStatic<AndroidJavaObject>("PNG");
                bitmap.Call<bool>("compress", png, 100, stream);
                byte[] bytes = stream.Call<byte[]>("toByteArray");
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = "Android icon " + packageName,
                };
                if (!texture.LoadImage(bytes, false))
                {
                    UnityEngine.Object.Destroy(texture);
                    Cache[packageName] = null;
                    return null;
                }
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(.5f, .5f),
                    100f);
                Cache[packageName] = sprite;
                return sprite;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[XrLab] app icon unavailable " + packageName + ": " +
                    exception.Message);
            }
#endif
            Cache[packageName] = null;
            return null;
        }
    }

    internal static class XrLabSprites
    {
        private static Sprite _rounded;
        private static Sprite _circle;
        private static Sprite _speaker;

        public static Sprite Rounded =>
            _rounded ??= Build(false, "XR Lab rounded glass");
        public static Sprite Circle =>
            _circle ??= Build(true, "XR Lab app circle");
        public static Sprite Speaker =>
            _speaker ??= BuildSpeaker();

        private static Sprite BuildSpeaker()
        {
            const int size = 64;
            var texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false, true)
            {
                name = "XR Lab speaker",
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
                        cone = y >= Mathf.Lerp(25f, 14f, t) &&
                            y <= Mathf.Lerp(39f, 50f, t);
                    }
                    float dx = x - 35f;
                    float dy = y - 32f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    bool wave = x >= 38 &&
                        (Mathf.Abs(distance - 13f) < 1.45f ||
                         Mathf.Abs(distance - 22f) < 1.45f);
                    texture.SetPixel(
                        x, y,
                        new Color(1f, 1f, 1f, body || cone || wave ? 1f : 0f));
                }
            }
            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f);
        }

        private static Sprite Build(bool circle, string name)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            float radius = circle ? 30f : 13f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x - 31.5f) - (31.5f - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y - 31.5f) - (31.5f - radius), 0f);
                    float distance = circle
                        ? Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) - 30f
                        : Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    byte alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(.5f - distance) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Vector4 border = circle
                ? Vector4.zero
                : new Vector4(13f, 13f, 13f, 13f);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
        }
    }

}