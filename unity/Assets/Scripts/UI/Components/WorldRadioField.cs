using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Holographic interpolation of measured Wi-Fi/BLE RSSI samples. It is a
    /// signal-strength map, never a claim to render physical EM waves.
    /// </summary>
    public sealed class WorldRadioField : UIComponentBase
    {
        public sealed class Sample
        {
            public Vector3 Position;
            public float RssiDbm;
            public string Source;
            public string NetworkId;
        }

        public sealed class Field
        {
            public readonly List<Sample> Samples = new List<Sample>();
            public float Quality;
            public string CalibrationId;
        }

        private const int MaximumSamples = 24;
        private readonly List<LineRenderer> _rings = new List<LineRenderer>();
        private Material _material;
        private TextMeshPro _label;
        private Field _field;
        private bool _qualified;

        public override string ComponentKey => "world_radio";

        protected override void OnConfigured()
        {
            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");
            if (shader != null) _material = new Material(shader);
            var labelGo = new GameObject("RadioFieldLabel");
            labelGo.transform.SetParent(transform, false);
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 0.055f;
            _label.enableWordWrapping = false;
        }

        protected override void Bind(UIIntent intent)
        {
            _qualified = TryReadField(intent, out _field, out _);
            SetEnabled(_qualified);
            if (!_qualified) return;
            int ringsNeeded = _field.Samples.Count * 3;
            EnsureCapacity(ringsNeeded);
            Sample strongest = _field.Samples[0];
            int lineIndex = 0;
            foreach (Sample sample in _field.Samples)
            {
                if (sample.RssiDbm > strongest.RssiDbm) strongest = sample;
                float strength = Mathf.InverseLerp(-100f, -25f, sample.RssiDbm);
                Color color = Color.Lerp(
                    new Color(0.3f, 0.25f, 1f, 1f),
                    new Color(0.05f, 1f, 0.72f, 1f),
                    strength);
                for (int layer = 0; layer < 3; layer++)
                {
                    LineRenderer ring = _rings[lineIndex++];
                    ring.enabled = true;
                    ring.widthMultiplier = 0.012f + strength * 0.014f;
                    float radius = 0.14f + layer * 0.15f + strength * 0.18f;
                    float lift = 0.025f + layer * 0.075f;
                    DrawRing(ring, sample.Position + Vector3.up * lift, radius);
                    color.a = 0.26f + strength * 0.54f;
                    ring.startColor = color;
                    ring.endColor = color;
                }
            }
            for (int i = lineIndex; i < _rings.Count; i++)
                _rings[i].enabled = false;
            _label.transform.position = strongest.Position + Vector3.up * 0.72f;
            _label.text =
                "<color=#5EFFE0>CARTE RADIO</color>\n" +
                $"<size=62%>{strongest.NetworkId} • " +
                $"{strongest.RssiDbm:0} DBM • MESURÉ</size>";
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || !_qualified) return;
            float pulse = 0.8f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.2f;
            foreach (LineRenderer ring in _rings)
            {
                if (!ring.enabled) continue;
                Color color = ring.startColor;
                color.a = Mathf.Clamp01(
                    CurrentAlpha * Mathf.Lerp(0.32f, 0.78f, pulse));
                ring.startColor = color;
                ring.endColor = color;
            }
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam != null)
            {
                Vector3 forward = _label.transform.position - cam.transform.position;
                if (forward.sqrMagnitude > 0.0001f)
                    _label.transform.rotation =
                        Quaternion.LookRotation(forward, Vector3.up);
            }
            Color text = Color.white;
            text.a = CurrentAlpha;
            _label.color = text;
        }

        protected override void ApplyVisual() { }

        private void EnsureCapacity(int count)
        {
            while (_rings.Count < count)
            {
                var go = new GameObject("RadioRing" + _rings.Count);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = 40;
                line.numCornerVertices = 3;
                if (_material != null) line.material = _material;
                _rings.Add(line);
            }
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

        private void SetEnabled(bool enabled)
        {
            foreach (LineRenderer ring in _rings) ring.enabled = enabled;
            if (_label != null) _label.enabled = enabled;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public static bool TryReadField(
            UIIntent intent,
            out Field field,
            out string error)
        {
            field = null;
            if (!WorldContractRead.TrackingGate(
                    intent, false, 0.60f, out float quality, out error))
                return false;
            if (!IntentRead.Flag(intent.Content, "pseudonymized"))
            {
                error = "radio_identity_not_pseudonymized";
                return false;
            }
            if (
                !intent.Content.TryGetValue("samples", out object raw) ||
                !WorldContractRead.TryArray(raw, out JArray array) ||
                array.Count < 2 ||
                array.Count > MaximumSamples)
            {
                error = "radio_samples_invalid";
                return false;
            }
            var decoded = new Field
            {
                Quality = quality,
                CalibrationId = IntentRead.Content(
                    intent, "calibration_id", ""),
            };
            foreach (JToken token in array)
            {
                if (!(token is JObject obj) ||
                    !WorldContractRead.TryVector(
                        obj["position"], out Vector3 position))
                {
                    error = "radio_sample_position_invalid";
                    return false;
                }
                string source = obj.Value<string>("source")?.Trim();
                string id = obj.Value<string>("network_id")?.Trim();
                float rssi = obj.Value<float?>("rssi_dbm") ?? float.NaN;
                if (
                    (source != "wifi" && source != "ble") ||
                    string.IsNullOrWhiteSpace(id) ||
                    id.Length > 32 ||
                    !id.StartsWith("radio-", StringComparison.Ordinal) ||
                    !WorldContractRead.Finite(rssi) ||
                    rssi < -120f ||
                    rssi > -10f)
                {
                    error = "radio_sample_invalid";
                    return false;
                }
                decoded.Samples.Add(new Sample
                {
                    Position = position,
                    RssiDbm = rssi,
                    Source = source,
                    NetworkId = id,
                });
            }
            field = decoded;
            error = null;
            return true;
        }
    }
}
