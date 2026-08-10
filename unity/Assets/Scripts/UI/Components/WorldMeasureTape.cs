using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>Depth-proven 3D tape with explicit uncertainty.</summary>
    public sealed class WorldMeasureTape : UIComponentBase
    {
        public sealed class Measure
        {
            public Vector3 Start;
            public Vector3 End;
            public float DistanceM;
            public float UncertaintyM;
            public float Quality;
            public string CalibrationId;
            public string Label;
        }

        private LineRenderer _beam;
        private LineRenderer _startRing;
        private LineRenderer _endRing;
        private TextMeshPro _label;
        private Material _material;
        private Measure _measure;
        private bool _qualified;

        public override string ComponentKey => "world_measure";

        protected override void OnConfigured()
        {
            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");
            if (shader != null) _material = new Material(shader);
            _beam = MakeLine("MeasureBeam", false, 2, 0.022f);
            _startRing = MakeLine("MeasureStart", true, 32, 0.018f);
            _endRing = MakeLine("MeasureEnd", true, 32, 0.018f);
            var labelGo = new GameObject("MeasureLabel");
            labelGo.transform.SetParent(transform, false);
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 0.07f;
            _label.enableWordWrapping = false;
        }

        protected override void Bind(UIIntent intent)
        {
            _qualified = TryReadMeasure(intent, out _measure, out _);
            SetEnabled(_qualified);
            if (!_qualified) return;
            _beam.SetPosition(0, _measure.Start);
            _beam.SetPosition(1, _measure.End);
            DrawRing(_startRing, _measure.Start, 0.07f);
            DrawRing(_endRing, _measure.End, 0.07f);
            Vector3 midpoint = (_measure.Start + _measure.End) * 0.5f;
            _label.transform.position = midpoint + Vector3.up * 0.12f;
            string value = _measure.DistanceM < 1f
                ? $"{_measure.DistanceM * 100f:0.0} CM"
                : $"{_measure.DistanceM:0.00} M";
            string uncertainty = _measure.UncertaintyM < 0.01f
                ? $"± {_measure.UncertaintyM * 1000f:0} MM"
                : $"± {_measure.UncertaintyM * 100f:0.0} CM";
            _label.text =
                $"<color=#66F8FF>{value}</color>\n" +
                $"<size=58%>{uncertainty} • DEPTH</size>";
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || !_qualified) return;
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam != null)
            {
                Vector3 forward = _label.transform.position - cam.transform.position;
                if (forward.sqrMagnitude > 0.0001f)
                    _label.transform.rotation =
                        Quaternion.LookRotation(forward, Vector3.up);
            }
            ApplyAlpha(CurrentAlpha);
        }

        protected override void ApplyVisual() => ApplyAlpha(CurrentAlpha);

        private LineRenderer MakeLine(
            string name, bool loop, int count, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = count;
            line.widthMultiplier = width;
            line.numCornerVertices = 4;
            if (_material != null) line.material = _material;
            return line;
        }

        private static void DrawRing(
            LineRenderer line, Vector3 center, float radius)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = i / (float)line.positionCount * Mathf.PI * 2f;
                line.SetPosition(
                    i,
                    center + new Vector3(
                        Mathf.Cos(a) * radius,
                        0f,
                        Mathf.Sin(a) * radius));
            }
        }

        private void ApplyAlpha(float alpha)
        {
            Color color = new Color(0.16f, 0.95f, 1f, Mathf.Clamp01(alpha));
            foreach (LineRenderer line in Lines())
            {
                if (line == null) continue;
                line.startColor = color;
                line.endColor = color;
            }
            if (_label != null)
            {
                Color text = Color.white;
                text.a = color.a;
                _label.color = text;
            }
        }

        private IEnumerable<LineRenderer> Lines()
        {
            yield return _beam;
            yield return _startRing;
            yield return _endRing;
        }

        private void SetEnabled(bool enabled)
        {
            foreach (LineRenderer line in Lines())
                if (line != null) line.enabled = enabled;
            if (_label != null) _label.enabled = enabled;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public static bool TryReadMeasure(
            UIIntent intent,
            out Measure measure,
            out string error)
        {
            measure = null;
            if (!WorldContractRead.TrackingGate(
                    intent, true, 0.75f, out float quality, out error))
                return false;
            if (!IntentRead.Flag(intent.Content, "intrinsics_valid"))
            {
                error = "intrinsics_required";
                return false;
            }
            if (
                !intent.Content.TryGetValue("start", out object startRaw) ||
                !intent.Content.TryGetValue("end", out object endRaw) ||
                !WorldContractRead.TryVector(startRaw, out Vector3 start) ||
                !WorldContractRead.TryVector(endRaw, out Vector3 end))
            {
                error = "measurement_points_invalid";
                return false;
            }
            float actual = Vector3.Distance(start, end);
            float claimed = (float)IntentRead.Num(
                intent.Content, "distance_m", double.NaN);
            float uncertainty = (float)IntentRead.Num(
                intent.Content, "uncertainty_m", double.NaN);
            if (
                actual < 0.01f ||
                actual > 65f ||
                !WorldContractRead.Finite(claimed) ||
                Mathf.Abs(actual - claimed) > Mathf.Max(0.02f, actual * 0.02f) ||
                !WorldContractRead.Finite(uncertainty) ||
                uncertainty <= 0f ||
                uncertainty > Mathf.Max(0.5f, actual * 0.25f))
            {
                error = "measurement_consistency_failed";
                return false;
            }
            measure = new Measure
            {
                Start = start,
                End = end,
                DistanceM = actual,
                UncertaintyM = uncertainty,
                Quality = quality,
                CalibrationId = IntentRead.Content(
                    intent, "calibration_id", ""),
                Label = IntentRead.Content(intent, "label", ""),
            };
            error = null;
            return true;
        }
    }
}
