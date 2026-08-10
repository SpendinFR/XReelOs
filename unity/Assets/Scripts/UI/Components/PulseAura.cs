using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Experimental rPPG aura. It visualises a measured optical periodicity with
    /// its quality; it never labels stress, emotion or medical state.
    /// </summary>
    public sealed class PulseAura : UIComponentBase
    {
        private const int Segments = 72;
        [SerializeField] private float _planeDistance = 1.35f;

        private readonly List<LineRenderer> _rings = new List<LineRenderer>();
        private Rect _bbox;
        private float _bpm;
        private float _quality;
        private TextMeshPro _chip;

        public override string ComponentKey => "pulse_aura";

        protected override void OnConfigured()
        {
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("PulseRing" + i);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = Segments;
                line.widthMultiplier = 0.0035f - i * 0.0006f;
                line.material = new Material(
                    Shader.Find("MLOmega/XREAL Runtime Unlit"));
                _rings.Add(line);
            }
            var chipGo = new GameObject("PulseQuality");
            chipGo.transform.SetParent(transform, false);
            _chip = chipGo.AddComponent<TextMeshPro>();
            _chip.fontSize = 0.035f;
            _chip.alignment = TextAlignmentOptions.Center;
        }

        protected override void Bind(Contracts.V19.UIIntent intent)
        {
            if (!TryReadPulse(intent, out _bbox, out _bpm, out _quality))
            {
                foreach (LineRenderer ring in _rings) ring.enabled = false;
                if (_chip != null) _chip.enabled = false;
                return;
            }
            if (_chip != null)
            {
                _chip.enabled = true;
                _chip.text =
                    $"<color=#FF63D8>≈ {_bpm:0} bpm</color>\n" +
                    $"<size=68%><color=#9FB3C8>rPPG exp. · qualité {_quality:P0}</color></size>";
            }
        }

        protected override void OnTruth(TruthDescriptor truth) { }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || _quality <= 0f) return;
            Draw();
        }

        private void Draw()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null) return;
            float phase = Time.unscaledTime * (_bpm / 60f) * Mathf.PI * 2f;
            float beat = 0.5f + 0.5f * Mathf.Sin(phase);
            Vector2 center = new Vector2(
                _bbox.center.x,
                1f - _bbox.center.y);
            float radiusX = Mathf.Max(0.04f, _bbox.width * 0.65f);
            float radiusY = Mathf.Max(0.06f, _bbox.height * 0.68f);
            for (int ringIndex = 0; ringIndex < _rings.Count; ringIndex++)
            {
                LineRenderer ring = _rings[ringIndex];
                ring.enabled = true;
                float expansion =
                    1f + ringIndex * 0.12f + beat * (0.025f + ringIndex * 0.008f);
                for (int i = 0; i < Segments; i++)
                {
                    float a = i / (float)Segments * Mathf.PI * 2f;
                    Vector2 point = center + new Vector2(
                        Mathf.Cos(a) * radiusX * expansion,
                        Mathf.Sin(a) * radiusY * expansion);
                    ring.SetPosition(
                        i,
                        cam.ViewportPointToRay(point).GetPoint(_planeDistance));
                }
                Color colour = Color.Lerp(
                    new Color(0.25f, 0.76f, 1f),
                    new Color(1f, 0.24f, 0.72f),
                    beat);
                colour.a = CurrentAlpha * _quality * (1f - ringIndex * 0.18f);
                ring.startColor = colour;
                ring.endColor = colour;
            }
            if (_chip != null)
            {
                Vector2 chipPoint = center + new Vector2(0f, radiusY * 1.35f);
                Vector3 position =
                    cam.ViewportPointToRay(chipPoint).GetPoint(_planeDistance);
                _chip.transform.SetPositionAndRotation(
                    position,
                    Quaternion.LookRotation(
                        position - cam.transform.position,
                        Vector3.up));
                Color colour = _chip.color;
                colour.a = CurrentAlpha;
                _chip.color = colour;
            }
        }

        public static bool TryReadPulse(
            Contracts.V19.UIIntent intent,
            out Rect bbox,
            out float bpm,
            out float quality)
        {
            bbox = default;
            bpm = (float)IntentRead.Num(intent?.Content, "bpm", 0d);
            quality = (float)IntentRead.Num(intent?.Content, "signal_quality", 0d);
            return intent != null &&
                IntentRead.Flag(intent.Content, "experimental", false) &&
                !IntentRead.Flag(intent.Content, "persisted", true) &&
                IntentRead.TryRect(intent.Anchor, "bbox", out bbox) &&
                bpm >= 40f &&
                bpm <= 200f &&
                quality >= 0.45f &&
                quality <= 1f;
        }
    }
}
