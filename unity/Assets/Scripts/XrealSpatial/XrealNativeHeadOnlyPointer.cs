using MLOmega.XR.Reflex;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Lab-only head fallback for moments where the Eye cannot resolve a hand.
    /// It is explicitly opt-in. Once enabled, gaze interaction stays available
    /// continuously; there is no hidden wake gesture that could strand a user.
    /// </summary>
    public sealed partial class XrealNativeHandPointer
    {
        private enum HeadWakePhase
        {
            WaitingStill,
            WaitingLift,
            HoldingLift,
        }

        private enum HeadDwellPhase
        {
            Idle,
            Hovering,
            PressDecision,
            Dragging,
            ReleaseHold,
            Cooldown,
        }

        private const string HeadOnlyPreference =
            "mlomega.atelier.head_only.v1";
        private const float HeadWakeStillSeconds = .52f;
        private const float HeadWakeLiftY = .045f;
        private const float HeadWakeHoldSeconds = .38f;
        private const float HeadDwellClickSeconds = 1.05f;
        private const float HeadPressDecisionSeconds = .68f;
        private const float HeadDragReleaseSeconds = .95f;

        private bool _headOnlyEnabled;
        private bool _headOnlyInteractionActive;
        private bool _gestureStandbyBeforeHeadOnly;
        private HeadWakePhase _headWakePhase;
        private float _headWakePhaseSince = -1f;
        private float _headWakeBaselineY;
        private Quaternion _headLastRotation;
        private bool _headMotionInitialized;
        private float _headAngularSpeed;
        private float _headOnlyLastInteractionAt = -1f;
        private float _headPassiveMenuGazeSince = -1f;

        private HeadDwellPhase _headDwellPhase;
        private GameObject _headDwellTarget;
        private Quaternion _headDwellAnchorRotation;
        private float _headDwellPhaseSince = -1f;
        private Vector3 _headDwellWorldPoint;
        private Vector2 _headDwellScreenPoint;
        private bool _headDwellManipulationClaimed;
        private LineRenderer _headClickProgress;
        private LineRenderer _headActionProgress;

        private void InitializeHeadOnlyMode()
        {
            _headOnlyEnabled =
                PlayerPrefs.GetInt(HeadOnlyPreference, 0) == 1;
            _headOnlyInteractionActive = _headOnlyEnabled;
            _headWakePhase = HeadWakePhase.WaitingStill;
            _headWakePhaseSince = Time.unscaledTime;
            if (_headOnlyEnabled && _eyeGestures != null)
            {
                _gestureStandbyBeforeHeadOnly =
                    _eyeGestures.IsInteractionStandby;
                _eyeGestures.SetInteractionStandby(true);
            }
            _creator?.SetHeadOnlyModeVisualState(
                _headOnlyEnabled,
                _headOnlyInteractionActive,
                false);
        }

        public void ToggleHeadOnlyMode()
        {
            SetHeadOnlyMode(!_headOnlyEnabled, true);
        }

        public void EnterHeadOnlyPassiveMode()
        {
            if (!_headOnlyEnabled)
            {
                _gestureStandbyBeforeHeadOnly =
                    _eyeGestures != null && _eyeGestures.IsInteractionStandby;
                _headOnlyEnabled = true;
                PlayerPrefs.SetInt(HeadOnlyPreference, 1);
                PlayerPrefs.Save();
                if (_eyeGestures != null)
                    _eyeGestures.SetInteractionStandby(true);
            }
            EnterHeadOnlyPassive(true);
        }

        public void EnterHeadOnlyInteractionMode()
        {
            if (!_headOnlyEnabled)
            {
                SetHeadOnlyMode(true, true);
                return;
            }
            ActivateHeadOnlyInteraction(true);
        }

        private void SetHeadOnlyMode(bool enabled, bool notify)
        {
            if (_headOnlyEnabled == enabled)
            {
                if (enabled) ActivateHeadOnlyInteraction(notify);
                return;
            }
            if (enabled)
            {
                _gestureStandbyBeforeHeadOnly =
                    _eyeGestures != null && _eyeGestures.IsInteractionStandby;
            }
            _headOnlyEnabled = enabled;
            PlayerPrefs.SetInt(HeadOnlyPreference, enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (_eyeGestures != null)
                _eyeGestures.SetInteractionStandby(
                    enabled || _gestureStandbyBeforeHeadOnly);
            if (enabled)
                ActivateHeadOnlyInteraction(notify);
            else
            {
                _headOnlyInteractionActive = false;
                ResetHeadOnlyDwell(true);
                _creator?.SetHeadOnlyModeVisualState(false, false, notify);
            }
        }

        private void EnterHeadOnlyPassive(bool notify)
        {
            _headOnlyInteractionActive = false;
            _headWakePhase = HeadWakePhase.WaitingStill;
            _headWakePhaseSince = Time.unscaledTime;
            ResetHeadOnlyDwell(true);
            ReleasePointer(false);
            SetCursorVisible(false);
            _creator?.SetHeadOnlyModeVisualState(true, false, notify);
        }

        private void ActivateHeadOnlyInteraction(bool notify = true)
        {
            if (!_headOnlyEnabled) return;
            _headOnlyInteractionActive = true;
            _headOnlyLastInteractionAt = Time.unscaledTime;
            _headDwellPhase = HeadDwellPhase.Idle;
            _headDwellTarget = null;
            _creator?.SetHeadOnlyModeVisualState(true, true, notify);
        }

        private void UpdateHeadMotionMetrics()
        {
            if (_camera == null) return;
            Quaternion rotation = _camera.transform.rotation;
            if (!_headMotionInitialized)
            {
                _headLastRotation = rotation;
                _headMotionInitialized = true;
                _headAngularSpeed = 0f;
                return;
            }
            float dt = Mathf.Max(.001f, Time.unscaledDeltaTime);
            _headAngularSpeed = Quaternion.Angle(_headLastRotation, rotation) / dt;
            _headLastRotation = rotation;
        }

        private void UpdateHeadOnlyPassiveActivation()
        {
            if (!_headOnlyEnabled || _headOnlyInteractionActive || _camera == null)
                return;
            Ray gaze = _camera.ViewportPointToRay(
                new Vector3(.5f, .5f, 0f));
            bool onPassiveMenu =
                _creator != null &&
                _creator.IsHeadOnlyPassiveMenuGazeTarget(gaze);
            if (!onPassiveMenu)
            {
                _headPassiveMenuGazeSince = -1f;
                return;
            }
            float now = Time.unscaledTime;
            if (_headPassiveMenuGazeSince < 0f)
                _headPassiveMenuGazeSince = now;
            if (now - _headPassiveMenuGazeSince >= .28f)
            {
                _headPassiveMenuGazeSince = -1f;
                ActivateHeadOnlyInteraction(false);
            }
        }

        private void UpdateHeadOnlyDwell(
            Vector3 worldPoint,
            Vector2 screenPoint,
            bool deckHit)
        {
            if (!_headOnlyEnabled || !_headOnlyInteractionActive)
            {
                ResetHeadOnlyDwell(true);
                return;
            }
            EnsureHeadOnlyProgressVisuals();
            float now = Time.unscaledTime;
            _headDwellWorldPoint = worldPoint;
            _headDwellScreenPoint = screenPoint;

            if (_headDwellPhase == HeadDwellPhase.Dragging ||
                _headDwellPhase == HeadDwellPhase.ReleaseHold)
            {
                UpdateHeadOnlyDrag(now, worldPoint, deckHit);
                return;
            }

            if (_headDwellPhase == HeadDwellPhase.PressDecision)
            {
                SetHeadOnlyCursorFeedback(
                    new Color(1f, .78f, .36f, .98f),
                    new Color(.34f, .20f, .06f, 1f));
                float decisionProgress = Mathf.Clamp01(
                    (now - _headDwellPhaseSince) / HeadPressDecisionSeconds);
                ShowHeadOnlyProgress(
                    _headActionProgress,
                    worldPoint,
                    decisionProgress,
                    .009f,
                    new Color(1f, .72f, .30f, .98f));
                if (_headAngularSpeed >= 3.2f)
                {
                    _headDwellPhase = HeadDwellPhase.Dragging;
                    _headDwellPhaseSince = now;
                    HideHeadOnlyProgress(_headActionProgress);
                    ContinuePointerDrag();
                }
                else if (decisionProgress >= 1f)
                {
                    ReleasePointer(true);
                    _headOnlyLastInteractionAt = now;
                    _headDwellPhase = HeadDwellPhase.Cooldown;
                    _headDwellPhaseSince = now;
                    HideHeadOnlyProgress(_headActionProgress);
                }
                return;
            }

            bool manipulationHandle =
                deckHit &&
                _creator != null &&
                _creator.IsDeckManipulationHandle(worldPoint);
            GameObject dwellTarget = _hover != null
                ? _hover
                : (manipulationHandle ? _creator.gameObject : null);

            if (_headDwellPhase == HeadDwellPhase.Cooldown)
            {
                SetHeadOnlyCursorFeedback(
                    new Color(.82f, .86f, .92f, .92f),
                    new Color(.12f, .13f, .16f, .98f));
                HideHeadOnlyProgress(_headClickProgress);
                HideHeadOnlyProgress(_headActionProgress);
                // Looking away is the explicit re-arm. This prevents repeatedly
                // clicking the same video/control while simply watching it.
                if (dwellTarget != _headDwellTarget)
                {
                    _headDwellPhase = HeadDwellPhase.Idle;
                    _headDwellTarget = null;
                }
                return;
            }

            if (dwellTarget == null || !deckHit)
            {
                _headDwellPhase = HeadDwellPhase.Idle;
                _headDwellTarget = null;
                HideHeadOnlyProgress(_headClickProgress);
                ResetHeadOnlyCursorFeedback();
                return;
            }

            if (dwellTarget != _headDwellTarget)
            {
                _headDwellTarget = dwellTarget;
                _headDwellAnchorRotation = _camera.transform.rotation;
                _headDwellPhaseSince = now;
                _headDwellPhase = HeadDwellPhase.Hovering;
            }
            else if (
                Quaternion.Angle(
                    _headDwellAnchorRotation,
                    _camera.transform.rotation) > 1.05f)
            {
                _headDwellAnchorRotation = _camera.transform.rotation;
                _headDwellPhaseSince = now;
            }

            float progress = Mathf.Clamp01(
                (now - _headDwellPhaseSince) / HeadDwellClickSeconds);
            SetHeadOnlyCursorFeedback(
                new Color(.30f, .94f, 1f, .98f),
                progress >= .68f
                    ? new Color(.16f, .62f, .72f, 1f)
                    : new Color(.12f, .13f, .16f, .98f));
            ShowHeadOnlyProgress(
                _headClickProgress,
                worldPoint,
                progress,
                .0105f,
                new Color(.30f, .94f, 1f, .98f));
            if (progress < 1f) return;

            HideHeadOnlyProgress(_headClickProgress);
            Vector2 headAnchor = HeadOnlyViewportAnchor(worldPoint);
            _headDwellManipulationClaimed =
                _creator != null &&
                _creator.TryBeginDeckManipulation(worldPoint, headAnchor, 1f);
            if (_headDwellManipulationClaimed)
            {
                _headDwellPhase = HeadDwellPhase.Dragging;
                _headDwellPhaseSince = now;
                _headOnlyLastInteractionAt = now;
                return;
            }
            PressPointer();
            if (_pressed == null)
            {
                _headDwellPhase = HeadDwellPhase.Cooldown;
                return;
            }
            _headDwellPhase = HeadDwellPhase.PressDecision;
            _headDwellPhaseSince = now;
        }

        private void UpdateHeadOnlyDrag(
            float now,
            Vector3 worldPoint,
            bool deckHit)
        {
            if (!_headOnlyEnabled || !_headOnlyInteractionActive)
            {
                ResetHeadOnlyDwell(true);
                return;
            }
            if (_headDwellManipulationClaimed)
            {
                _creator?.UpdateDeckManipulation(
                    HeadOnlyViewportAnchor(worldPoint),
                    1f);
            }
            else
            {
                ContinuePointerDrag();
            }
            SetHeadOnlyCursorFeedback(
                new Color(1f, .48f, .32f, .98f),
                new Color(.55f, .15f, .08f, 1f));

            if (_headAngularSpeed <= 1.35f)
            {
                if (_headDwellPhase != HeadDwellPhase.ReleaseHold)
                {
                    _headDwellPhase = HeadDwellPhase.ReleaseHold;
                    _headDwellPhaseSince = now;
                }
                float progress = Mathf.Clamp01(
                    (now - _headDwellPhaseSince) / HeadDragReleaseSeconds);
                ShowHeadOnlyProgress(
                    _headActionProgress,
                    worldPoint,
                    progress,
                    .012f,
                    new Color(1f, .48f, .32f, .98f));
                if (progress >= 1f)
                {
                    CompleteHeadOnlyDrag(now);
                }
            }
            else
            {
                _headDwellPhase = HeadDwellPhase.Dragging;
                _headDwellPhaseSince = now;
                HideHeadOnlyProgress(_headActionProgress);
            }
        }

        private void CompleteHeadOnlyDrag(float now)
        {
            if (_headDwellManipulationClaimed)
                _creator?.EndDeckManipulation();
            else
                ReleasePointer(false);
            _headDwellManipulationClaimed = false;
            _headOnlyLastInteractionAt = now;
            _headDwellPhase = HeadDwellPhase.Cooldown;
            HideHeadOnlyProgress(_headActionProgress);
        }

        private Vector2 HeadOnlyViewportAnchor(Vector3 worldPoint)
        {
            if (_camera == null) return new Vector2(.5f, .5f);
            Vector3 viewport = _camera.WorldToViewportPoint(worldPoint);
            return new Vector2(
                Mathf.Clamp01(viewport.x),
                Mathf.Clamp01(viewport.y));
        }

        private void ResetHeadOnlyDwell(bool release)
        {
            if (_headDwellManipulationClaimed)
                _creator?.EndDeckManipulation();
            else if (release)
                ReleasePointer(false);
            _headDwellManipulationClaimed = false;
            _headDwellTarget = null;
            _headDwellPhase = HeadDwellPhase.Idle;
            _headDwellPhaseSince = -1f;
            HideHeadOnlyProgress(_headClickProgress);
            HideHeadOnlyProgress(_headActionProgress);
            ResetHeadOnlyCursorFeedback();
        }

        private void SetHeadOnlyCursorFeedback(Color ring, Color dot)
        {
            Renderer ringRenderer = _cursor != null
                ? _cursor.GetComponent<Renderer>()
                : null;
            if (ringRenderer?.material != null)
                ringRenderer.material.color = ring;
            Renderer dotRenderer = _cursorDot != null
                ? _cursorDot.GetComponent<Renderer>()
                : null;
            if (dotRenderer?.material != null)
                dotRenderer.material.color = dot;
        }

        private void ResetHeadOnlyCursorFeedback() =>
            SetHeadOnlyCursorFeedback(
                new Color(1f, 1f, 1f, .92f),
                new Color(.12f, .13f, .16f, .98f));

        private void EnsureHeadOnlyProgressVisuals()
        {
            if (_headClickProgress == null)
                _headClickProgress = BuildHeadOnlyProgressLine(
                    "XREAL Head Dwell Click");
            if (_headActionProgress == null)
                _headActionProgress = BuildHeadOnlyProgressLine(
                    "XREAL Head Dwell Action");
        }

        private LineRenderer BuildHeadOnlyProgressLine(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = .00125f;
            line.numCapVertices = 6;
            line.enabled = false;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) line.material = new Material(shader);
            return line;
        }

        private void ShowHeadOnlyProgress(
            LineRenderer line,
            Vector3 hit,
            float progress,
            float radius,
            Color color)
        {
            if (line == null || _camera == null) return;
            const int segments = 32;
            int count = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Clamp01(progress) * segments) + 1,
                2,
                segments + 1);
            line.positionCount = count;
            line.startColor = color;
            line.endColor = color;
            Vector3 centre =
                hit + _camera.transform.right * (radius * 2.1f) -
                _camera.transform.forward * .002f;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * .5f -
                    Mathf.PI * 2f * i / segments;
                line.SetPosition(
                    i,
                    centre +
                    _camera.transform.right * (Mathf.Cos(angle) * radius) +
                    _camera.transform.up * (Mathf.Sin(angle) * radius));
            }
            line.enabled = true;
        }

        private static void HideHeadOnlyProgress(LineRenderer line)
        {
            if (line != null) line.enabled = false;
        }

        private void DestroyHeadOnlyVisuals()
        {
            if (_headClickProgress != null && _headClickProgress.material != null)
                Destroy(_headClickProgress.material);
            if (_headActionProgress != null && _headActionProgress.material != null)
                Destroy(_headActionProgress.material);
        }
    }
}
