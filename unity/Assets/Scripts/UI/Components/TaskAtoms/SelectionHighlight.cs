// MLOmega V19 — E53 (Viki mode aide) — atom 10/12
// SelectionHighlight: "prends CELUI-LÀ". When several candidate objects share the
// same label (multiple tracks the PC couldn't disambiguate, or several bowls on the
// counter), this atom accentuates the correct track with a solid bright ring + a
// soft "celui-ci" tag, and draws faint dimming rings on the other candidate tracks
// so the eye is pulled to the right one. All rings follow their tracks in real time
// via the shared anchor math. Candidate track ids are supplied by the renderer
// (from the PC's list or from LocalTrackStore label matches).
using System.Collections.Generic;
using MLOmega.XR.Scene;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    public sealed class SelectionHighlight : TaskAtom
    {
        private const int Segments = 32;

        private sealed class Candidate
        {
            public string TrackId;
            public bool IsChosen;
            public LineRenderer Ring;
            public TaskAnchorMath Anchor;
            public readonly Vector3[] Corners = new Vector3[4];
        }

        private readonly List<Candidate> _candidates = new List<Candidate>();
        private TextMeshPro _tag;
        private Color _accent = Color.white;
        private float _pulse;

        protected override void Build()
        {
            var go = new GameObject("ChosenTag", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _tag = go.AddComponent<TextMeshPro>();
            _tag.fontSize = 0.04f;
            _tag.alignment = TextAlignmentOptions.Center;
            _tag.fontStyle = FontStyles.Bold;
            _tag.text = "celui-ci";
            _tag.color = Theme != null ? Theme.TextColor : Color.white;
        }

        /// <summary>
        /// Configure the candidate set. <paramref name="chosenTrackId"/> is the
        /// correct one; every id in <paramref name="allTrackIds"/> gets a ring.
        /// </summary>
        public void SetCandidates(SceneCache sceneCache, string chosenTrackId,
            IReadOnlyList<string> allTrackIds, Color accent)
        {
            _accent = accent;
            // Clear old rings.
            foreach (Candidate c in _candidates)
                if (c.Ring != null) Destroy(c.Ring.gameObject);
            _candidates.Clear();

            if (allTrackIds == null) return;
            foreach (string id in allTrackIds)
            {
                var ringGo = new GameObject($"Cand_{id}");
                ringGo.transform.SetParent(transform, false);
                var lr = ringGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.positionCount = Segments;
                lr.numCornerVertices = 2;
                lr.material = new Material(Shader.Find("MLOmega/XREAL Runtime Unlit"));
                _candidates.Add(new Candidate
                {
                    TrackId = id,
                    IsChosen = id == chosenTrackId,
                    Ring = lr,
                    Anchor = new TaskAnchorMath(sceneCache, Cam)
                });
            }
        }

        public override void Tick(float now, float dt)
        {
            _pulse += dt * 2.4f;
            float chosenBright = 0.8f + 0.2f * (0.5f + 0.5f * Mathf.Sin(_pulse));
            Camera cam = Cam;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;

            Vector3 chosenCenter = Vector3.zero;
            bool haveChosen = false;

            foreach (Candidate c in _candidates)
            {
                if (c.Ring == null) continue;
                c.Anchor.Resolve(c.TrackId, c.Corners);
                Vector3 center = c.Anchor.Center;
                float rx = Vector3.Distance(c.Corners[0], c.Corners[1]) * 0.5f;
                float ry = Vector3.Distance(c.Corners[0], c.Corners[3]) * 0.5f;
                for (int i = 0; i < Segments; i++)
                {
                    float a = (i / (float)Segments) * Mathf.PI * 2f;
                    c.Ring.SetPosition(i, center + right * (Mathf.Cos(a) * rx) + up * (Mathf.Sin(a) * ry));
                }

                if (c.IsChosen)
                {
                    c.Ring.widthMultiplier = 0.007f;
                    Color col = WithAlpha(_accent, chosenBright);
                    c.Ring.startColor = col; c.Ring.endColor = col;
                    chosenCenter = center; haveChosen = true;
                }
                else
                {
                    c.Ring.widthMultiplier = 0.003f;
                    Color dim = Theme != null ? Theme.MutedTextColor : new Color(1, 1, 1, 0.3f);
                    Color col = WithAlpha(dim, 0.4f);
                    c.Ring.startColor = col; c.Ring.endColor = col;
                }
            }

            if (_tag != null)
            {
                _tag.enabled = haveChosen;
                if (haveChosen)
                {
                    _tag.transform.position = chosenCenter + up * 0.10f;
                    Billboard(_tag.transform);
                    _tag.color = WithAlpha(_accent, chosenBright);
                }
            }
        }
    }
}
