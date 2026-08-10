// MLOmega V19 — E59
// Two tiny static services shared by every manipulable panel and the (Reflex)
// PanelManipulator, kept in the UI assembly so panels self-register without any
// dependency on Reflex (Reflex → UI is the one-way asmdef edge; a registry the
// panels push into avoids the manipulator reaching into UIRuntime's private pool):
//
//   * ManipulablePanelRegistry — the live set of on-screen manipulable panels. A
//     panel registers on becoming visible and unregisters on fade/recycle; the
//     manipulator hit-tests against this set at pinch-begin. No per-frame alloc
//     (the manipulator enumerates an existing list).
//   * PanelPlacementStore — remembered position/size PER PANEL TYPE for the current
//     session (§E59 "persistance de placement, session courante minimum"). It is a
//     plain in-memory map, reset on session reset, deliberately NOT the SceneCache
//     ui_state sub-cache (that one mandates a TTL and holds intent-visibility, not
//     transforms — a remembered placement must not expire under the user).
using System.Collections.Generic;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>Live registry of on-screen manipulable panels (self-registered).</summary>
    public static class ManipulablePanelRegistry
    {
        private static readonly List<IManipulablePanel> _panels = new List<IManipulablePanel>();

        /// <summary>The current manipulable panels. Read-only; do not mutate while enumerating.</summary>
        public static IReadOnlyList<IManipulablePanel> Panels => _panels;

        public static void Register(IManipulablePanel panel)
        {
            if (panel == null || _panels.Contains(panel)) return;
            _panels.Add(panel);
        }

        public static void Unregister(IManipulablePanel panel)
        {
            if (panel == null) return;
            _panels.Remove(panel);
        }

        /// <summary>Test/reset helper — clears the live set (never called in the hot path).</summary>
        public static void Clear() => _panels.Clear();
    }

    /// <summary>Remembered placement for one panel type.</summary>
    public readonly struct PanelPlacement
    {
        public readonly Vector3 Position;   // world-space centre
        public readonly Quaternion Rotation;
        public readonly Vector2 Size;       // metres

        public PanelPlacement(Vector3 position, Quaternion rotation, Vector2 size)
        {
            Position = position;
            Rotation = rotation;
            Size = size;
        }
    }

    /// <summary>Session-scoped placement memory, keyed by panel TYPE (PersistenceKey).</summary>
    public static class PanelPlacementStore
    {
        private static readonly Dictionary<string, PanelPlacement> _placements =
            new Dictionary<string, PanelPlacement>();

        /// <summary>Remember where/what size a panel type was last left.</summary>
        public static void Save(string key, PanelPlacement placement)
        {
            if (string.IsNullOrEmpty(key)) return;
            _placements[key] = placement;
        }

        /// <summary>Recall a remembered placement for a panel type, if any.</summary>
        public static bool TryGet(string key, out PanelPlacement placement)
        {
            if (!string.IsNullOrEmpty(key)) return _placements.TryGetValue(key, out placement);
            placement = default;
            return false;
        }

        /// <summary>Session reset (§9.1) — forget all remembered placements.</summary>
        public static void Reset() => _placements.Clear();
    }
}
