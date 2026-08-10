// MLOmega V19 — E53 (Viki mode aide) — atom 3/12
// TrajectoryGesture: the gesture-to-perform rendered as an animated glowing trace
// anchored to the object (the tractable, beautiful alternative to a 3D hand). It is
// fully parameterised by the task_anchor content:
//   * arc      — pour: an arc from `from` to `to` (bottle → bowl) with a travelling head
//   * circular — screw/turn: a looping circular arrow around the object centre
//   * linear   — wipe/move: a back-and-forth sweep between `from` and `to`
//   * pulse    — press: a soft expanding pulse ring on the target zone
// The trace draws itself with a soft looping progress, never harsh. Anchored via
// the shared TaskAnchorMath so it tracks the object; from/to default to sensible
// on-object points when the PC omits them.
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class TrajectoryGesture : TaskAtom
    {
        private const int Points = 32;

        private LineRenderer _trace;
        private TaskAnchorMath _anchor;
        private TaskAnchorMath _fromAnchor;
        private TaskAnchorMath _toAnchor;
        private string _trackId;
        private string _fromTrackId;
        private string _toTrackId;
        private GestureKind _kind = GestureKind.None;
        private Vector2 _from = new Vector2(0.5f, 1.2f); // default: above the object
        private Vector2 _to = new Vector2(0.5f, 0.5f);   // default: object centre
        private Color _accent = Color.white;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3[] _fromCorners = new Vector3[4];
        private readonly Vector3[] _toCorners = new Vector3[4];
        private float _phase;

        protected override void Build()
        {
            _trace = gameObject.AddComponent<LineRenderer>();
            _trace.useWorldSpace = true;
            _trace.loop = false;
            _trace.positionCount = Points;
            _trace.widthMultiplier = 0.007f;
            _trace.numCornerVertices = 3;
            _trace.material = new Material(Shader.Find("MLOmega/XREAL Runtime Unlit"));
            _trace.textureMode = LineTextureMode.Stretch;
        }

        public void SetGesture(TaskAnchorMath anchor, string trackId, GestureKind kind,
            Vector2 from, bool hasFrom, Vector2 to, bool hasTo, Color accent,
            string fromTrackId = null, string toTrackId = null, float planeDistance = 1.4f)
        {
            _anchor = anchor;
            _trackId = trackId;
            _kind = kind;
            if (hasFrom) _from = from;
            if (hasTo) _to = to;
            _accent = accent;
            _fromTrackId = fromTrackId;
            _toTrackId = toTrackId;
            _fromAnchor = !string.IsNullOrEmpty(fromTrackId)
                ? new TaskAnchorMath(Context != null ? Context.SceneCache : null, Cam, planeDistance)
                : null;
            _toAnchor = !string.IsNullOrEmpty(toTrackId)
                ? new TaskAnchorMath(Context != null ? Context.SceneCache : null, Cam, planeDistance)
                : null;
            _trace.loop = kind == GestureKind.Circular || kind == GestureKind.Pulse;
        }

        public override void Tick(float now, float dt)
        {
            if (_anchor == null || _trace == null || _kind == GestureKind.None) return;
            _anchor.Resolve(_trackId, _corners);
            if (_fromAnchor != null) _fromAnchor.Resolve(_fromTrackId, _fromCorners);
            if (_toAnchor != null) _toAnchor.Resolve(_toTrackId, _toCorners);
            _phase += dt * 0.9f;
            float loop = Mathf.Repeat(_phase, 1f);

            switch (_kind)
            {
                case GestureKind.Arc: DrawArc(loop); break;
                case GestureKind.Circular: DrawCircular(); break;
                case GestureKind.Linear: DrawLinear(loop); break;
                case GestureKind.Pulse: DrawPulse(loop); break;
            }

            // The trace fades its head/tail so it reads as a soft, directional stroke.
            Color col = WithAlpha(_accent);
            _trace.startColor = new Color(col.r, col.g, col.b, col.a * 0.15f);
            _trace.endColor = col;
        }

        // Arc: quadratic Bézier from→to with the control point lifted, revealed by `loop`.
        private void DrawArc(float loop)
        {
            Vector3 p0 = FromPoint();
            Vector3 p2 = ToPoint();
            Vector3 mid = (p0 + p2) * 0.5f;
            Camera cam = Cam;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            Vector3 ctrl = mid + up * (Vector3.Distance(p0, p2) * 0.5f);
            float reveal = 0.25f + 0.75f * loop; // grow the stroke, then loop
            for (int i = 0; i < Points; i++)
            {
                float t = (i / (float)(Points - 1)) * reveal;
                _trace.SetPosition(i, Bezier(p0, ctrl, p2, t));
            }
        }

        // Circular: a full ring around the object centre with a rotating gap head.
        private void DrawCircular()
        {
            Vector3 c = _anchor.Center;
            float r = _anchor.WorldRadius(_corners) * 1.3f;
            Camera cam = Cam;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            float head = Mathf.Repeat(_phase, 1f) * Mathf.PI * 2f;
            for (int i = 0; i < Points; i++)
            {
                float a = head + (i / (float)(Points - 1)) * Mathf.PI * 1.7f; // arrow, not full ring
                _trace.SetPosition(i, c + right * (Mathf.Cos(a) * r) + up * (Mathf.Sin(a) * r));
            }
        }

        // Linear: a straight sweep from→to whose head oscillates back and forth.
        private void DrawLinear(float loop)
        {
            Vector3 p0 = FromPoint();
            Vector3 p1 = ToPoint();
            float tri = Mathf.PingPong(_phase, 1f);
            for (int i = 0; i < Points; i++)
            {
                float t = (i / (float)(Points - 1)) * tri;
                _trace.SetPosition(i, Vector3.Lerp(p0, p1, t));
            }
        }

        // Pulse: an expanding ring on the target zone (press here).
        private void DrawPulse(float loop)
        {
            Vector3 c = ToPoint();
            float r = _anchor.WorldRadius(_corners) * (0.2f + 0.8f * loop);
            Camera cam = Cam;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            for (int i = 0; i < Points; i++)
            {
                float a = (i / (float)(Points - 1)) * Mathf.PI * 2f;
                _trace.SetPosition(i, c + right * (Mathf.Cos(a) * r) + up * (Mathf.Sin(a) * r));
            }
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        private Vector3 FromPoint() => _fromAnchor != null && _fromAnchor.TrackPresent
            ? _fromAnchor.Center : _anchor.LocalToWorld(_from);

        private Vector3 ToPoint() => _toAnchor != null && _toAnchor.TrackPresent
            ? _toAnchor.Center : _anchor.LocalToWorld(_to);
    }
}
