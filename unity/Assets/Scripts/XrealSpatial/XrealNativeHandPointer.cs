using System.Collections.Generic;
using MLOmega.XR.Core;
using MLOmega.XR.Reflex;
using MLOmega.XR.UI.Components;
using Unity.XR.XREAL;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Native XREAL/XR Hands pointer for the product menu and the isolated
    /// World Atelier deck. On One Pro + Eye, where the SDK exposes no native
    /// hand subsystem, the existing gaze ray remains the pointer and the
    /// on-device MediaPipe pinch becomes its select/grab button.
    ///
    /// Point with the index and pinch thumb-to-index to click. Two thresholds
    /// provide hysteresis so a noisy pinch cannot emit repeated clicks. Touch
    /// input remains active as a fallback.
    /// </summary>
    public sealed partial class XrealNativeHandPointer :
        MonoBehaviour,
        IWorldCreatorInteractionSettings
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private WorldCreatorController _creator;
        [SerializeField] private GestureBridge _eyeGestures;
        [SerializeField] private StreamingAssetsModelInstaller _modelInstaller;
        [SerializeField] private bool _activateEyeGesturesContinuously;
        [SerializeField] private bool _allowPhoneController = true;

        private readonly List<XRHandSubsystem> _subsystems =
            new List<XRHandSubsystem>();
        private readonly List<XREALSessionSubsystem> _sessionSubsystems =
            new List<XREALSessionSubsystem>();
        private readonly List<RaycastResult> _uiHits =
            new List<RaycastResult>(16);
        private readonly RaycastHit[] _physicalUiHits = new RaycastHit[64];
        private XROrigin _origin;
        private MenuPanel _menu;
        private EventSystem _events;
        private PointerEventData _pointer;
        private GameObject _hover;
        private GameObject _pressed;
        private LineRenderer _laser;
        private Transform _cursor;
        private Transform _cursorDot;
        private bool _pinching;
        private bool _hasSmoothedRay;
        private bool _loggedRunningSubsystem;
        private bool _loggedTrackedHand;
        private bool _phoneControllerSubscribed;
        private bool _phoneTouchActive;
        private bool _phoneTriggerPressed;
        private bool _eyePinching;
        private bool _deckPinchClaimed;
        private bool _releasingPointer;
        private Vector2 _eyeGesturePoint = new Vector2(-1f, -1f);
        private float _eyeGestureZoom = 1f;
        private Vector2 _phonePointerViewport = new Vector2(.5f, .5f);
        private XREALVirtualController _phoneController;
        private Vector3 _smoothedOrigin;
        private Vector3 _smoothedDirection;
        private float _nextSubsystemLookupAt;
        private float _nextDeviceStatusAt;
        private XREALSessionSubsystem _trackingSession;
        private float _trackingLossBeganAt = -1f;
        private float _trackingGoodBeganAt = -1f;
        private bool _trackingWarningVisible;
        private float _indexScrollTravel;
        private bool _indexScrollDispatched;
        private GameObject _indexScrollTarget;
        private string _trackingBadReason = "INITIALISATION";
        private bool _rayVisible;
        private string _trackingStatus = "TRACKING // INITIALISATION";
        private string _glassesTemperatureStatus = "XREAL // TEMP NORMALE";
        private const string RayVisiblePreference =
            "mlomega.atelier.eye_ray_visible.v1";
        private const string HandLowLightPreference =
            "mlomega.atelier.hand_low_light.v1";

        public bool IsRayVisible => _rayVisible;
        public bool IsGestureStandby =>
            _eyeGestures != null && _eyeGestures.IsInteractionStandby;
        public bool IsHeadOnlyModeEnabled => _headOnlyEnabled;
        public bool IsHeadOnlyInteractionActive => _headOnlyInteractionActive;
        public string TrackingStatus => _trackingStatus;
        public string GlassesTemperatureStatus => _glassesTemperatureStatus;
        public HandLowLightMode CurrentHandLowLightMode =>
            _eyeGestures != null
                ? _eyeGestures.LowLightMode
                : HandLowLightMode.Off;

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
            if (_creator == null)
                _creator = FindAnyObjectByType<WorldCreatorController>();
            _menu = FindAnyObjectByType<MenuPanel>();
            _origin = FindAnyObjectByType<XROrigin>();
            if (_eyeGestures == null)
                _eyeGestures = FindAnyObjectByType<GestureBridge>();
            if (_modelInstaller == null)
                _modelInstaller = FindAnyObjectByType<StreamingAssetsModelInstaller>();
            // A point cursor is enough for precise gaze+pinch selection. Keep the
            // long Eye ray opt-in because it is visually intrusive in OST lenses.
            _rayVisible = PlayerPrefs.GetInt(RayVisiblePreference, 0) == 1;
            InitializeHeadOnlyMode();
            _eyeGestures?.SetLowLightMode((HandLowLightMode)Mathf.Clamp(
                PlayerPrefs.GetInt(HandLowLightPreference, 0),
                0,
                2));
            EnsurePointerInfrastructure();
            BuildCursor();
        }

        public void SetRayVisible(bool visible)
        {
            _rayVisible = visible;
            PlayerPrefs.SetInt(RayVisiblePreference, visible ? 1 : 0);
            PlayerPrefs.Save();
            if (_laser != null && !visible) _laser.enabled = false;
        }

        public void ToggleRayVisible() => SetRayVisible(!_rayVisible);

        public void CycleHandLowLightMode()
        {
            if (_eyeGestures == null)
                _eyeGestures = FindAnyObjectByType<GestureBridge>();
            if (_eyeGestures == null) return;
            HandLowLightMode next = _eyeGestures.LowLightMode switch
            {
                HandLowLightMode.Off => HandLowLightMode.Light,
                HandLowLightMode.Light => HandLowLightMode.Strong,
                _ => HandLowLightMode.Off,
            };
            _eyeGestures.SetLowLightMode(next);
            PlayerPrefs.SetInt(HandLowLightPreference, (int)next);
            PlayerPrefs.Save();
        }

        public void SetGestureStandby(bool standby)
        {
            if (_eyeGestures == null)
                _eyeGestures = FindAnyObjectByType<GestureBridge>();
            if (_eyeGestures == null) return;
            _eyeGestures.SetInteractionStandby(standby);
            _eyePinching = false;
            ResetIndexScroll();
            if (_deckPinchClaimed && _creator != null)
                _creator.EndDeckManipulation();
            _deckPinchClaimed = false;
            ReleasePointer(false);
            if (_creator != null) _creator.SetGestureStandby(standby);
        }

        private void OnEnable()
        {
            XREALCallbackHandler.OnXREALGlassesTemperatureLevel -=
                OnGlassesTemperatureLevel;
            XREALCallbackHandler.OnXREALGlassesTemperatureLevel +=
                OnGlassesTemperatureLevel;
            if (_eyeGestures == null)
                _eyeGestures = FindAnyObjectByType<GestureBridge>();
            if (_eyeGestures != null)
                _eyeGestures.GestureRecognized += OnEyeGesture;
            if (!_activateEyeGesturesContinuously || _eyeGestures == null) return;
            if (_modelInstaller == null)
                _modelInstaller = FindAnyObjectByType<StreamingAssetsModelInstaller>();
            if (_modelInstaller == null || _modelInstaller.Done)
                ActivateEyeGestures();
            else
                _modelInstaller.Completed += ActivateEyeGestures;
        }

        private void OnDisable()
        {
            XREALCallbackHandler.OnXREALGlassesTemperatureLevel -=
                OnGlassesTemperatureLevel;
            if (_modelInstaller != null)
                _modelInstaller.Completed -= ActivateEyeGestures;
            if (_eyeGestures != null)
            {
                _eyeGestures.GestureRecognized -= OnEyeGesture;
                if (_activateEyeGesturesContinuously)
                    _eyeGestures.Deactivate();
            }
            _eyePinching = false;
            ResetHeadOnlyDwell(false);
            ResetIndexScroll();
            if (_deckPinchClaimed && _creator != null)
                _creator.EndDeckManipulation();
            _deckPinchClaimed = false;
            UnsubscribePhoneController();
            ReleasePointer(false);
            SetCursorVisible(false);
        }

        private void OnDestroy()
        {
            DestroyHeadOnlyVisuals();
            if (_laser != null && _laser.material != null)
                Destroy(_laser.material);
            if (_cursor != null)
            {
                var renderer = _cursor.GetComponent<Renderer>();
                if (renderer != null && renderer.material != null)
                    Destroy(renderer.material);
            }
        }

        private void Update()
        {
            UpdateDeviceStatus();
            EnsurePointerInfrastructure();
            UpdateHeadMotionMetrics();
            if (_allowPhoneController)
                EnsurePhoneController();
            else if (_phoneControllerSubscribed)
                UnsubscribePhoneController();
            // A closed Atelier must be optically empty. The palm callback still
            // runs through GestureBridge and can reopen it without a cursor.
            // Head-only is an explicit fallback, not a dormant wake mode. Once
            // enabled its gaze cursor must remain available continuously so a
            // user whose hand is not visible can still reach every control.
            if (_headOnlyEnabled && !_headOnlyInteractionActive)
            {
                UpdateHeadOnlyPassiveActivation();
                ResetIndexScroll();
                ReleasePointer(false);
                SetCursorVisible(false);
                _hasSmoothedRay = false;
                return;
            }
            if (_creator != null && _creator.IsDeckClosed && !_headOnlyEnabled)
            {
                ResetIndexScroll();
                ReleasePointer(false);
                SetCursorVisible(false);
                _hasSmoothedRay = false;
                return;
            }
            // In low-power gesture standby, hide the Eye ray/cursor completely.
            // A deliberate S24 touch remains an available independent fallback.
            if (
                _eyeGestures != null &&
                _eyeGestures.IsInteractionStandby &&
                !_headOnlyEnabled &&
                (!_allowPhoneController ||
                 (!_phoneTouchActive && !_phoneTriggerPressed)))
            {
                ReleasePointer(false);
                SetCursorVisible(false);
                _hasSmoothedRay = false;
                return;
            }
            bool hasPointer;
            Ray handRay;
            bool pinching;
            if (_headOnlyEnabled)
            {
                hasPointer = TryGetGazePointer(out handRay, out pinching);
                pinching = false;
            }
            else
            {
                hasPointer = TryGetHandRay(out handRay, out pinching);
            }
            // A subscribed XREALVirtualController exists even while the S24
            // touch surface is idle.  Treating that idle singleton as a live
            // pointer made it permanently win over head gaze, so Eye pinches
            // clicked the last phone coordinate instead of what the user was
            // looking at.  Phone input still takes priority while it is
            // actively touched/pressed, then gaze resumes automatically.
            if (
                !_headOnlyEnabled &&
                _allowPhoneController &&
                !hasPointer &&
                (_phoneTouchActive || _phoneTriggerPressed))
                hasPointer = TryGetPhonePointer(out handRay, out pinching);
            if (!hasPointer && !_headOnlyEnabled)
                hasPointer = TryGetGazePointer(out handRay, out pinching);
            if (!hasPointer && !_headOnlyEnabled && _allowPhoneController)
                hasPointer = TryGetPhonePointer(out handRay, out pinching);
            if (
                _camera == null ||
                _events == null ||
                !hasPointer)
            {
                ReleasePointer(false);
                SetCursorVisible(false);
                _hasSmoothedRay = false;
                return;
            }

            float blend = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
            if (!_hasSmoothedRay)
            {
                _smoothedOrigin = handRay.origin;
                _smoothedDirection = handRay.direction;
                _hasSmoothedRay = true;
            }
            else
            {
                _smoothedOrigin = Vector3.Lerp(
                    _smoothedOrigin, handRay.origin, blend);
                _smoothedDirection = Vector3.Slerp(
                    _smoothedDirection, handRay.direction, blend).normalized;
            }
            handRay = new Ray(_smoothedOrigin, _smoothedDirection);

            Vector2 screenPoint = default;
            Vector3 worldHit = default;
            GameObject physicalTarget = null;
            bool physicalHit = TryGetPhysicalUiTarget(
                handRay,
                out worldHit,
                out physicalTarget);
            bool deckHit = physicalHit;
            if (physicalHit)
            {
                screenPoint = RectTransformUtility.WorldToScreenPoint(
                    _camera,
                    worldHit);
            }
            else
            {
                deckHit =
                    _creator != null &&
                    _creator.TryProjectDeckPointer(
                        handRay,
                        out screenPoint,
                        out worldHit);
            }
            if (!deckHit)
            {
                Vector3 projected =
                    _camera.WorldToScreenPoint(handRay.GetPoint(2.5f));
                if (projected.z <= 0f)
                {
                    ReleasePointer(false);
                    SetCursorVisible(false);
                    return;
                }
                screenPoint = new Vector2(projected.x, projected.y);
                worldHit = handRay.GetPoint(1.25f);
            }

            UpdateEventPointer(
                screenPoint,
                deckHit,
                worldHit,
                physicalTarget);
            UpdateProductMenu(screenPoint, pinching);
            if (_creator != null)
                _creator.UpdateDeckManipulationHover(worldHit, deckHit);
            SetCursor(
                handRay.origin,
                worldHit,
                deckHit || (_menu != null && _menu.IsOpen),
                pinching);

            if (_headOnlyEnabled)
            {
                UpdateHeadOnlyDwell(worldHit, screenPoint, deckHit);
                return;
            }

            if (pinching && !_pinching)
            {
                Debug.Log(
                    "[XrealNativeHandPointer] pinch press: " +
                    $"deckHit={deckHit}, hover={(_hover != null ? _hover.name : "<none>")}, " +
                    $"gaze={_eyePinching}, phone={_phoneTriggerPressed}");
                _deckPinchClaimed =
                    _eyePinching &&
                    _creator != null &&
                    _creator.TryBeginDeckManipulation(
                        worldHit,
                        _eyeGesturePoint,
                        _eyeGestureZoom);
                if (!_deckPinchClaimed) PressPointer();
            }
            else if (pinching && _pinching && _deckPinchClaimed)
            {
                _creator.UpdateDeckManipulation(
                    _eyeGesturePoint,
                    _eyeGestureZoom);
            }
            else if (pinching && _pinching)
            {
                ContinuePointerDrag();
            }
            else if (!pinching && _pinching)
            {
                if (_deckPinchClaimed)
                    _creator.EndDeckManipulation();
                else
                    ReleasePointer(true);
                _deckPinchClaimed = false;
            }
            _pinching = pinching;
        }

        private void UpdateDeviceStatus()
        {
            // XREAL's own "look around" popup samples its native tracking
            // reason every frame. The former 750 ms polling could miss that
            // short transition and leave our gauge green. Cache subsystem
            // discovery, but sample the native-backed state every rendered
            // frame and latch a loss long enough for the 1 Hz UI to show it.
            if (
                _trackingSession == null ||
                !_trackingSession.running ||
                Time.unscaledTime >= _nextDeviceStatusAt)
            {
                _nextDeviceStatusAt = Time.unscaledTime + .75f;
                _sessionSubsystems.Clear();
                SubsystemManager.GetSubsystems(_sessionSubsystems);
                _trackingSession = null;
                for (int i = 0; i < _sessionSubsystems.Count; i++)
                {
                    if (_sessionSubsystems[i] == null) continue;
                    if (_trackingSession == null)
                        _trackingSession = _sessionSubsystems[i];
                    if (_sessionSubsystems[i].running)
                    {
                        _trackingSession = _sessionSubsystems[i];
                        break;
                    }
                }
            }
            XREALSessionSubsystem session = _trackingSession;
            if (session == null)
            {
                UpdateTrackingWarning(true, "INDISPONIBLE");
                _creator?.ReportSpatialTrackingState(false, "INDISPONIBLE");
                return;
            }
            bool reasonIsClear =
                session.notTrackingReason == NotTrackingReason.None;
            bool headPoseExplicitlyLost = false;
            UnityEngine.XR.InputDevice head =
                UnityEngine.XR.InputDevices.GetDeviceAtXRNode(
                    UnityEngine.XR.XRNode.Head);
            if (head.isValid)
            {
                if (
                    head.TryGetFeatureValue(
                        UnityEngine.XR.CommonUsages.isTracked,
                        out bool isTracked) &&
                    !isTracked)
                    headPoseExplicitlyLost = true;
                if (
                    head.TryGetFeatureValue(
                        UnityEngine.XR.CommonUsages.trackingState,
                        out UnityEngine.XR.InputTrackingState inputState) &&
                    inputState == UnityEngine.XR.InputTrackingState.None)
                    headPoseExplicitlyLost = true;
            }
            bool trackingLost =
                session.trackingState != TrackingState.Tracking ||
                !reasonIsClear ||
                headPoseExplicitlyLost;
            string rawReason = trackingLost
                ? (headPoseExplicitlyLost
                    ? "POSE PERDUE"
                    : session.notTrackingReason switch
                    {
                        NotTrackingReason.InsufficientLight =>
                            "LUMIÈRE INSUFFISANTE",
                        NotTrackingReason.InsufficientFeatures =>
                            "PEU DE REPÈRES",
                        NotTrackingReason.ExcessiveMotion =>
                            "MOUVEMENT EXCESSIF",
                        NotTrackingReason.Relocalizing => "RELOCALISATION",
                        NotTrackingReason.Initializing => "INITIALISATION",
                        NotTrackingReason.CameraUnavailable =>
                            "CAMÉRA INDISPONIBLE",
                        _ => session.trackingState.ToString().ToUpperInvariant(),
                    })
                : "OK";
            UpdateTrackingWarning(trackingLost, rawReason);
            _creator?.ReportSpatialTrackingState(!trackingLost, rawReason);
        }

        private void UpdateTrackingWarning(bool trackingLost, string reason)
        {
            float now = Time.unscaledTime;
            if (trackingLost)
            {
                _trackingGoodBeganAt = -1f;
                if (_trackingLossBeganAt < 0f) _trackingLossBeganAt = now;
                _trackingBadReason = reason;
                // Ignore native one-frame relocalisation chatter. The warning is
                // useful only after a sustained loss, not every brief Look Around.
                if (now - _trackingLossBeganAt >= 1.15f)
                    _trackingWarningVisible = true;
            }
            else
            {
                _trackingLossBeganAt = -1f;
                if (_trackingGoodBeganAt < 0f) _trackingGoodBeganAt = now;
                if (_trackingWarningVisible && now - _trackingGoodBeganAt >= .65f)
                    _trackingWarningVisible = false;
            }
            _trackingStatus = _trackingWarningVisible
                ? "TRACKING // " + _trackingBadReason
                : "TRACKING // OK";
        }

        private void OnGlassesTemperatureLevel(XREALTemperatureLevel level)
        {
            _glassesTemperatureStatus = level switch
            {
                XREALTemperatureLevel.LEVEL_HOT => "XREAL // TEMP ÉLEVÉE",
                XREALTemperatureLevel.LEVEL_WARM => "XREAL // TEMP TIÈDE",
                _ => "XREAL // TEMP NORMALE",
            };
        }

        private void ActivateEyeGestures()
        {
            if (_modelInstaller != null)
                _modelInstaller.Completed -= ActivateEyeGestures;
            if (_activateEyeGesturesContinuously && _eyeGestures != null)
            {
                _eyeGestures.Activate();
                Debug.Log(
                    "[XrealNativeHandPointer] Eye MediaPipe armed: " +
                    "head gaze aims, physical hand pinch selects.");
            }
        }

        private void OnEyeGesture(GestureEvent ev)
        {
            if (_headOnlyEnabled) return;
            if (ev.ScreenPoint.x >= 0f && ev.ScreenPoint.y >= 0f)
                _eyeGesturePoint = ev.ScreenPoint;
            switch (ev.Kind)
            {
                case GestureKind.PinchBegin:
                case GestureKind.PinchUpdate:
                    if (ev.ZoomFactor > 0f)
                        _eyeGestureZoom = ev.ZoomFactor;
                    _eyePinching = true;
                    break;
                case GestureKind.PinchEnd:
                    _eyePinching = false;
                    break;
                case GestureKind.OpenPalmMenu:
                    if (_creator != null)
                        _creator.OpenDeckFromPalm();
                    break;
                case GestureKind.TwoPalmMenu:
                    if (GestureBridge.TryHandleTwoPalmOverride())
                        break;
                    if (_creator != null)
                        _creator.OpenWindowDockFromTwoPalms();
                    break;
                case GestureKind.FistToggle:
                    _eyePinching = false;
                    ResetIndexScroll();
                    if (_deckPinchClaimed && _creator != null)
                        _creator.EndDeckManipulation();
                    _deckPinchClaimed = false;
                    ReleasePointer(false);
                    if (_creator != null && _eyeGestures != null)
                        _creator.SetGestureStandby(
                            _eyeGestures.IsInteractionStandby);
                    break;
                case GestureKind.IndexScrollBegin:
                    _eyePinching = false;
                    ReleasePointer(false);
                    _indexScrollTravel = 0f;
                    _indexScrollDispatched = false;
                    // Keep the page selected at the beginning of the stroke.
                    // During a downward hand movement the head gaze commonly
                    // leaves the transparent Android hit surface for a frame;
                    // requiring the live hover at dispatch time used to discard
                    // precisely that otherwise valid reverse gesture.
                    _indexScrollTarget = _hover;
                    break;
                case GestureKind.IndexScrollUpdate:
                    if (_indexScrollDispatched) break;
                    _indexScrollTravel += ev.ZoomFactor;
                    // The native pose already survived a timed one-index gate.
                    // A 5.2% frame-height stroke is deliberate while remaining
                    // comfortably above ordinary landmark jitter. The previous
                    // 7.5% threshold made the gesture needlessly exaggerated,
                    // especially near the lower edge of the Eye image.
                    if (Mathf.Abs(_indexScrollTravel) >= .045f)
                    {
                        _indexScrollDispatched = true;
                        DispatchIndexScroll(Mathf.Sign(_indexScrollTravel));
                    }
                    break;
                case GestureKind.IndexScrollEnd:
                    ResetIndexScroll();
                    break;
                case GestureKind.TwoFingerKeyboard:
                    _creator?.ToggleLabKeyboardFromGesture();
                    break;
                case GestureKind.ThumbUpQuickMenu:
                    _creator?.ToggleQuickMenuFromThumb();
                    break;
            }
        }

        private void DispatchIndexScroll(float direction)
        {
            if (Mathf.Abs(direction) < .5f) return;
            GameObject target = _hover != null ? _hover : _indexScrollTarget;
            if (target == null) return;
            EnsurePointerInfrastructure();
            if (_pointer == null) return;
            // One decisive event per physical index stroke. Protected Android
            // windows turn this into their already-proven full swipe routine;
            // normal Unity ScrollRects consume the same wheel-style event.
            _pointer.scrollDelta = new Vector2(0f, -direction * 36f);
            ExecuteEvents.ExecuteHierarchy(
                target,
                _pointer,
                ExecuteEvents.scrollHandler);
            _pointer.scrollDelta = Vector2.zero;
        }

        private void ResetIndexScroll()
        {
            _indexScrollTravel = 0f;
            _indexScrollDispatched = false;
            _indexScrollTarget = null;
        }

        private void EnsurePointerInfrastructure()
        {
            _events = EventSystem.current;
            if (_pointer == null && _events != null)
                _pointer = new PointerEventData(_events)
                {
                    pointerId = -22001,
                    button = PointerEventData.InputButton.Left,
                };
        }

        private bool TryGetHandRay(out Ray ray, out bool pinching)
        {
            ray = default;
            pinching = false;
            if (Time.unscaledTime >= _nextSubsystemLookupAt)
            {
                _nextSubsystemLookupAt = Time.unscaledTime + 1f;
                _subsystems.Clear();
                SubsystemManager.GetSubsystems(_subsystems);
            }
            foreach (XRHandSubsystem subsystem in _subsystems)
            {
                if (subsystem == null || !subsystem.running) continue;
                if (!_loggedRunningSubsystem)
                {
                    Debug.Log(
                        "[XrealNativeHandPointer] XR Hands subsystem running; " +
                        "point with an index and pinch to select.");
                    _loggedRunningSubsystem = true;
                }
                if (TryGetHandRay(subsystem.rightHand, out ray, out pinching))
                {
                    LogTrackedHandOnce("right");
                    return true;
                }
                if (TryGetHandRay(subsystem.leftHand, out ray, out pinching))
                {
                    LogTrackedHandOnce("left");
                    return true;
                }
            }
            return false;
        }

        private void EnsurePhoneController()
        {
            if (
                _phoneControllerSubscribed &&
                _phoneController == XREALVirtualController.Singleton)
                return;
            UnsubscribePhoneController();
            _phoneController = XREALVirtualController.Singleton;
            if (_phoneController == null) return;
            _phoneController.pointerDown += OnPhonePointerDown;
            _phoneController.pointerUp += OnPhonePointerUp;
            _phoneController.pointerDrag += OnPhonePointerDrag;
            _phoneController.pointerEndDrag += OnPhonePointerEndDrag;
            _phoneControllerSubscribed = true;
            Debug.Log(
                "[XrealNativeHandPointer] S24 touchpad fallback ready; " +
                "drag to aim and tap to select.");
        }

        private void UnsubscribePhoneController()
        {
            if (_phoneController != null && _phoneControllerSubscribed)
            {
                _phoneController.pointerDown -= OnPhonePointerDown;
                _phoneController.pointerUp -= OnPhonePointerUp;
                _phoneController.pointerDrag -= OnPhonePointerDrag;
                _phoneController.pointerEndDrag -= OnPhonePointerEndDrag;
            }
            _phoneControllerSubscribed = false;
            _phoneController = null;
            _phoneTouchActive = false;
            _phoneTriggerPressed = false;
        }

        private void OnPhonePointerDown(
            XREALButtonType type,
            GameObject target,
            PointerEventData eventData)
        {
            if (type == XREALButtonType.TriggerButton)
                _phoneTriggerPressed = true;
            if (type == XREALButtonType.Primary2DAxis)
            {
                _phoneTouchActive = true;
                UpdatePhoneViewport(target, eventData);
            }
        }

        private void OnPhonePointerUp(
            XREALButtonType type,
            GameObject target,
            PointerEventData eventData)
        {
            if (type == XREALButtonType.TriggerButton)
                _phoneTriggerPressed = false;
        }

        private void OnPhonePointerDrag(
            XREALButtonType type,
            GameObject target,
            PointerEventData eventData)
        {
            if (type != XREALButtonType.Primary2DAxis) return;
            _phoneTouchActive = true;
            UpdatePhoneViewport(target, eventData);
        }

        private void OnPhonePointerEndDrag(
            XREALButtonType type,
            GameObject target,
            PointerEventData eventData)
        {
            if (type == XREALButtonType.Primary2DAxis)
                _phoneTouchActive = false;
        }

        private void UpdatePhoneViewport(
            GameObject target,
            PointerEventData eventData)
        {
            if (
                target == null ||
                eventData == null ||
                !(target.transform is RectTransform rect) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 local))
                return;
            Rect bounds = rect.rect;
            float x = Mathf.InverseLerp(bounds.xMin, bounds.xMax, local.x);
            float y = Mathf.InverseLerp(bounds.yMin, bounds.yMax, local.y);
            // Leave a small comfort margin so the cursor cannot disappear
            // behind the optical display's edge.
            _phonePointerViewport = new Vector2(
                Mathf.Lerp(.06f, .94f, x),
                Mathf.Lerp(.06f, .94f, y));
        }

        private bool TryGetPhonePointer(out Ray ray, out bool pressing)
        {
            ray = default;
            pressing = false;
            if (_camera == null || !_phoneControllerSubscribed) return false;
            ray = _camera.ViewportPointToRay(new Vector3(
                _phonePointerViewport.x,
                _phonePointerViewport.y,
                0f));
            pressing = _phoneTriggerPressed || _eyePinching;
            // Keep the cursor visible at its last position so the user always
            // knows what a tap will select, even between touch movements.
            return true;
        }

        private bool TryGetGazePointer(out Ray ray, out bool pressing)
        {
            ray = default;
            pressing = false;
            if (
                _camera == null ||
                _eyeGestures == null ||
                !_eyeGestures.IsRunning ||
                (_eyeGestures.IsInteractionStandby && !_headOnlyEnabled))
                return false;
            ray = _camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
            pressing = _eyePinching;
            return true;
        }

        private bool TryGetPhysicalUiTarget(
            Ray ray,
            out Vector3 worldPoint,
            out GameObject target)
        {
            worldPoint = default;
            target = null;
            int count = Physics.RaycastNonAlloc(
                ray,
                _physicalUiHits,
                4f,
                ~0,
                QueryTriggerInteraction.Collide);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _physicalUiHits[i];
                if (hit.collider == null || hit.distance >= nearest) continue;
                GameObject handler =
                    ExecuteEvents.GetEventHandler<IPointerClickHandler>(
                        hit.collider.gameObject);
                if (handler == null) continue;
                nearest = hit.distance;
                worldPoint = hit.point;
                target = handler;
            }
            return target != null;
        }

        private void LogTrackedHandOnce(string handedness)
        {
            if (_loggedTrackedHand) return;
            Debug.Log(
                "[XrealNativeHandPointer] Native " + handedness +
                " hand tracked; pinch interaction is active.");
            _loggedTrackedHand = true;
        }

        private bool TryGetHandRay(
            XRHand hand,
            out Ray ray,
            out bool pinching)
        {
            ray = default;
            pinching = false;
            if (!hand.isTracked) return false;
            if (
                !TryWorldJoint(
                    hand, XRHandJointID.IndexProximal, out Vector3 indexBase) ||
                !TryWorldJoint(
                    hand, XRHandJointID.IndexTip, out Vector3 indexTip) ||
                !TryWorldJoint(
                    hand, XRHandJointID.ThumbTip, out Vector3 thumbTip) ||
                !TryWorldJoint(
                    hand, XRHandJointID.Wrist, out Vector3 wrist) ||
                !TryWorldJoint(
                    hand, XRHandJointID.MiddleProximal, out Vector3 middleBase))
                return false;
            Vector3 direction = indexTip - indexBase;
            if (direction.sqrMagnitude < .0001f) return false;

            float handScale = Mathf.Clamp(
                Vector3.Distance(wrist, middleBase), .055f, .11f);
            float engage = Mathf.Clamp(handScale * .32f, .018f, .031f);
            float release = Mathf.Clamp(engage * 1.45f, .027f, .045f);
            float pinchDistance = Vector3.Distance(indexTip, thumbTip);
            pinching = _pinching
                ? pinchDistance <= release
                : pinchDistance <= engage;
            ray = new Ray(indexBase, direction.normalized);
            return true;
        }

        private bool TryWorldJoint(
            XRHand hand,
            XRHandJointID id,
            out Vector3 world)
        {
            world = default;
            XRHandJoint joint = hand.GetJoint(id);
            if (!joint.TryGetPose(out Pose pose)) return false;
            Transform tracking =
                _origin != null ? _origin.TrackablesParent : null;
            world = tracking != null
                ? tracking.TransformPoint(pose.position)
                : pose.position;
            return true;
        }

        private void UpdateEventPointer(
            Vector2 screenPoint,
            bool raycastUi,
            Vector3 worldPoint,
            GameObject physicalTarget)
        {
            if (_pointer == null) return;
            _pointer.delta = screenPoint - _pointer.position;
            _pointer.position = screenPoint;
            GameObject next = physicalTarget;
            if (raycastUi)
            {
                // External Lab content is already intersected in world space.
                // Prefer that exact result over the S24 screen-space raycaster,
                // whose coordinates do not match an XR eye render target.
                if (
                    next == null &&
                    _creator != null &&
                    _creator.TryResolveExternalSpatialTarget(
                        worldPoint,
                        out next))
                {
                    _pointer.pointerCurrentRaycast = new RaycastResult
                    {
                        gameObject = next,
                        screenPosition = screenPoint,
                        worldPosition = worldPoint,
                    };
                }
                if (next == null)
                {
                    _uiHits.Clear();
                    _events.RaycastAll(_pointer, _uiHits);
                    foreach (RaycastResult hit in _uiHits)
                    {
                        if (hit.gameObject == null) continue;
                        next = hit.gameObject;
                        _pointer.pointerCurrentRaycast = hit;
                        break;
                    }
                }
                if (
                    next == null &&
                    _creator != null &&
                    _creator.TryResolveDeckTarget(worldPoint, out next))
                {
                    _pointer.pointerCurrentRaycast = new RaycastResult
                    {
                        gameObject = next,
                        screenPosition = screenPoint,
                        worldPosition = worldPoint,
                    };
                }
                else if (physicalTarget != null)
                {
                    _pointer.pointerCurrentRaycast = new RaycastResult
                    {
                        gameObject = physicalTarget,
                        screenPosition = screenPoint,
                        worldPosition = worldPoint,
                    };
                }
            }
            SetHover(next);
        }

        private void ContinuePointerDrag()
        {
            if (_pointer == null || _pressed == null) return;
            GameObject drag = ExecuteEvents.GetEventHandler<IDragHandler>(_pressed);
            if (drag == null) return;
            _pointer.pointerDrag = drag;
            _pointer.dragging = true;
            ExecuteEvents.Execute(drag, _pointer, ExecuteEvents.dragHandler);
        }

        private void SetHover(GameObject rawTarget)
        {
            GameObject next = rawTarget == null
                ? null
                : ExecuteEvents.GetEventHandler<IPointerEnterHandler>(
                    rawTarget);
            if (next == _hover) return;
            if (_hover != null)
                ExecuteEvents.Execute(
                    _hover, _pointer, ExecuteEvents.pointerExitHandler);
            _hover = next;
            _pointer.pointerEnter = next;
            if (_hover != null)
                ExecuteEvents.Execute(
                    _hover, _pointer, ExecuteEvents.pointerEnterHandler);
        }

        private void PressPointer()
        {
            if (_hover == null || _pointer == null) return;
            _pointer.pressPosition = _pointer.position;
            _pointer.pointerPressRaycast = _pointer.pointerCurrentRaycast;
            _pointer.eligibleForClick = true;
            _pressed = ExecuteEvents.ExecuteHierarchy(
                _hover, _pointer, ExecuteEvents.pointerDownHandler);
            if (_pressed == null)
                _pressed =
                    ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hover);
            _pointer.pointerPress = _pressed;
            _pointer.rawPointerPress = _hover;
            if (_pressed != null)
                _events.SetSelectedGameObject(_pressed, _pointer);
        }

        private void ReleasePointer(bool allowClick)
        {
            // A UI action can switch to head-only/passive mode from inside its
            // own pointerClick callback. Guard that re-entrant release so the
            // same button never receives a duplicate pointerUp/click.
            if (_releasingPointer) return;
            _releasingPointer = true;
            try
            {
                if (_pointer != null && _pressed != null)
                {
                    ExecuteEvents.Execute(
                        _pressed,
                        _pointer,
                        ExecuteEvents.pointerUpHandler);
                    GameObject click =
                        _hover == null
                            ? null
                            : ExecuteEvents.GetEventHandler<IPointerClickHandler>(
                                _hover);
                    if (
                        allowClick &&
                        _pointer.eligibleForClick &&
                        click == _pressed)
                    {
                        ExecuteEvents.Execute(
                            _pressed,
                            _pointer,
                            ExecuteEvents.pointerClickHandler);
                    }
                }
                if (_pointer != null)
                {
                    if (_pointer.dragging && _pointer.pointerDrag != null)
                        ExecuteEvents.Execute(
                            _pointer.pointerDrag,
                            _pointer,
                            ExecuteEvents.endDragHandler);
                    _pointer.eligibleForClick = false;
                    _pointer.pointerPress = null;
                    _pointer.rawPointerPress = null;
                    _pointer.pointerDrag = null;
                    _pointer.dragging = false;
                }
                _pressed = null;
                _pinching = false;
            }
            finally
            {
                _releasingPointer = false;
            }
        }

        private void UpdateProductMenu(
            Vector2 screenPoint,
            bool pinching)
        {
            if (_menu == null || !_menu.IsOpen || _camera == null) return;
            Vector2 viewport = new Vector2(
                Mathf.Clamp01(screenPoint.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(screenPoint.y / Mathf.Max(1f, Screen.height)));
            _menu.HoverAtViewport(viewport);
            if (_pinching && !pinching) _menu.PinchCommit();
        }

        private void BuildCursor()
        {
            var line = new GameObject("XREAL Hand Ray");
            line.transform.SetParent(transform, false);
            _laser = line.AddComponent<LineRenderer>();
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.widthMultiplier = .00115f;
            _laser.numCapVertices = 6;
            _laser.startColor = new Color(1f, 1f, 1f, .22f);
            _laser.endColor = new Color(1f, 1f, 1f, .72f);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) _laser.material = new Material(shader);

            var cursor = new GameObject("XREAL Vision Cursor Ring");
            cursor.transform.SetParent(transform, false);
            cursor.transform.localScale = Vector3.one * .012f;
            var filter = cursor.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildCursorRingMesh();
            Renderer renderer = cursor.AddComponent<MeshRenderer>();
            Shader unlit = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Unlit/Color");
            if (renderer != null && unlit != null)
            {
                renderer.material = new Material(unlit);
                renderer.material.color = new Color(1f, 1f, 1f, .92f);
            }
            _cursor = cursor.transform;

            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "XREAL Vision Cursor Dot";
            dot.transform.SetParent(transform, false);
            Collider dotCollider = dot.GetComponent<Collider>();
            if (dotCollider != null) Destroy(dotCollider);
            Renderer dotRenderer = dot.GetComponent<Renderer>();
            if (dotRenderer != null && unlit != null)
            {
                dotRenderer.material = new Material(unlit);
                // White ring + graphite centre stays legible on both bright
                // web pages and dark XR panels without sampling the scene.
                dotRenderer.material.color = new Color(.12f, .13f, .16f, .98f);
            }
            _cursorDot = dot.transform;
            SetCursorVisible(false);
        }

        private static Mesh BuildCursorRingMesh()
        {
            const int segments = 32;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle),
                    0f);
                vertices[i * 2] = direction;
                vertices[i * 2 + 1] = direction * .62f;
                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = next * 2;
                triangles[t + 2] = next * 2 + 1;
                triangles[t + 3] = i * 2;
                triangles[t + 4] = next * 2 + 1;
                triangles[t + 5] = i * 2 + 1;
            }
            var mesh = new Mesh { name = "MLOmega Vision Cursor Ring" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SetCursor(
            Vector3 origin,
            Vector3 hit,
            bool visible,
            bool pressed)
        {
            SetCursorVisible(visible);
            if (!visible) return;
            _laser.SetPosition(0, origin);
            _laser.SetPosition(1, hit);
            _cursor.position = hit;
            if (_camera != null) _cursor.rotation = _camera.transform.rotation;
            float pulse = pressed
                ? .0095f
                : .012f + .00065f * Mathf.Sin(Time.unscaledTime * 4f);
            _cursor.localScale = Vector3.one * pulse;
            if (_cursorDot != null)
            {
                _cursorDot.position = hit;
                _cursorDot.localScale = Vector3.one *
                    (pressed ? .0055f : .0038f);
            }
        }

        private void SetCursorVisible(bool visible)
        {
            if (_laser != null) _laser.enabled = visible && _rayVisible;
            if (_cursor != null) _cursor.gameObject.SetActive(visible);
            if (_cursorDot != null) _cursorDot.gameObject.SetActive(visible);
        }
    }
}
