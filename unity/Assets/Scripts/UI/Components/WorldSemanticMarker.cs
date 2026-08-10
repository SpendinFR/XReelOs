using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Compact world-locked annotation for a place, storefront, sign, object or
    /// memory. It is deliberately not a full profile card: the world stays
    /// readable while gaze/pinch can still open the detailed card.
    /// </summary>
    public sealed class WorldSemanticMarker : UIComponentBase
    {
        private static readonly List<WorldSemanticMarker> Live =
            new List<WorldSemanticMarker>();

        public sealed class Marker
        {
            public Vector3 Position;
            public string MarkerId;
            public string CalibrationId;
            public string Label;
            public string Subtitle;
            public string Kind;
            public float DistanceM;
            public float AnchorQuality;
            public bool DepthValid;
        }

        private LineRenderer _stem;
        private LineRenderer _halo;
        private LineRenderer _bracket;
        private TextMeshPro _label;
        private Material _material;
        private Marker _marker;
        private bool _qualified;
        private Color _truthAccent = new Color(0.3f, 0.95f, 1f, 1f);
        private Color _kindAccent = new Color(0.3f, 0.95f, 1f, 1f);

        public override string ComponentKey => "world_marker";
        public bool IsQualified => _qualified;
        public string MarkerId => _marker?.MarkerId ?? string.Empty;
        public string Label => _marker?.Label ?? string.Empty;

        protected override void OnConfigured()
        {
            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Unlit/Color");
            if (shader != null) _material = new Material(shader);

            _stem = MakeLine("MarkerStem", 0.011f, false, 2);
            _halo = MakeLine("MarkerHalo", 0.014f, true, 32);
            _bracket = MakeLine("MarkerBracket", 0.016f, false, 5);

            var labelGo = new GameObject("WorldLabel");
            labelGo.transform.SetParent(transform, false);
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontStyle = FontStyles.Bold;
            _label.fontSize = 0.045f;
            _label.enableWordWrapping = false;
            _label.color = Color.white;
        }

        protected override void Bind(UIIntent intent)
        {
            _qualified = TryReadMarker(intent, out _marker, out _);
            if (_qualified)
            {
                if (!Live.Contains(this)) Live.Add(this);
            }
            else
            {
                Live.Remove(this);
            }
            SetGeometryEnabled(_qualified);
            if (!_qualified) return;
            _kindAccent = KindColor(_marker.Kind);

            string distance = _marker.DistanceM > 0f
                ? (_marker.DistanceM >= 1000f
                    ? (_marker.DistanceM / 1000f).ToString("0.0") + " KM"
                    : Mathf.RoundToInt(_marker.DistanceM) + " M")
                : string.Empty;
            string detail = JoinDetails(_marker.Subtitle, distance);
            _label.text =
                $"<color=#7FF6FF>{KindGlyph(_marker.Kind)}</color> {_marker.Label}" +
                (string.IsNullOrWhiteSpace(detail)
                    ? string.Empty
                    : $"\n<size=62%><color=#A9BACB>{detail}</color></size>");
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            _truthAccent = truth.Accent;
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || !_qualified)
            {
                Live.Remove(this);
                return;
            }
            if (!Live.Contains(this)) Live.Add(this);
            Draw(Time.unscaledTime);
        }

        protected override void ApplyVisual()
        {
            ApplyColors(CurrentAlpha);
        }

        private void Draw(float now)
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            Vector3 origin = _marker.Position;
            Vector3 up = Vector3.up;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            float bob = Mathf.Sin(now * 2.1f) * 0.025f;
            float height = 0.42f + bob;
            Vector3 center = origin + up * height;
            _stem.SetPosition(0, origin + up * 0.025f);
            _stem.SetPosition(1, center);

            float radius = 0.105f + Mathf.Sin(now * 3f) * 0.008f;
            for (int i = 0; i < _halo.positionCount; i++)
            {
                float a = i / (float)_halo.positionCount * Mathf.PI * 2f;
                _halo.SetPosition(
                    i,
                    origin + up * 0.025f +
                    new Vector3(
                        Mathf.Cos(a) * radius,
                        0f,
                        Mathf.Sin(a) * radius));
            }

            float w = 0.23f;
            float h = 0.105f;
            _bracket.SetPosition(0, center - right * w - up * h);
            _bracket.SetPosition(1, center - right * w + up * h);
            _bracket.SetPosition(2, center + right * w + up * h);
            _bracket.SetPosition(3, center + right * w - up * h);
            _bracket.SetPosition(4, center - right * w - up * h);

            _label.transform.position = center + up * 0.01f;
            if (cam != null)
            {
                Vector3 forward = _label.transform.position - cam.transform.position;
                if (forward.sqrMagnitude > 0.0001f)
                    _label.transform.rotation =
                        Quaternion.LookRotation(forward, Vector3.up);
            }
            ApplyColors(CurrentAlpha);
        }

        private LineRenderer MakeLine(
            string name, float width, bool loop, int positionCount)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = positionCount;
            line.widthMultiplier = width;
            line.alignment = LineAlignment.View;
            line.numCornerVertices = 3;
            if (_material != null) line.material = _material;
            return line;
        }

        private void ApplyColors(float alpha)
        {
            bool observed = string.Equals(
                Intent?.TruthLevel, "observed", StringComparison.OrdinalIgnoreCase);
            Color color = observed ? _kindAccent : _truthAccent;
            color.a = Mathf.Clamp01(alpha);
            foreach (LineRenderer line in Lines())
            {
                if (line == null) continue;
                line.startColor = color;
                line.endColor = color;
            }
            if (_label != null)
            {
                Color label = Color.white;
                label.a = color.a;
                _label.color = label;
            }
        }

        private IEnumerable<LineRenderer> Lines()
        {
            yield return _stem;
            yield return _halo;
            yield return _bracket;
        }

        private void SetGeometryEnabled(bool enabled)
        {
            foreach (LineRenderer line in Lines())
                if (line != null) line.enabled = enabled;
            if (_label != null) _label.enabled = enabled;
        }

        private void OnDestroy()
        {
            Live.Remove(this);
            if (_material != null) Destroy(_material);
        }

        private void OnDisable() => Live.Remove(this);

        public static bool TryResolveAtViewport(
            Camera camera,
            Vector2 viewport,
            out WorldSemanticMarker marker)
        {
            marker = null;
            if (
                camera == null ||
                viewport.x < 0f || viewport.x > 1f ||
                viewport.y < 0f || viewport.y > 1f)
                return false;
            float best = 0.065f * 0.065f;
            // Bind registers a qualified marker immediately, before its first
            // Update. Resolve that authoritative list first, then scan the active
            // hierarchy as a pool/re-enable safety net.
            var candidates = new List<WorldSemanticMarker>(Live);
            foreach (WorldSemanticMarker candidate in FindObjectsByType<
                WorldSemanticMarker>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None))
            {
                if (!candidates.Contains(candidate)) candidates.Add(candidate);
            }
            // EditMode and the first frame of a freshly instantiated pool do not
            // always expose the object through FindObjectsByType yet. Restrict
            // Resources' broader view to active scene instances (never assets).
            foreach (WorldSemanticMarker candidate in
                Resources.FindObjectsOfTypeAll<WorldSemanticMarker>())
            {
                if (
                    candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.gameObject.activeInHierarchy &&
                    !candidates.Contains(candidate))
                    candidates.Add(candidate);
            }
            foreach (WorldSemanticMarker candidate in candidates)
            {
                if (
                    candidate == null ||
                    !candidate._qualified ||
                    candidate.Phase == UIComponentPhase.Idle ||
                    candidate._marker == null)
                    continue;
                Vector3 world = candidate._marker.Position + Vector3.up * 0.42f;
                Vector3 projected = camera.WorldToViewportPoint(world);
                if (projected.z <= 0f) continue;
                float distance = (
                    new Vector2(projected.x, projected.y) - viewport
                ).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                marker = candidate;
            }
            return marker != null;
        }

        public static bool TryReadMarker(
            UIIntent intent,
            out Marker marker,
            out string error)
        {
            marker = null;
            error = "invalid_marker";
            if (intent?.Content == null || intent.Anchor == null)
                return false;
            if (!string.Equals(
                    IntentRead.Anchor(intent, "coordinate_space", ""),
                    "tracking_local",
                    StringComparison.Ordinal))
            {
                error = "unsupported_coordinate_space";
                return false;
            }
            if (
                !IntentRead.Flag(intent.Content, "pose_valid") ||
                string.IsNullOrWhiteSpace(IntentRead.Content(
                    intent, "calibration_id")))
            {
                error = "unproven_tracking_calibration";
                return false;
            }
            float quality = (float)IntentRead.Num(
                intent.Content, "anchor_quality", double.NaN);
            if (float.IsNaN(quality) || quality < 0.7f)
            {
                error = "anchor_quality_below_threshold";
                return false;
            }
            if (intent.EvidenceRefs == null || intent.EvidenceRefs.Count == 0)
            {
                error = "evidence_missing";
                return false;
            }
            if (!TryPosition(intent.Anchor, out Vector3 position))
            {
                error = "position_invalid";
                return false;
            }
            string label = IntentRead.Content(intent, "label", "").Trim();
            string markerId = IntentRead.Content(intent, "marker_id", "").Trim();
            if (string.IsNullOrWhiteSpace(label) ||
                string.IsNullOrWhiteSpace(markerId))
            {
                error = "marker_identity_missing";
                return false;
            }
            marker = new Marker
            {
                Position = position,
                MarkerId = markerId,
                CalibrationId = IntentRead.Content(intent, "calibration_id", ""),
                Label = label,
                Subtitle = IntentRead.Content(intent, "subtitle", ""),
                Kind = IntentRead.Content(intent, "kind", "place"),
                DistanceM = Mathf.Max(0f, (float)IntentRead.Num(
                    intent.Content, "distance_m", 0d)),
                AnchorQuality = quality,
                DepthValid = IntentRead.Flag(intent.Content, "depth_valid"),
            };
            error = null;
            return true;
        }

        private static bool TryPosition(
            Dictionary<string, object> anchor,
            out Vector3 position)
        {
            position = default;
            if (
                anchor == null ||
                !anchor.TryGetValue("position", out object raw) ||
                raw == null)
                return false;
            Dictionary<string, object> fields =
                raw as Dictionary<string, object>;
            if (fields == null && raw is JObject obj)
                fields = obj.ToObject<Dictionary<string, object>>();
            if (fields == null) return false;
            float x = (float)IntentRead.Num(fields, "x", double.NaN);
            float y = (float)IntentRead.Num(fields, "y", double.NaN);
            float z = (float)IntentRead.Num(fields, "z", double.NaN);
            if (!Finite(x) || !Finite(y) || !Finite(z)) return false;
            position = new Vector3(x, y, z);
            return position.sqrMagnitude <= 10000f;
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static string JoinDetails(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first)) return second ?? string.Empty;
            if (string.IsNullOrWhiteSpace(second)) return first;
            return first + "  •  " + second;
        }

        private static string KindGlyph(string kind)
        {
            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "storefront": return "◆";
                case "sign": return "▰";
                case "object": return "◇";
                case "memory": return "◈";
                case "destination": return "◎";
                case "hazard": return "△";
                default: return "●";
            }
        }

        private static Color KindColor(string kind)
        {
            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "storefront": return new Color(1f, 0.35f, 0.83f, 1f);
                case "sign": return new Color(1f, 0.76f, 0.22f, 1f);
                case "object": return new Color(0.34f, 1f, 0.68f, 1f);
                case "memory": return new Color(0.68f, 0.47f, 1f, 1f);
                case "destination": return new Color(0.42f, 1f, 0.58f, 1f);
                case "hazard": return new Color(1f, 0.34f, 0.22f, 1f);
                default: return new Color(0.3f, 0.95f, 1f, 1f);
            }
        }
    }
}
