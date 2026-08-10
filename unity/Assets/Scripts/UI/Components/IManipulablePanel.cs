// MLOmega V19 — E59
// Opt-in contract for a UI surface the user can PLACE, RESIZE, CLOSE and MINIMISE
// by hand (window management gestuel). Only panels that implement this are grabbed
// by the PanelManipulator; everything else (object-anchored atoms like task_anchor,
// PersonTag, ObjectOutline — they FOLLOW the world) is left untouched, so the
// existing pinch→LensWindow zoom is never stolen unless the pinch begins ON a
// manipulable panel.
//
// The interface is deliberately world-space and framework-free (metres, not px):
// the manipulator does a ray hit-test at pinch-begin and then drives Move/Resize
// through these methods. Persistence of the chosen placement is keyed by
// PersistenceKey (per panel TYPE) so a re-opened virtual screen comes back where
// the user last left it (session-scoped — see PanelPlacementStore).
//
// Lives in the UI assembly (panels implement it here); Reflex references UI, so the
// PanelManipulator (Reflex) consumes it without introducing an asmdef cycle.
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>What the manipulator wants to do at the point it hit-tested.</summary>
    public enum ManipulationKind
    {
        None = 0,
        /// <summary>Grab-drag the whole panel (pinch began on its body).</summary>
        Move = 1,
        /// <summary>Resize from a corner/edge handle (pinch began on a handle).</summary>
        Resize = 2,
    }

    /// <summary>Which corner a resize drag is pulling (for anchoring the opposite corner).</summary>
    public enum ResizeCorner
    {
        BottomRight = 0,
        BottomLeft = 1,
        TopRight = 2,
        TopLeft = 3,
    }

    /// <summary>
    /// A UI surface the user can move/resize/close/minimise by hand. Implemented by
    /// VirtualScreen (priority), MenuPanel and any opt-in card; NOT by object-anchored
    /// atoms. All geometry is world-space so the manipulator is renderer-agnostic.
    /// </summary>
    public interface IManipulablePanel
    {
        /// <summary>Stable key for placement persistence, per panel TYPE (e.g. "virtual_screen").</summary>
        string PersistenceKey { get; }

        /// <summary>The panel's world-space transform (root the manipulator moves).</summary>
        Transform PanelTransform { get; }

        /// <summary>Current world-space size in metres (width, height).</summary>
        Vector2 PanelSize { get; }

        /// <summary>True only while the surface is actually on screen (grab is a no-op otherwise).</summary>
        bool IsManipulable { get; }

        /// <summary>
        /// Whether resizing keeps the width/height ratio (the video window does; a text
        /// card need not). The manipulator enforces this while dragging a corner.
        /// </summary>
        bool LockAspectRatio { get; }

        /// <summary>Min/max size clamps (metres) the manipulator honours on resize.</summary>
        Vector2 MinSize { get; }
        Vector2 MaxSize { get; }

        /// <summary>Move the panel so its centre sits at <paramref name="worldPosition"/>.</summary>
        void MoveTo(Vector3 worldPosition);

        /// <summary>Resize to <paramref name="size"/> (already clamped/aspect-corrected by the caller).</summary>
        void ResizeTo(Vector2 size);

        /// <summary>User closed the panel (✕): dismiss it. Same effect as a dismissed receipt.</summary>
        void CloseFromGesture();

        /// <summary>User minimised the panel (–): collapse to a recallable dock pastille.</summary>
        void MinimiseFromGesture();

        /// <summary>Restore a minimised panel to its last placement/size (pinch-tap the pastille).</summary>
        void RestoreFromGesture();

        /// <summary>True while minimised (the manipulator then only offers restore).</summary>
        bool IsMinimised { get; }
    }

    /// <summary>Optional visual hook used by PanelManipulator during a claimed pinch.</summary>
    public interface IManipulationFeedback
    {
        void SetManipulationFeedback(bool active, bool resizing);
    }
}
