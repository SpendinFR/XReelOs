// MLOmega V19 — E59
// PanelManipulator: hand window-management. The user PLACES, RESIZES and MANAGES
// what they display — above all the video window (VirtualScreen: replay/YouTube),
// and also the menu and opt-in cards. It turns the already-emitted pinch stream
// (GestureBridge; the Kotlin GesturePipeline carries a normalised x,y anchor on
// every PINCH_BEGIN/UPDATE/END — no native change needed) into direct manipulation.
//
// DISAMBIGUATION (the load-bearing rule — never steal the existing pinch→LensWindow
// zoom): at PINCH_BEGIN we ray hit-test the pinch anchor against the live set of
// IManipulablePanel surfaces (ManipulablePanelRegistry).
//   * hit a panel BODY   → GRAB: the panel sticks to the pinch point and follows the
//                          hand; release = drop at the new place. The pinch is CLAIMED
//                          so LensWindow does NOT also zoom.
//   * hit a resize HANDLE → RESIZE: drag the corner; clamp min/max; the video window
//                          keeps its aspect ratio.
//   * hit a close/minimise glass button → pinch-tap fires close (✕) / minimise (–).
//   * hit NOTHING manipulable → NOT claimed → the pinch falls through to LensWindow
//                          zoom exactly as before.
// Object-anchored atoms (task_anchor, PersonTag, ObjectOutline) never implement
// IManipulablePanel, so they are never grabbed — they keep following the world.
//
// Lives in Reflex (which already references UI), like MenuGestureController: it can
// see both GestureBridge (Reflex) and the panel interfaces (UI) without any asmdef
// cycle. Budget §9.4: no per-frame allocation; the gesture pipeline stays on-demand
// (ReflexScheduler already gates it). All logic is deterministic and driven by
// InjectGesture, so the whole flow is EditMode-testable without a device.
using MLOmega.XR.UI.Components;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    public sealed class PanelManipulator : MonoBehaviour
    {
        [SerializeField] private Camera _camera;

        [Tooltip("Fraction of the panel's smaller half-extent, measured in from each " +
                 "corner, that counts as a resize handle rather than a body grab.")]
        [Range(0.08f, 0.4f)]
        [SerializeField] private float _cornerHandleFraction = 0.22f;

        [Tooltip("Fraction of the panel span, in from the top corners, reserved for the " +
                 "✕ / – glass buttons (pinch-tap zone).")]
        [Range(0.06f, 0.3f)]
        [SerializeField] private float _buttonZoneFraction = 0.16f;

        [Tooltip("Max seconds between pinch begin and end for it to count as a 'tap' " +
                 "on a close/minimise button (a longer hold is a drag, ignored on buttons).")]
        [SerializeField] private float _tapMaxSeconds = 0.5f;

        [Tooltip("Soft alignment duration after releasing a moved/resized panel.")]
        [Min(0.01f)]
        [SerializeField] private float _snapDurationSeconds = 0.18f;

        [Tooltip("Camera-local position grid used by the soft release snap (metres).")]
        [Min(0.005f)]
        [SerializeField] private float _snapGridMeters = 0.025f;

        /// <summary>
        /// True while a pinch is actively grabbing/resizing a panel. Read by the reflex
        /// layer to SUPPRESS the LensWindow zoom for the same pinch (single owner).
        /// </summary>
        public bool HasClaim => _mode != ManipulationKind.None || _pendingButtonTap || _pendingRestore;

        /// <summary>The panel currently under manipulation (null when idle). For tests.</summary>
        public IManipulablePanel ActivePanel => _target;

        /// <summary>Current manipulation kind (None while no pinch is claimed). For tests.</summary>
        public ManipulationKind ActiveKind => _mode;

        private ManipulationKind _mode = ManipulationKind.None;
        private IManipulablePanel _target;
        private ResizeCorner _corner;

        // Grab: world-space offset from panel centre to the grab point, kept constant so
        // the panel "sticks" to the pinch point (no snap-to-centre jump).
        private Vector3 _grabOffset;
        // Resize: the fixed opposite corner (world) and the initial size, so dragging is stable.
        private Vector3 _fixedCornerWorld;
        private Vector2 _startSize;

        // Button pinch-tap bookkeeping.
        private bool _pendingButtonTap;
        private bool _pendingCloseNotMinimise;
        private bool _pendingRestore;
        private long _pinchBeginMs;

        // Release polish: a short camera-local grid/facing interpolation. It is
        // tick-driven (rather than a coroutine) so EditMode tests are deterministic.
        private IManipulablePanel _snapPanel;
        private Vector3 _snapStartPosition;
        private Quaternion _snapStartRotation;
        private Vector3 _snapTargetPosition;
        private Quaternion _snapTargetRotation;
        private float _snapElapsed;

        public bool IsSnapping => _snapPanel != null;

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
        }

        private void Update() => TickSnap(Time.unscaledDeltaTime);

        // NB: the manipulator is driven by ReflexScheduler.OnGestureForLens (same pinch
        // stream as LensWindow), which calls OnGesture BEFORE the lens so a claimed
        // grab/resize suppresses the zoom. It therefore does NOT self-subscribe to the
        // GestureBridge — that would double-process the pinch and race the lens decision.
        private void OnDisable()
        {
            Cancel();
            _snapPanel = null;
        }

        /// <summary>Handle one gesture. Public so EditMode tests drive it without a device.</summary>
        public void OnGesture(GestureEvent ev)
        {
            switch (ev.Kind)
            {
                case GestureKind.PinchBegin:
                    Begin(ev);
                    break;
                case GestureKind.PinchUpdate:
                    Drag(ev);
                    break;
                case GestureKind.PinchEnd:
                    End(ev);
                    break;
            }
        }

        // ------------------------------------------------------------------
        //  Pinch begin — hit-test + disambiguation (never steals the zoom)
        // ------------------------------------------------------------------

        private void Begin(GestureEvent ev)
        {
            // A new grab interrupts the cosmetic snap at its current interpolated pose.
            _snapPanel = null;
            Cancel();
            _pinchBeginMs = ev.TimestampMs;

            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;
            Vector2 pt = ev.ScreenPoint;
            if (pt.x < 0f || pt.y < 0f) return; // no anchor → leave the pinch to LensWindow

            // Test panels front-to-back is not tracked; the registry is tiny (density
            // cap), so a linear scan choosing the closest hit is fine and alloc-free.
            IManipulablePanel best = null;
            float bestDist = float.MaxValue;
            ManipulationKind bestKind = ManipulationKind.None;
            ResizeCorner bestCorner = ResizeCorner.BottomRight;
            Vector3 bestWorld = default;

            Ray ray = cam.ViewportPointToRay(new Vector3(pt.x, pt.y, 0f));
            foreach (IManipulablePanel panel in ManipulablePanelRegistry.Panels)
            {
                if (panel == null || !panel.IsManipulable) continue;
                Transform t = panel.PanelTransform;
                if (t == null) continue;

                if (!RayHitsPanel(ray, t, panel.PanelSize, out Vector3 worldHit, out Vector2 localHit))
                    continue;

                // Menu action rows are controls, not drag handles. Leave these
                // pinches unclaimed so MenuGestureController can select the row;
                // title, borders and corners remain fully manipulable.
                if (panel is MenuPanel menu && menu.IsActionPoint(localHit)) continue;

                float dist = Vector3.Distance(cam.transform.position, worldHit);
                if (dist >= bestDist) continue;

                best = panel;
                bestDist = dist;
                bestWorld = worldHit;

                // A minimised panel is a recall pastille: any pinch-tap on it restores it.
                if (panel.IsMinimised)
                {
                    bestKind = ManipulationKind.None;
                    _pendingButtonTap = false;
                    _pendingRestore = true;
                    continue;
                }
                _pendingRestore = false;

                ManipulationKind kind = ClassifyHit(localHit, panel.PanelSize,
                    out ResizeCorner corner, out bool buttonClose, out bool buttonMinimise);
                bestKind = kind;
                bestCorner = corner;

                // A button hit is resolved on release (pinch-tap), not on begin.
                if (buttonClose || buttonMinimise)
                {
                    bestKind = ManipulationKind.None;
                    _pendingButtonTap = true;
                    _pendingCloseNotMinimise = buttonClose;
                }
            }

            if (best == null) return; // nothing manipulable under the pinch → zoom as before

            _target = best;
            SetFeedback(_target, true, bestKind == ManipulationKind.Resize);
            if (_pendingButtonTap || _pendingRestore) return; // resolve on release

            _mode = bestKind;
            _corner = bestCorner;
            _startSize = best.PanelSize;

            Transform pt3 = best.PanelTransform;
            if (_mode == ManipulationKind.Move)
            {
                _grabOffset = pt3.position - bestWorld;
            }
            else if (_mode == ManipulationKind.Resize)
            {
                _fixedCornerWorld = OppositeCornerWorld(pt3, best.PanelSize, _corner);
            }
        }

        // ------------------------------------------------------------------
        //  Pinch update — follow the hand
        // ------------------------------------------------------------------

        private void Drag(GestureEvent ev)
        {
            if (_target == null || _mode == ManipulationKind.None) return;
            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;
            Vector2 pt = ev.ScreenPoint;
            // Only the (-1,-1) sentinel means "no anchor"; a live drag target may leave
            // the [0,1] viewport (dragging a corner outward), so don't reject those.
            if (pt.x < 0f && pt.y < 0f) return;

            Transform t = _target.PanelTransform;
            if (t == null) return;

            // Project the pinch anchor onto the panel's own plane (keeps depth stable so
            // the panel does not fly toward/away from the user while dragging in view).
            Plane plane = new Plane(-t.forward, t.position);
            Ray ray = cam.ViewportPointToRay(new Vector3(pt.x, pt.y, 0f));
            if (!plane.Raycast(ray, out float enter)) return;
            Vector3 hit = ray.GetPoint(enter);

            if (_mode == ManipulationKind.Move)
            {
                _target.MoveTo(hit + _grabOffset);
            }
            else if (_mode == ManipulationKind.Resize)
            {
                ApplyResize(t, hit);
            }
        }

        private void ApplyResize(Transform t, Vector3 dragWorld)
        {
            // Work in the panel's local frame from the FIXED opposite corner.
            Vector3 local = t.InverseTransformPoint(dragWorld) - t.InverseTransformPoint(_fixedCornerWorld);
            float w = Mathf.Abs(local.x);
            float h = Mathf.Abs(local.y);

            Vector2 min = _target.MinSize, max = _target.MaxSize;
            if (_target.LockAspectRatio && _startSize.x > 0f && _startSize.y > 0f)
            {
                float aspect = _startSize.x / _startSize.y;
                // Grow along the axis that moved most, then derive the other from aspect.
                if (w / aspect >= h) h = w / aspect; else w = h * aspect;
                // Clamp PROPORTIONALLY so the ratio survives the min/max cap (clamping
                // each axis independently would distort a video window at the bounds).
                float s = 1f;
                if (w > max.x) s = Mathf.Min(s, max.x / w);
                if (h > max.y) s = Mathf.Min(s, max.y / h);
                if (w * s < min.x) s = Mathf.Max(s, min.x / w);
                if (h * s < min.y) s = Mathf.Max(s, min.y / h);
                w *= s; h *= s;
            }
            else
            {
                w = Mathf.Clamp(w, min.x, max.x);
                h = Mathf.Clamp(h, min.y, max.y);
            }
            Vector2 size = new Vector2(w, h);
            _target.ResizeTo(size);

            // Keep the fixed corner pinned: recentre so the anchored corner stays put.
            Vector3 half = new Vector3(size.x * 0.5f, size.y * 0.5f, 0f);
            Vector3 signed = CornerSign(_corner);
            Vector3 newCenterLocalFromFixed = new Vector3(signed.x * size.x, signed.y * size.y, 0f) * 0.5f;
            t.position = _fixedCornerWorld + t.TransformVector(newCenterLocalFromFixed);
            _target.MoveTo(t.position);
        }

        // ------------------------------------------------------------------
        //  Pinch end — drop, or fire the button tap, and persist placement
        // ------------------------------------------------------------------

        private void End(GestureEvent ev)
        {
            if (_pendingRestore && _target != null)
            {
                bool quick = ev.TimestampMs - _pinchBeginMs <= (long)(_tapMaxSeconds * 1000f);
                if (quick) _target.RestoreFromGesture();
                Cancel();
                return;
            }

            if (_pendingButtonTap && _target != null)
            {
                bool quick = ev.TimestampMs - _pinchBeginMs <= (long)(_tapMaxSeconds * 1000f);
                if (quick)
                {
                    if (_pendingCloseNotMinimise) _target.CloseFromGesture();
                    else _target.MinimiseFromGesture();
                }
                Cancel();
                return;
            }

            if (_target != null && _mode != ManipulationKind.None)
            {
                Persist(_target);
                StartSoftSnap(_target);
            }
            Cancel();
        }

        private void StartSoftSnap(IManipulablePanel panel)
        {
            Transform t = panel?.PanelTransform;
            if (t == null) return;
            Camera cam = _camera != null ? _camera : Camera.main;
            _snapPanel = panel;
            _snapStartPosition = t.position;
            _snapStartRotation = t.rotation;
            _snapTargetPosition = t.position;
            _snapTargetRotation = t.rotation;
            _snapElapsed = 0f;
            if (cam == null) return;

            Vector3 local = cam.transform.InverseTransformPoint(t.position);
            float grid = Mathf.Max(0.005f, _snapGridMeters);
            local.x = Mathf.Round(local.x / grid) * grid;
            local.y = Mathf.Round(local.y / grid) * grid;
            _snapTargetPosition = cam.transform.TransformPoint(local);
            _snapTargetRotation = Quaternion.LookRotation(
                _snapTargetPosition - cam.transform.position, cam.transform.up);
        }

        /// <summary>Advance the release snap; public for deterministic EditMode tests.</summary>
        public void TickSnap(float deltaSeconds)
        {
            if (_snapPanel == null) return;
            Transform t = _snapPanel.PanelTransform;
            if (t == null || !_snapPanel.IsManipulable)
            {
                _snapPanel = null;
                return;
            }
            _snapElapsed += Mathf.Max(0f, deltaSeconds);
            float duration = Mathf.Max(0.01f, _snapDurationSeconds);
            float raw = Mathf.Clamp01(_snapElapsed / duration);
            float eased = raw * raw * (3f - 2f * raw);
            _snapPanel.MoveTo(Vector3.Lerp(_snapStartPosition, _snapTargetPosition, eased));
            t.rotation = Quaternion.Slerp(_snapStartRotation, _snapTargetRotation, eased);
            if (raw < 1f) return;
            Persist(_snapPanel);
            _snapPanel = null;
        }

        private void Cancel()
        {
            SetFeedback(_target, false, false);
            _mode = ManipulationKind.None;
            _target = null;
            _pendingButtonTap = false;
            _pendingCloseNotMinimise = false;
            _pendingRestore = false;
        }

        private static void SetFeedback(IManipulablePanel panel, bool active, bool resizing)
        {
            if (panel is IManipulationFeedback feedback)
                feedback.SetManipulationFeedback(active, resizing);
        }

        private static void Persist(IManipulablePanel panel)
        {
            Transform t = panel.PanelTransform;
            if (t == null) return;
            PanelPlacementStore.Save(panel.PersistenceKey,
                new PanelPlacement(t.position, t.rotation, panel.PanelSize));
        }

        // ------------------------------------------------------------------
        //  Geometry helpers (world-space, alloc-free)
        // ------------------------------------------------------------------

        /// <summary>
        /// Intersect a ray with the panel's quad (centred at the transform, spanning
        /// PanelSize in its local X/Y). Returns the world hit + local (metre) offset
        /// from the panel centre. The panel faces -forward (UGUI world-space convention).
        /// </summary>
        private static bool RayHitsPanel(Ray ray, Transform t, Vector2 size,
            out Vector3 worldHit, out Vector2 localHit)
        {
            worldHit = default; localHit = default;
            Plane plane = new Plane(-t.forward, t.position);
            if (!plane.Raycast(ray, out float enter)) return false;
            worldHit = ray.GetPoint(enter);
            Vector3 local = t.InverseTransformPoint(worldHit);
            if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.y) > size.y * 0.5f)
                return false;
            localHit = new Vector2(local.x, local.y);
            return true;
        }

        /// <summary>Classify where on the panel the hit landed: body / resize handle / button.</summary>
        private ManipulationKind ClassifyHit(Vector2 local, Vector2 size,
            out ResizeCorner corner, out bool buttonClose, out bool buttonMinimise)
        {
            corner = ResizeCorner.BottomRight;
            buttonClose = false;
            buttonMinimise = false;

            float halfW = size.x * 0.5f, halfH = size.y * 0.5f;
            bool right = local.x >= 0f, top = local.y >= 0f;

            // Glass buttons sit in the TOP corners (✕ top-right, – to its left).
            float btn = Mathf.Min(size.x, size.y) * _buttonZoneFraction;
            if (top && (halfH - local.y) <= btn)
            {
                if ((halfW - local.x) <= btn) { buttonClose = true; return ManipulationKind.None; }
                if ((halfW - local.x) <= btn * 2f) { buttonMinimise = true; return ManipulationKind.None; }
            }

            // Corner handles (all four) for resize.
            float handle = Mathf.Min(size.x, size.y) * _cornerHandleFraction;
            bool nearX = (halfW - Mathf.Abs(local.x)) <= handle;
            bool nearY = (halfH - Mathf.Abs(local.y)) <= handle;
            if (nearX && nearY)
            {
                corner = (top, right) switch
                {
                    (true, true) => ResizeCorner.TopRight,
                    (true, false) => ResizeCorner.TopLeft,
                    (false, true) => ResizeCorner.BottomRight,
                    _ => ResizeCorner.BottomLeft,
                };
                return ManipulationKind.Resize;
            }

            return ManipulationKind.Move;
        }

        /// <summary>Sign of the DRAGGED corner in local space (+1/-1 per axis).</summary>
        private static Vector3 CornerSign(ResizeCorner c) => c switch
        {
            ResizeCorner.TopRight => new Vector3(1f, 1f, 0f),
            ResizeCorner.TopLeft => new Vector3(-1f, 1f, 0f),
            ResizeCorner.BottomRight => new Vector3(1f, -1f, 0f),
            _ => new Vector3(-1f, -1f, 0f),
        };

        /// <summary>World position of the corner OPPOSITE the dragged one (the fixed pivot).</summary>
        private static Vector3 OppositeCornerWorld(Transform t, Vector2 size, ResizeCorner dragged)
        {
            Vector3 s = -CornerSign(dragged); // opposite corner
            Vector3 localCorner = new Vector3(s.x * size.x, s.y * size.y, 0f) * 0.5f;
            return t.TransformPoint(localCorner);
        }
    }
}
