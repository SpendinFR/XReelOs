// MLOmega V19 — E53 (Viki mode aide)
// Shared, allocation-free anchoring math for the task-overlay atoms. Every atom
// that lives ON an object (ring, gesture, quantity chip, timer, highlight, zoom)
// projects a normalised screen bbox — read live from the device track store
// (SceneCache.Tracks, refreshed by the on-device StableTrack path) — onto a plane
// a fixed distance in front of the camera, exactly like E25's ObjectOutline. It is
// centralised so no atom re-invents the projection and so the "l'ancre SUIT
// l'objet" behaviour is identical everywhere: the bbox is re-read every frame from
// the track, so when the user moves the bowl the whole overlay moves with it.
//
// It also owns the track lifecycle the design demands: a track that stops being
// reconciled ages out of SceneCache.Tracks (§9.1 "très court"); this helper reports
// that as a lost anchor so an atom can switch to a discreet "searching" state and
// re-acquire automatically the moment the track (or a rematched one) reappears.
using System.Collections.Generic;
using MLOmega.XR.Scene;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    /// <summary>
    /// Resolves the live world-space quad for an anchored task atom from a track id.
    /// Pure data + one SceneCache read per Resolve; no per-frame allocation.
    /// </summary>
    public sealed class TaskAnchorMath
    {
        private readonly SceneCache _sceneCache;
        private readonly Camera _camera;
        private readonly float _planeDistance;

        // Last known bbox, kept so a momentarily-lost track keeps the overlay where
        // it was (rather than snapping to a fallback) until re-acquisition or a
        // deliberate "searching" fade by the atom.
        private Rect _lastBbox = new Rect(0.42f, 0.42f, 0.16f, 0.16f);
        private bool _hasEverResolved;

        public TaskAnchorMath(SceneCache sceneCache, Camera camera, float planeDistance = 1.4f)
        {
            _sceneCache = sceneCache;
            _camera = camera;
            _planeDistance = planeDistance;
        }

        /// <summary>True on the frame the track was present in the store.</summary>
        public bool TrackPresent { get; private set; }

        /// <summary>The last resolved normalised bbox (top-down image coords, 0..1).</summary>
        public Rect LastBbox => _lastBbox;

        /// <summary>Centre of the last resolved quad in world space.</summary>
        public Vector3 Center { get; private set; }

        /// <summary>
        /// Re-read the track's bbox and recompute the four world corners. Returns
        /// false (and leaves the corners on the last known position) when the track
        /// is absent — the atom decides whether to hold, fade to "searching", or
        /// hand off to a DirectionalArrow. Corners are written into
        /// <paramref name="corners"/> (length 4: BL, BR, TR, TL) so the caller can
        /// feed a LineRenderer without allocating.
        /// </summary>
        public bool Resolve(string trackId, Vector3[] corners)
        {
            Camera cam = _camera != null ? _camera : Camera.main;
            TrackPresent = false;

            if (!string.IsNullOrEmpty(trackId) && _sceneCache != null &&
                _sceneCache.Tracks.TryGet(trackId, out SceneCache.TrackEntry entry))
            {
                _lastBbox = BboxToRect(entry.Track.BboxOrMask, _lastBbox);
                TrackPresent = true;
                _hasEverResolved = true;
            }

            if (cam == null || corners == null || corners.Length < 4)
            {
                return TrackPresent;
            }

            Rect b = _lastBbox;
            corners[0] = ViewportToPlane(cam, new Vector2(b.xMin, b.yMax)); // bottom-left
            corners[1] = ViewportToPlane(cam, new Vector2(b.xMax, b.yMax)); // bottom-right
            corners[2] = ViewportToPlane(cam, new Vector2(b.xMax, b.yMin)); // top-right
            corners[3] = ViewportToPlane(cam, new Vector2(b.xMin, b.yMin)); // top-left
            Center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            return TrackPresent;
        }

        /// <summary>Has the anchor ever locked onto its track at least once?</summary>
        public bool HasEverResolved => _hasEverResolved;

        /// <summary>World point for a normalised point inside the anchored bbox (0..1 local).</summary>
        public Vector3 LocalToWorld(Vector2 local01)
        {
            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return Center;
            Rect b = _lastBbox;
            var vp = new Vector2(
                Mathf.Lerp(b.xMin, b.xMax, local01.x),
                Mathf.Lerp(b.yMin, b.yMax, local01.y));
            return ViewportToPlane(cam, vp);
        }

        /// <summary>Approximate world radius of the anchored region (half its larger extent).</summary>
        public float WorldRadius(Vector3[] corners)
        {
            if (corners == null || corners.Length < 4) return 0.05f;
            float w = Vector3.Distance(corners[0], corners[1]);
            float h = Vector3.Distance(corners[0], corners[3]);
            return Mathf.Max(w, h) * 0.5f;
        }

        private Vector3 ViewportToPlane(Camera cam, Vector2 viewport)
        {
            // bbox y is top-down (image space); Unity viewport is bottom-up.
            Ray ray = cam.ViewportPointToRay(new Vector3(viewport.x, 1f - viewport.y, 0f));
            return ray.GetPoint(_planeDistance);
        }

        private static Rect BboxToRect(Dictionary<string, object> bbox, Rect fallback)
        {
            if (bbox == null) return fallback;
            float x = (float)IntentRead.Num(bbox, "x", fallback.x);
            float y = (float)IntentRead.Num(bbox, "y", fallback.y);
            float w = (float)IntentRead.Num(bbox, "w", (float)IntentRead.Num(bbox, "width", fallback.width));
            float h = (float)IntentRead.Num(bbox, "h", (float)IntentRead.Num(bbox, "height", fallback.height));
            return new Rect(x, y, w, h);
        }
    }
}
