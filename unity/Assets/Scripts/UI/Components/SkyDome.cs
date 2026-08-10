using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Bounded local planetarium. The PC supplies calculated azimuth/altitude and
    /// a proven tracking-space north calibration; this renderer performs no
    /// astronomy or GPS inference of its own.
    /// </summary>
    public sealed class SkyDome : UIComponentBase
    {
        private const int MaxBodies = 32;
        private const int MaxEdges = 24;
        private const float RadiusM = 14f;

        private readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
        private readonly List<LineRenderer> _edges = new List<LineRenderer>();
        private Material _lineMaterial;
        private Color _accent = new Color(0.35f, 0.85f, 1f, 1f);
        private bool _qualified;

        public override string ComponentKey => "sky_dome";
        public bool IsQualified => _qualified;
        public int VisibleBodyCount => _labels.Count;

        protected override void OnConfigured()
        {
            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Unlit/Color");
            if (shader != null) _lineMaterial = new Material(shader);
        }

        protected override void Bind(UIIntent intent)
        {
            ClearVisuals();
            _qualified = TryRead(intent, out Vector3 origin, out float northYaw,
                out List<Body> bodies, out List<Edge> edges);
            if (!_qualified) return;

            var positions = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (Body body in bodies)
            {
                Vector3 direction = Direction(body.AzimuthDeg, body.AltitudeDeg);
                Vector3 position =
                    origin + Quaternion.Euler(0f, northYaw, 0f) * direction * RadiusM;
                positions[body.Name] = position;
                var go = new GameObject("Sky-" + body.Name);
                go.transform.SetParent(transform, false);
                go.transform.position = position;
                var label = go.AddComponent<TextMeshPro>();
                label.text = (body.Kind == "planet" ? "◈ " : "• ") + body.Name;
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = body.Kind == "planet" ? 0.075f : 0.055f;
                label.fontStyle = body.Kind == "planet"
                    ? FontStyles.Bold
                    : FontStyles.Normal;
                label.enableWordWrapping = false;
                label.color = body.Kind == "sun"
                    ? new Color(1f, 0.82f, 0.28f)
                    : _accent;
                _labels.Add(label);
            }

            foreach (Edge edge in edges)
            {
                if (!positions.TryGetValue(edge.From, out Vector3 start) ||
                    !positions.TryGetValue(edge.To, out Vector3 end))
                    continue;
                var go = new GameObject(
                    "Constellation-" + edge.From + "-" + edge.To);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.widthMultiplier = 0.014f;
                line.SetPosition(0, start);
                line.SetPosition(1, end);
                line.startColor = new Color(_accent.r, _accent.g, _accent.b, 0.45f);
                line.endColor = new Color(_accent.r, _accent.g, _accent.b, 0.45f);
                if (_lineMaterial != null) line.material = _lineMaterial;
                _edges.Add(line);
            }
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            _accent = truth.Accent;
        }

        protected override void Update()
        {
            base.Update();
            Camera camera = Context != null ? Context.Camera : Camera.main;
            if (camera == null) return;
            foreach (TextMeshPro label in _labels)
            {
                if (label == null) continue;
                Vector3 forward = label.transform.position - camera.transform.position;
                if (forward.sqrMagnitude > 0.0001f)
                    label.transform.rotation =
                        Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        protected override void ApplyVisual()
        {
            float alpha = CurrentAlpha;
            foreach (TextMeshPro label in _labels)
            {
                if (label == null) continue;
                Color color = label.color;
                color.a = alpha;
                label.color = color;
            }
            foreach (LineRenderer line in _edges)
            {
                if (line == null) continue;
                Color color = new Color(_accent.r, _accent.g, _accent.b, alpha * 0.45f);
                line.startColor = color;
                line.endColor = color;
            }
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }

        private void ClearVisuals()
        {
            foreach (TextMeshPro label in _labels)
                if (label != null) Destroy(label.gameObject);
            foreach (LineRenderer line in _edges)
                if (line != null) Destroy(line.gameObject);
            _labels.Clear();
            _edges.Clear();
        }

        private static Vector3 Direction(float azimuthDeg, float altitudeDeg)
        {
            float azimuth = azimuthDeg * Mathf.Deg2Rad;
            float altitude = altitudeDeg * Mathf.Deg2Rad;
            float horizontal = Mathf.Cos(altitude);
            return new Vector3(
                Mathf.Sin(azimuth) * horizontal,
                Mathf.Sin(altitude),
                Mathf.Cos(azimuth) * horizontal);
        }

        private static bool TryRead(
            UIIntent intent,
            out Vector3 origin,
            out float northYaw,
            out List<Body> bodies,
            out List<Edge> edges)
        {
            origin = Vector3.zero;
            northYaw = float.NaN;
            bodies = new List<Body>();
            edges = new List<Edge>();
            if (intent?.Content == null || intent.Anchor == null ||
                intent.EvidenceRefs == null || intent.EvidenceRefs.Count == 0)
                return false;
            if (!string.Equals(
                    IntentRead.Anchor(intent, "coordinate_space"),
                    "tracking_local",
                    StringComparison.Ordinal))
                return false;
            northYaw = (float)IntentRead.Num(
                intent.Content, "world_north_yaw_deg", double.NaN);
            if (!Finite(northYaw) ||
                string.IsNullOrWhiteSpace(
                    IntentRead.Content(intent, "calibration_id")))
                return false;
            if (!TryVector(intent.Anchor, "position", out origin))
                return false;
            if (!intent.Content.TryGetValue("bodies", out object rawBodies))
                return false;
            JArray array;
            try { array = rawBodies as JArray ?? JArray.FromObject(rawBodies); }
            catch { return false; }
            foreach (JToken token in array)
            {
                if (!(token is JObject row) || bodies.Count >= MaxBodies) break;
                string name = (row.Value<string>("name") ?? "").Trim();
                float azimuth = row.Value<float?>("azimuth_deg") ?? float.NaN;
                float altitude = row.Value<float?>("altitude_deg") ?? float.NaN;
                if (string.IsNullOrWhiteSpace(name) ||
                    !Finite(azimuth) || !Finite(altitude) ||
                    altitude < -15f || altitude > 90f)
                    continue;
                bodies.Add(new Body
                {
                    Name = name.Length <= 40 ? name : name.Substring(0, 40),
                    Kind = (row.Value<string>("kind") ?? "star").Trim(),
                    AzimuthDeg = Mathf.Repeat(azimuth, 360f),
                    AltitudeDeg = altitude,
                });
            }
            if (intent.Content.TryGetValue(
                    "constellation_edges", out object rawEdges))
            {
                try
                {
                    JArray edgeArray = rawEdges as JArray ??
                        JArray.FromObject(rawEdges);
                    foreach (JToken token in edgeArray)
                    {
                        if (!(token is JObject row) || edges.Count >= MaxEdges) break;
                        string from = (row.Value<string>("from") ?? "").Trim();
                        string to = (row.Value<string>("to") ?? "").Trim();
                        if (from.Length > 0 && to.Length > 0)
                            edges.Add(new Edge { From = from, To = to });
                    }
                }
                catch { }
            }
            return bodies.Count > 0;
        }

        private static bool TryVector(
            Dictionary<string, object> source,
            string key,
            out Vector3 value)
        {
            value = default;
            if (source == null || !source.TryGetValue(key, out object raw) ||
                raw == null)
                return false;
            JObject obj;
            try { obj = raw as JObject ?? JObject.FromObject(raw); }
            catch { return false; }
            float x = obj.Value<float?>("x") ?? float.NaN;
            float y = obj.Value<float?>("y") ?? float.NaN;
            float z = obj.Value<float?>("z") ?? float.NaN;
            if (!Finite(x) || !Finite(y) || !Finite(z)) return false;
            value = new Vector3(x, y, z);
            return true;
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class Body
        {
            public string Name;
            public string Kind;
            public float AzimuthDeg;
            public float AltitudeDeg;
        }

        private sealed class Edge
        {
            public string From;
            public string To;
        }
    }
}
