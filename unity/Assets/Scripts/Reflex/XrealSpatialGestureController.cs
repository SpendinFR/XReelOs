using MLOmega.XR.UI;
using MLOmega.XR.UI.Components;
using MLOmega.XR.Transport;
using MLOmega.Contracts.V19;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    /// <summary>
    /// Explicit pinch interaction for the XREAL spatial ruler and keyboard.
    /// It reuses the real Eye/MediaPipe gesture stream but only while one of
    /// those opt-in tools is active. It never changes global menu gestures.
    /// </summary>
    public sealed class XrealSpatialGestureController : MonoBehaviour
    {
        [SerializeField] private GestureBridge _gestures;
        [SerializeField] private MonoBehaviour _spatial;
        [SerializeField] private AugmentedRealityFeatureRegistry _features;
        [SerializeField] private LiveTransportBridge _transport;

        private IXrealSpatialProvider SpatialProvider =>
            _spatial as IXrealSpatialProvider;

        private void Awake()
        {
            if (_gestures == null) _gestures = FindAnyObjectByType<GestureBridge>();
            if (_spatial == null)
            {
                foreach (MonoBehaviour behaviour in
                    FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (behaviour is IXrealSpatialProvider)
                    {
                        _spatial = behaviour;
                        break;
                    }
                }
            }
            if (_features == null)
                _features = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
            if (_transport == null)
                _transport = FindAnyObjectByType<LiveTransportBridge>();
        }

        private void OnEnable()
        {
            if (_gestures != null)
                _gestures.GestureRecognized += OnGesture;
        }

        private void OnDisable()
        {
            if (_gestures != null)
                _gestures.GestureRecognized -= OnGesture;
        }

        private void Update()
        {
            if (_gestures == null || _features == null) return;
            if (
                _features.IsActive(AugmentedRealityFeatureRegistry.ArMeasurement) ||
                _features.IsActive(AugmentedRealityFeatureRegistry.SpatialKeyboard))
            {
                // Idempotent. The ReflexScheduler can still own its other gates;
                // this keeps the shared recognizer alive for an explicitly armed
                // spatial tool without creating a second camera graph.
                _gestures.Activate();
            }
        }

        private void OnGesture(GestureEvent gesture)
        {
            if (
                SpatialProvider == null ||
                gesture.ScreenPoint.x < 0f ||
                gesture.ScreenPoint.y < 0f)
                return;
            if (gesture.Kind == GestureKind.PinchBegin)
            {
                if (
                    _features.IsActive(
                        AugmentedRealityFeatureRegistry.SpatialKeyboard) &&
                    SpatialProvider.PressKeyboard(gesture.ScreenPoint, true))
                    return;
                if (
                    _features.IsActive(
                        AugmentedRealityFeatureRegistry.BallisticPreview) &&
                    SpatialProvider.SetBallisticTarget(gesture.ScreenPoint))
                    return;
                if (
                    _features.IsActive(
                        AugmentedRealityFeatureRegistry.ArMeasurement) &&
                    SpatialProvider.CaptureMeasurementPoint(gesture.ScreenPoint))
                    return;
                if (
                    _features.IsActive(
                        AugmentedRealityFeatureRegistry.PersistentAnchors) &&
                    SpatialProvider.PersistAnchorAtViewport(gesture.ScreenPoint))
                    return;
                if (
                    _transport != null &&
                    _features.IsActive(
                        AugmentedRealityFeatureRegistry.ObjectMenus) &&
                    WorldSemanticMarker.TryResolveAtViewport(
                        Camera.main,
                        gesture.ScreenPoint,
                        out WorldSemanticMarker marker))
                {
                    _transport.SendContractMessage(ContractJson.Serialize(new
                    {
                        type = "device_intent",
                        action = "inspect_object",
                        track_id = marker.MarkerId,
                        label = marker.Label,
                    }));
                }
            }
        }
    }
}
