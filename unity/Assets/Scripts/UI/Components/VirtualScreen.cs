// MLOmega V19 — E25 / E59
// VirtualScreen (§13.1, §14.9): an explicit, user-requested resizable surface for
// a TV / replay / notes / work screen. Unlike the automatic components it only
// appears on a deliberate request, so it carries no "urgency" signalling. Content
// is a texture (player/PC stream, resolved by the runtime) or a notes body. The
// surface can be resized via SetSize; StatusBar keeps mic/camera controls (handled
// by StatusBar, not here). Emits displayed/seen/dismissed like the rest.
//
// E59: it is the PRIORITY hand-manipulable window — it implements IManipulablePanel
// so the PanelManipulator can grab-drag it anywhere, resize it from a corner (aspect
// ratio LOCKED — it is a video surface), and close/minimise it. It self-registers in
// the ManipulablePanelRegistry while visible and restores its remembered placement
// (per-type, session-scoped) when re-opened.
using UnityEngine;
using UnityEngine.UI;

namespace MLOmega.XR.UI.Components
{
    public sealed class VirtualScreen : UIComponentBase, IManipulablePanel, IManipulationFeedback
    {
        [SerializeField] private Vector2 _size = new Vector2(1.2f, 0.68f);
        [SerializeField] private Vector3 _placeOffset = new Vector3(0f, 0.1f, 1.8f);

        [Header("E59 — hand manipulation")]
        [SerializeField] private Vector2 _minSize = new Vector2(0.5f, 0.28f);
        [SerializeField] private Vector2 _maxSize = new Vector2(3.0f, 2.0f);

        private GlassPanel _panel;
        private RawImage _surface;
        private bool _placed;
        private bool _registered;
        private bool _minimised;
        private Vector3 _restorePosition;
        private Quaternion _restoreRotation;
        private Vector2 _restoreSize;

        public override string ComponentKey => "virtual_screen";

        protected override void OnConfigured()
        {
            _panel = new GlassPanel(transform, _size, Theme,
                Context != null ? Context.GlassMaterial : null,
                withTitle: true, withBody: false, withTruthChip: false);

            var go = new GameObject("Surface", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_panel.Root, false);
            rt.anchorMin = new Vector2(0.02f, 0.02f);
            rt.anchorMax = new Vector2(0.98f, 0.86f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _surface = go.GetComponent<RawImage>();
            _surface.raycastTarget = false;
            _surface.color = new Color(0.03f, 0.04f, 0.06f, 1f);
        }

        /// <summary>Assign the stream/replay/notes render texture.</summary>
        public void SetSurfaceTexture(Texture texture)
        {
            if (_surface == null) return;
            _surface.texture = texture;
            _surface.color = texture != null ? Color.white : new Color(0.03f, 0.04f, 0.06f, 1f);
        }

        /// <summary>Resize the surface (explicit user resize).</summary>
        public void SetSize(Vector2 size)
        {
            _size = size;
            if (_panel != null) _panel.Root.sizeDelta = size;
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            if (_panel.Title != null)
            {
                _panel.Title.text = IntentRead.Content(intent, "title",
                    IntentRead.Content(intent, "label", "Virtual Screen"));
            }
            _minimised = false;
            // A one-shot placement in front of the user; the surface then stays put.
            Place();
            _placed = true;
        }

        private void Place()
        {
            // E59: re-open where the user last left it (session-scoped, per type).
            if (PanelPlacementStore.TryGet(ComponentKey, out PanelPlacement saved))
            {
                transform.SetPositionAndRotation(saved.Position, saved.Rotation);
                SetSize(saved.Size);
                return;
            }
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null) return;
            transform.SetPositionAndRotation(
                cam.transform.TransformPoint(_placeOffset),
                Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up));
        }

        protected override void Update()
        {
            base.Update();
            if (Phase != UIComponentPhase.Idle)
            {
                if (!_placed) { Place(); _placed = true; }
                // Register once visible; unregister on fade/recycle (idle branch below).
                if (!_registered) { ManipulablePanelRegistry.Register(this); _registered = true; }
                _panel?.SetAlpha(CurrentAlpha);
                if (_surface != null && _surface.texture != null)
                {
                    Color c = _surface.color; c.a = CurrentAlpha; _surface.color = c;
                }
            }
            else
            {
                _placed = false;
                if (_registered) { ManipulablePanelRegistry.Unregister(this); _registered = false; }
            }
        }

        private void OnDisable()
        {
            if (_registered) { ManipulablePanelRegistry.Unregister(this); _registered = false; }
        }

        // ------------------------------------------------------------------
        //  E59 — IManipulablePanel (hand window management)
        // ------------------------------------------------------------------

        public string PersistenceKey => ComponentKey;
        public Transform PanelTransform => transform;
        public Vector2 PanelSize => _size;
        public bool IsManipulable => Phase != UIComponentPhase.Idle;
        public bool LockAspectRatio => true; // a video surface keeps its ratio
        public Vector2 MinSize => _minSize;
        public Vector2 MaxSize => _maxSize;
        public bool IsMinimised => _minimised;

        public void MoveTo(Vector3 worldPosition) => transform.position = worldPosition;

        public void ResizeTo(Vector2 size) => SetSize(size);

        public void CloseFromGesture() => RaiseDismissed();

        public void MinimiseFromGesture()
        {
            if (_minimised) return;
            _minimised = true;
            _restorePosition = transform.position;
            _restoreRotation = transform.rotation;
            _restoreSize = _size;
            // Collapse to a small recallable pastille docked at the periphery of view.
            SetSize(new Vector2(0.12f, 0.12f));
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam != null)
            {
                transform.SetPositionAndRotation(
                    cam.transform.TransformPoint(new Vector3(0.55f, -0.32f, 1.6f)),
                    Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up));
            }
            // Stay REGISTERED while minimised so the manipulator can pinch-tap the
            // pastille to restore it (IsMinimised gates the hit-test to restore-only).
        }

        public void RestoreFromGesture()
        {
            if (!_minimised) return;
            _minimised = false;
            transform.SetPositionAndRotation(_restorePosition, _restoreRotation);
            SetSize(_restoreSize);
        }

        public void SetManipulationFeedback(bool active, bool resizing) =>
            _panel?.SetManipulationFeedback(active, resizing);
    }
}
