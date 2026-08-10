// MLOmega V19 — E53 (Viki mode aide) — atom 5/12
// TimerRing: an animated countdown ring for a step with a timer_seconds (steep for
// a boil, a rest, a glue set…). It sweeps from full to empty over the duration,
// shows the remaining mm:ss in the centre, and gives a soft (never alarming) pulse
// when it reaches zero. Anchored near the object when a track is present, else
// billboarded in front of the user. Deterministic: it advances on Tick(now,dt) so
// tests can drive it without a player loop.
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class TimerRing : TaskAtom
    {
        private const int Segments = 48;

        private LineRenderer _ring;
        private TextMeshPro _label;
        private TaskAnchorMath _anchor; // optional
        private string _trackId;
        private float _duration;
        private float _remaining;
        private bool _running;
        private bool _fired;
        private Color _accent = Color.white;
        private readonly Vector3[] _corners = new Vector3[4];
        private float _finishPhase;

        /// <summary>True the frame the timer reaches zero and every frame after (until reset).</summary>
        public bool Elapsed => _fired;
        public float Remaining => _remaining;

        protected override void Build()
        {
            _ring = gameObject.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = false;
            _ring.positionCount = Segments;
            _ring.widthMultiplier = 0.006f;
            _ring.numCornerVertices = 2;
            _ring.material = new Material(Shader.Find("MLOmega/XREAL Runtime Unlit"));

            var go = new GameObject("TimerText", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _label = go.AddComponent<TextMeshPro>();
            _label.fontSize = 0.05f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = Theme != null ? Theme.TextColor : Color.white;
        }

        public void SetTimer(TaskAnchorMath anchor, string trackId, float seconds, Color accent)
        {
            _anchor = anchor;
            _trackId = trackId;
            _duration = Mathf.Max(0.01f, seconds);
            _remaining = _duration;
            _accent = accent;
            _running = true;
            _fired = false;
        }

        public override void Tick(float now, float dt)
        {
            if (_ring == null) return;
            if (_running)
            {
                _remaining -= dt;
                if (_remaining <= 0f)
                {
                    _remaining = 0f;
                    _running = false;
                    _fired = true;
                }
            }

            // Anchor to the object if we have a live track, else billboard ahead.
            Vector3 center;
            if (_anchor != null && _anchor.Resolve(_trackId, _corners) )
            {
                center = _anchor.LocalToWorld(new Vector2(-0.15f, -0.15f));
            }
            else
            {
                Camera cam = Cam;
                center = cam != null
                    ? cam.transform.TransformPoint(new Vector3(-0.28f, 0.14f, 1.1f))
                    : Vector3.zero;
            }

            Camera c2 = Cam;
            Vector3 right = c2 != null ? c2.transform.right : Vector3.right;
            Vector3 up = c2 != null ? c2.transform.up : Vector3.up;
            float r = 0.05f;
            float frac = _duration > 0f ? _remaining / _duration : 0f;
            int shown = Mathf.Clamp(Mathf.CeilToInt(frac * Segments), 1, Segments);
            for (int i = 0; i < Segments; i++)
            {
                float a = Mathf.PI * 0.5f - (i / (float)Segments) * Mathf.PI * 2f;
                Vector3 p = center + right * (Mathf.Cos(a) * r) + up * (Mathf.Sin(a) * r);
                _ring.SetPosition(i, i < shown ? p : center); // collapse consumed arc to centre
            }

            float extra = 1f;
            if (_fired)
            {
                _finishPhase += dt * 3f;
                extra = 0.5f + 0.5f * (0.5f + 0.5f * Mathf.Sin(_finishPhase)); // gentle end pulse
            }
            Color col = WithAlpha(_accent, extra);
            _ring.startColor = col; _ring.endColor = col;

            if (_label != null)
            {
                _label.transform.position = center;
                Billboard(_label.transform);
                _label.text = _fired ? "0:00" : Format(_remaining);
                _label.color = WithAlpha(Theme != null ? Theme.TextColor : Color.white);
            }
        }

        private static string Format(float s)
        {
            int total = Mathf.CeilToInt(s);
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
