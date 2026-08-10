using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Dependency-free boundary between the common PhoneOnly/UI assemblies and
    /// the optional XREAL spatial assembly. Implementations exist only in a
    /// glasses build; PhoneOnly never references AR Foundation or XR Hands.
    /// </summary>
    public interface IXrealSpatialProvider
    {
        bool TryProjectImagePoint(Vector2 imagePoint, out Vector3 worldPoint);
        bool CaptureMeasurementPoint(Vector2 viewport);
        bool PressKeyboard(Vector2 viewport, bool pinchBegin);
        bool PersistAnchorAtViewport(Vector2 viewport);
        bool SetBallisticTarget(Vector2 viewport);
        bool StartNavigation(string destination);
        bool NameCurrentIndoorPlace(string label);
        bool ImportAnchoredWorld();
        System.Collections.Generic.IReadOnlyList<WorldMapSelection>
            AvailableWorldMaps { get; }
        bool SetWorldMapActive(string mapId, bool active);
        bool RemoveInstalledWorldMap(string mapId);
    }

    /// <summary>Creator-only surface; production code never calls these methods.</summary>
    public interface IWorldCreatorSpatialProvider
    {
        bool CreatorReady { get; }
        WorldMapStore CreatorMap { get; }
        System.Collections.Generic.IReadOnlyList<WorldMapSelection>
            CreatorMaps { get; }
        void EnableCreatorMode();
        void BeginCreatorSpatialMapping();
        bool CreateCreatorMap(string displayName);
        bool SwitchCreatorMap(string mapId);
        bool DeleteCreatorMap(string mapId);
        bool TryCreatorPlacement(
            Vector2 viewport,
            out Vector3 position,
            out Quaternion rotation);
        bool PersistCreatorContent(
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
            float motionHeightM);
        bool PrepareCreatorExport(out string error);
        bool RemoveCreatorContent(string worldContentId);
        bool SaveCreatorDynamicBinding(
            WorldCreatorCatalog.Entry preset,
            string targetLabel,
            string targetKind,
            string attachment,
            string label,
            string subtitle,
            Vector3 scale,
            string assetId);
        bool RemoveCreatorDynamicBinding(string bindingId);
        event System.Action<string, bool, string> CreatorOperationCompleted;
    }
}
