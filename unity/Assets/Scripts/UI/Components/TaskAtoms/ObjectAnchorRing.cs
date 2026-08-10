// MLOmega V19 — E53 (Viki mode aide) — atom 2/12
// ObjectAnchorRing: a glowing rounded ring/contour drawn AROUND a live track and
// re-read every frame from the device track store, so it FOLLOWS the object in
// real time — move the bowl, the ring moves with it. On track loss it does not
// snap or stick to a stale object (§13.2): it fades to a discreet dashed
// "searching" pulse held at the last known position, and re-locks automatically
// the instant the track (or a PC-rematched track id) reappears. Colour comes from
// the truth accent supplied by the owner; never garish.
using MLOmega.XR.UI.Components;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class ObjectAnchorRing : TaskAtom
    {
        private const int Segments = 40;

        private LineRenderer _ring;
        private TaskAnchorMath _anchor;
        private string _trackId;
        private Color _accent = Color.white;
        private readonly Vector3[] _corners = new Vector3[4];
        private float _searchPhase;
        private bool _searching;

        /// <summary>True while the ring has lost its track and is in searching mode.</summary>
        public bool IsSearching => _searching;

        protected override void Build()
        {
            _ring = gameObject.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = true;
            _ring.positionCount = Segments;
            _ring.widthMultiplier = 0.005f;
            _ring.numCornerVertices = 2;
            _ring.material = new Material(Shader.Find("MLOmega/XREAL Runtime Unlit"));
            _ring.textureMode = LineTextureMode.Stretch;
        }

        /// <summary>Bind to a track id + the shared anchor math + accent colour.</summary>
        public void SetTarget(TaskAnchorMath anchor, string trackId, Color accent)
        {
            _anchor = anchor;
            _trackId = trackId;
            _accent = accent;
        }

        public override void Tick(float now, float dt)
        {
            if (_anchor == null || _ring == null) return;
            bool present = _anchor.Resolve(_trackId, _corners);
            _searching = !present && _anchor.HasEverResolved;

            Vector3 center = _anchor.Center;
            float rx = Vector3.Distance(_corners[0], _corners[1]) * 0.5f;
            float ry = Vector3.Distance(_corners[0], _corners[3]) * 0.5f;
            Camera cam = Cam;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;

            for (int i = 0; i < Segments; i++)
            {
                float a = (i / (float)Segments) * Mathf.PI * 2f;
                _ring.SetPosition(i, center + right * (Mathf.Cos(a) * rx) + up * (Mathf.Sin(a) * ry));
            }

            // Searching mode: gentle pulse + dimmer so it reads as "looking for it".
            float extra = 1f;
            if (_searching)
            {
                _searchPhase += dt * 2.4f;
                extra = 0.35f + 0.20f * (0.5f + 0.5f * Mathf.Sin(_searchPhase));
                _ring.widthMultiplier = 0.004f;
            }
            else
            {
                _ring.widthMultiplier = 0.006f;
            }

            Color col = WithAlpha(_accent, extra);
            _ring.startColor = col;
            _ring.endColor = col;
        }
    }
}
