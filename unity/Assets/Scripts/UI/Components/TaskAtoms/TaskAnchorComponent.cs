// MLOmega V19 — E53 (Viki mode aide)
// TaskAnchorComponent = the TaskOverlayRenderer for a single `task_anchor` intent.
// It is the composition brain: from ONE task_anchor content it decides which
// anchored atoms to spin up and drives them every frame against the live track:
//   * ObjectAnchorRing      — always, when the anchor targets an object; FOLLOWS the
//                             track in real time, fades to "searching" on loss
//   * TrajectoryGesture     — when content.gesture.kind != none
//   * QuantityChip          — when content.quantity is set
//   * TimerRing             — when content.timer_seconds > 0 (can run WITH a gesture)
//   * CautionCue            — when content.caution is set
//   * SelectionHighlight    — when the same label matches several live tracks
//                             (multi-candidate → "prends celui-là")
//   * TaskDirectionalArrow  — when there is NO live track for the object: point to
//                             its last-known bearing (map-quality gated) / "cherche X"
//   * ZoomInset             — optional, when a zoom texture id is provided
// It plugs into the E25 system exactly like every other component: registered in
// UIComponentRegistry, instantiated + pooled + given receipts/TTL by UIRuntime,
// anchored via the shared SceneCache track store. On a step change the PC re-sends
// the same intent id → UIRuntime.Refresh → the atoms re-target in place, no realloc.
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Scene;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class TaskAnchorComponent : UIComponentBase
    {
        [Tooltip("Distance in front of the camera the anchored overlay plane sits (m).")]
        [SerializeField] private float _planeDistance = 1.4f;

        private TaskAnchorContent _content;
        private TaskAnchorMath _anchor;
        private Color _accent = Color.white;

        // Composable atoms (lazily created; kept for the component's lifetime and
        // shown/hidden per content so a Refresh never reallocates).
        private ObjectAnchorRing _ring;
        private TrajectoryGesture _gesture;
        private QuantityChip _quantity;
        private TimerRing _timer;
        private CautionCue _caution;
        private SelectionHighlight _selection;
        private TaskDirectionalArrow _arrow;
        private ZoomInset _zoom;

        private readonly List<TaskAtom> _all = new List<TaskAtom>();

        public override string ComponentKey => "task_anchor";

        // --- test/inspection accessors (which atoms are live for this content) ---
        public bool RingActive => _ring != null && _ring.gameObject.activeSelf;
        public bool GestureActive => _gesture != null && _gesture.gameObject.activeSelf;
        public bool QuantityActive => _quantity != null && _quantity.gameObject.activeSelf;
        public bool TimerActive => _timer != null && _timer.gameObject.activeSelf;
        public bool CautionActive => _caution != null && _caution.gameObject.activeSelf;
        public bool SelectionActive => _selection != null && _selection.gameObject.activeSelf;
        public bool ArrowActive => _arrow != null && _arrow.gameObject.activeSelf;
        public bool ZoomActive => _zoom != null && _zoom.gameObject.activeSelf;
        public TimerRing TimerAtom => _timer;
        public ObjectAnchorRing RingAtom => _ring;
        public TrajectoryGesture GestureAtom => _gesture;
        public bool IsGhost => _content != null && _content.Ghost;

        protected override void OnConfigured()
        {
            _anchor = new TaskAnchorMath(
                Context != null ? Context.SceneCache : null,
                Context != null ? Context.Camera : null,
                _planeDistance);
        }

        protected override void OnTruth(TruthDescriptor truth) => _accent = truth.Accent;

        protected override void Bind(UIIntent intent)
        {
            _content = TaskAnchorContent.From(intent);

            bool hasTrack = TrackPresent(_content.TrackId);
            List<string> candidates = MatchingTracks(_content.TrackId);
            bool multiCandidate = candidates.Count > 1;

            // 1) Anchor ring on the object — unless there is no track at all, in which
            //    case we hand off to a directional arrow toward the last-known bearing.
            SetAtom(ref _ring, hasTrack || _anchor.HasEverResolved, "AnchorRing", a =>
                a.SetTarget(_anchor, _content.TrackId, _accent));

            // 2) Directional arrow only when there is genuinely no live track.
            SetAtom(ref _arrow, !hasTrack && !multiCandidate, "DirArrow", a =>
                a.SetTarget(_content.EntityId, _content.Name, _accent));

            // 3) Gesture trace, when present. Runs even alongside a timer.
            SetAtom(ref _gesture, _content.HasGesture, "Gesture", a =>
                a.SetGesture(_anchor, _content.TrackId, _content.Gesture,
                    _content.GestureFrom, _content.HasFrom,
                    _content.GestureTo, _content.HasTo, _accent,
                    _content.FromTrackId, _content.ToTrackId, _planeDistance));

            // 4) Quantity chip.
            SetAtom(ref _quantity, _content.HasQuantity, "Quantity", a =>
                a.SetQuantity(_anchor, _content.TrackId, _content.Quantity));

            // 5) Timer ring (can coexist with the gesture).
            SetAtom(ref _timer, _content.HasTimer, "Timer", a =>
                a.SetTimer(_anchor, _content.TrackId, _content.TimerSeconds, _accent));

            // 6) Caution cue.
            SetAtom(ref _caution, _content.HasCaution, "Caution", a =>
                a.SetCaution(_anchor, _content.TrackId, _content.Caution));

            // 7) Selection highlight when several candidate tracks share the label.
            SetAtom(ref _selection, multiCandidate, "Selection", a =>
                a.SetCandidates(Context != null ? Context.SceneCache : null,
                    _content.TrackId, candidates, _accent));

            // 8) Zoom inset when a texture id is supplied (texture resolved by runtime).
            bool wantZoom = !string.IsNullOrEmpty(IntentRead.Content(intent, "zoom_texture_id", null));
            SetAtom(ref _zoom, wantZoom, "Zoom", a =>
                a.SetZoom(_anchor, _content.TrackId, null,
                    IntentRead.Content(intent, "zoom_caption", "Zoom")));

            // N+1 is admitted early to pre-create/configure its atoms, but it must
            // stay invisible until the PC refreshes this stable id as current.
            if (_content.Ghost)
            {
                for (int n = 0; n < _all.Count; n++) _all[n].SetVisible(false);
            }
        }

        // Create-on-demand + show/hide + reconfigure. Never reallocates on Refresh:
        // the atom persists and is only toggled active and re-targeted.
        private void SetAtom<T>(ref T field, bool wanted, string name, System.Action<T> configure)
            where T : TaskAtom
        {
            if (wanted)
            {
                if (field == null)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(transform, false);
                    field = go.AddComponent<T>();
                    field.Init(Context, Theme);
                    _all.Add(field);
                }
                field.SetVisible(true);
                configure(field);
            }
            else if (field != null)
            {
                field.SetVisible(false);
            }
        }

        private bool TrackPresent(string trackId)
        {
            SceneCache sc = Context != null ? Context.SceneCache : null;
            return sc != null && !string.IsNullOrEmpty(trackId) && sc.Tracks.Contains(trackId);
        }

        // Candidate disambiguation: the PC may pass an explicit list of same-label
        // track ids in ui_hint.candidate_track_ids; otherwise we treat the single
        // target as the only candidate. (Local label→track matching, when the device
        // grows a label index, would extend this without touching the atoms.)
        private List<string> MatchingTracks(string chosenTrackId)
        {
            var result = new List<string>();
            if (Intent?.UiHint != null &&
                Intent.UiHint.TryGetValue("candidate_track_ids", out object raw) &&
                raw is IList<object> list)
            {
                foreach (object o in list)
                {
                    string id = o?.ToString();
                    if (!string.IsNullOrEmpty(id)) result.Add(id);
                }
            }
            if (result.Count == 0 && !string.IsNullOrEmpty(chosenTrackId))
            {
                result.Add(chosenTrackId);
            }
            return result;
        }

        protected override void ApplyVisual() { } // atoms position themselves in Tick

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle) return;
            TickAtoms(Time.unscaledTime, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Deterministic per-frame composition step. Public so EditMode tests can
        /// advance the anchored atoms without a running player loop (mirrors
        /// SceneCache.Tick / UIComponentBase.Tick). Re-reads the tracks, promotes the
        /// search arrow on a lost anchor, and drives every live atom.
        /// </summary>
        public void TickAtoms(float now, float dt)
        {
            if (Phase == UIComponentPhase.Idle) return;

            // Drive the live atoms first so the ring re-reads the track this frame and
            // its searching state is fresh for the handoff decision below.
            for (int i = 0; i < _all.Count; i++)
            {
                TaskAtom atom = _all[i];
                if (atom == null || !atom.gameObject.activeSelf) continue;
                atom.SetAlpha(CurrentAlpha);
                atom.Tick(now, dt);
            }

            // If the ring lost its track for good and no arrow yet, promote the arrow
            // so the object stays findable (search/reacquire handoff). The freshly
            // promoted arrow ticks on the next frame.
            if (_ring != null && _ring.gameObject.activeSelf && _ring.IsSearching &&
                (_arrow == null || !_arrow.gameObject.activeSelf) &&
                !SelectionActive)
            {
                SetAtom(ref _arrow, true, "DirArrow", a =>
                    a.SetTarget(_content.EntityId, _content.Name, _accent));
            }
        }
    }
}
