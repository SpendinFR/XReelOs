// MLOmega V19 — E53 (Viki mode aide)
// Base class for the composable glass atoms of the task UI bank. An atom is a
// self-contained, data-driven visual (no hard-coded content) that the two E53
// renderers (TaskPanelComponent / TaskAnchorComponent) instantiate, drive and
// tear down. It is deliberately NOT a UIComponentBase: receipts, TTL and broker
// lifecycle belong to the single admitted intent that owns the assembly (the E25
// rule "un seul propriétaire de reçu par intent"). An atom only knows how to
// (a) receive the shared glass context/theme, (b) push an animated alpha, and
// (c) tick its own idle animation deterministically so EditMode tests can advance
// it without a running player loop — mirroring UIComponentBase.Tick.
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public abstract class TaskAtom : MonoBehaviour
    {
        protected UIComponentContext Context { get; private set; }
        protected UITheme Theme { get; private set; }

        /// <summary>Current animated alpha (0..1), pushed by the owning renderer.</summary>
        protected float Alpha { get; private set; } = 1f;

        /// <summary>Camera the atom billboards / projects against.</summary>
        protected Camera Cam => Context != null && Context.Camera != null ? Context.Camera : Camera.main;

        /// <summary>Called once by the renderer right after AddComponent, before the first data push.</summary>
        public void Init(UIComponentContext context, UITheme theme)
        {
            Context = context;
            Theme = theme;
            Build();
        }

        /// <summary>Build persistent visuals once (line renderers, panels, labels).</summary>
        protected virtual void Build() { }

        /// <summary>Push the owner's animated alpha into this atom's materials/labels.</summary>
        public virtual void SetAlpha(float alpha)
        {
            Alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>
        /// Deterministic per-frame step. <paramref name="now"/> is unscaled seconds,
        /// <paramref name="dt"/> the delta. The renderer calls this (not Unity's
        /// Update) so tests drive time exactly like SceneCache.Tick / the base UI
        /// component. Default: no-op.
        /// </summary>
        public virtual void Tick(float now, float dt) { }

        /// <summary>Show/hide without destroying (search/reacquire, ghost→current).</summary>
        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        /// <summary>Convenience: a colour with the atom's current alpha applied.</summary>
        protected Color WithAlpha(Color c, float extra = 1f)
        {
            c.a *= Alpha * extra;
            return c;
        }

        /// <summary>Billboard a transform to face the camera (shared by label atoms).</summary>
        protected void Billboard(Transform t)
        {
            Camera cam = Cam;
            if (cam == null || t == null) return;
            t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, Vector3.up);
        }
    }
}
