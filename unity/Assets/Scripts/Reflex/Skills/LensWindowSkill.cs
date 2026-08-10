// MLOmega V19 — E26
// LensWindowSkill (§9.2/§14.5 — "attraper le réel"): a pinch gesture opens a
// stabilised lens/crop around the centre of view; the continuous pinch zoom
// factor drives the crop magnification live. It seeds a pure-local anchor in the
// LocalTrackStore (TemplateTracker) so the lens stays glued to the same spot even
// with the PC cut, and emits a lens_window UIIntent (focus → priority rung 2)
// carrying the zoom factor + crop centre. Pinch-end closes the lens (and ends the
// local anchor). OCR from the PC enriches it later; the reflex works alone.
using System.Collections.Generic;
using MLOmega.XR.Scene;
using UnityEngine;

namespace MLOmega.XR.Reflex.Skills
{
    public sealed class LensWindowSkill : ReflexSkillBase
    {
        [SerializeField] private LocalTrackStore _trackStore;

        public override ReflexSkillId SkillId => ReflexSkillId.LensWindow;

        private const string LensIntentId = "ul_lens";
        private string _anchorTrackId;
        private bool _open;
        private float _zoom = 1f;
        private Vector2 _center = new Vector2(0.5f, 0.5f);

        public bool IsOpen => _open;
        public float CurrentZoom => _zoom;

        protected override void Awake()
        {
            base.Awake();
            if (_trackStore == null) _trackStore = FindAnyObjectByType<LocalTrackStore>();
        }

        /// <summary>
        /// Handle a gesture from the GestureBridge. Pinch begin opens the lens at
        /// the gesture anchor, pinch update re-emits with the new zoom, pinch end
        /// closes it. Non-pinch gestures are ignored here (menu/hide are handled
        /// elsewhere).
        /// </summary>
        public void OnGesture(GestureEvent ev, float[] greyFrame = null)
        {
            if (!IsActive) return;
            switch (ev.Kind)
            {
                case GestureKind.PinchBegin:
                    OpenLens(ev, greyFrame);
                    break;
                case GestureKind.PinchUpdate:
                    UpdateLens(ev);
                    break;
                case GestureKind.PinchEnd:
                    CloseLens();
                    break;
            }
        }

        private void OpenLens(GestureEvent ev, float[] greyFrame)
        {
            _open = true;
            _zoom = Mathf.Max(1f, ev.ZoomFactor);
            _center = ResolveCenter(ev.ScreenPoint);

            // Pure-local anchor so the crop stays stabilised without the PC.
            if (_trackStore != null && greyFrame != null)
            {
                _anchorTrackId = _trackStore.BeginLocalAnchor(greyFrame, _center, "lens");
            }
            Emit();
            RecordReflex("lens:open",
                new Dictionary<string, object> { { "zoom", _zoom } },
                0, 1.0, "info", ev.TimestampMs);
        }

        private void UpdateLens(GestureEvent ev)
        {
            if (!_open) return;
            _zoom = Mathf.Max(1f, ev.ZoomFactor);
            // Keep the crop centred on the local anchor if it is still tracked.
            if (_anchorTrackId != null && _trackStore != null &&
                _trackStore.TryGetLocalCenter(_anchorTrackId, out Vector2 c))
            {
                _center = c;
            }
            Emit();
        }

        private void CloseLens()
        {
            if (!_open) return;
            _open = false;
            if (_anchorTrackId != null) _trackStore?.EndLocalAnchor(_anchorTrackId);
            _anchorTrackId = null;
            _zoom = 1f;
        }

        private void Emit()
        {
            var intent = NewIntent("lens_window", LensIntentId);
            // Explicit focus: classifier lifts this to priority rung 2.
            intent.UiHint["focus"] = true;
            intent.Content["zoom"] = _zoom;
            intent.Content["title"] = "Lens";
            intent.Anchor["center"] = new List<object> { _center.x, _center.y };
            if (_anchorTrackId != null) intent.TargetTrackId = _anchorTrackId;
            EmitIntent(intent);
        }

        private Vector2 ResolveCenter(Vector2 screenPoint)
        {
            if (screenPoint.x >= 0f && screenPoint.y >= 0f) return screenPoint;
            return new Vector2(0.5f, 0.5f); // centre of view
        }

        protected override void OnDeactivated() => CloseLens();
    }
}
