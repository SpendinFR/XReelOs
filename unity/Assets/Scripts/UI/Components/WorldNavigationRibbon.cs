using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// FreeGuy street navigation rendered in the active XR tracking space.
    ///
    /// The component never converts GPS itself. A spatial provider must supply a
    /// calibrated, non-overlapping polyline in Unity tracking-local metres. If
    /// pose, calibration or route quality is missing, the world geometry stays
    /// hidden instead of drawing a persuasive but wrong arrow.
    /// </summary>
    public sealed class WorldNavigationRibbon : UIComponentBase
    {
        public sealed class Route
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public string RouteId;
            public string CalibrationId;
            public string Destination;
            public string Eta;
            public float DistanceM;
            public float MapQuality;
            public float RouteQuality;
            public bool DepthValid;
        }

        private const int MaxPoints = 128;
        private const int GroundChevronCount = 14;
        private const int FloatingArrowCount = 9;
        private const float GroundLift = 0.035f;

        [SerializeField] private float _ribbonWidth = 0.105f;
        [SerializeField] private float _beaconHeight = 2.25f;
        [SerializeField] private float _beaconRadius = 0.42f;
        [SerializeField] private float _chevronSize = 0.18f;
        [SerializeField] private float _floatingArrowHeight = 0.82f;
        [SerializeField] private float _floatingArrowSize = 0.34f;

        private LineRenderer _ribbon;
        private LineRenderer _destinationGroundRing;
        private LineRenderer _destinationPortal;
        private LineRenderer _destinationBeam;
        private LineRenderer[] _chevrons;
        private LineRenderer[] _floatingArrowLeft;
        private LineRenderer[] _floatingArrowRight;
        private TextMeshPro _destinationLabel;
        private Material _material;
        private readonly List<float> _cumulative = new List<float>();
        private Route _route;
        private bool _qualified;
        private Color _accent = new Color(0.24f, 0.95f, 1f, 1f);

        public override string ComponentKey => "world_navigation";
        public bool IsQualified => _qualified;
        public int RoutePointCount => _route?.Points.Count ?? 0;

        protected override void OnConfigured()
        {
            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Unlit/Color");
            if (shader != null) _material = new Material(shader);

            _ribbon = MakeLine("NeonRoute", _ribbonWidth, loop: false);
            _ribbon.numCornerVertices = 6;
            _ribbon.numCapVertices = 6;

            _destinationGroundRing = MakeLine("DestinationGroundRing", 0.028f, loop: true);
            _destinationGroundRing.positionCount = 48;
            _destinationPortal = MakeLine("DestinationPortal", 0.035f, loop: true);
            _destinationPortal.positionCount = 48;
            _destinationBeam = MakeLine("DestinationBeam", 0.018f, loop: false);
            _destinationBeam.positionCount = 2;

            _chevrons = new LineRenderer[GroundChevronCount];
            for (int i = 0; i < _chevrons.Length; i++)
            {
                _chevrons[i] = MakeLine("RouteChevron-" + i, 0.018f, loop: false);
                _chevrons[i].positionCount = 3;
            }
            _floatingArrowLeft = new LineRenderer[FloatingArrowCount];
            _floatingArrowRight = new LineRenderer[FloatingArrowCount];
            for (int i = 0; i < FloatingArrowCount; i++)
            {
                _floatingArrowLeft[i] =
                    MakeLine("WorldArrowLeft-" + i, 0.032f, loop: false);
                _floatingArrowRight[i] =
                    MakeLine("WorldArrowRight-" + i, 0.032f, loop: false);
                _floatingArrowLeft[i].positionCount = 2;
                _floatingArrowRight[i].positionCount = 2;
            }

            var labelGo = new GameObject("DestinationLabel");
            labelGo.transform.SetParent(transform, false);
            _destinationLabel = labelGo.AddComponent<TextMeshPro>();
            _destinationLabel.alignment = TextAlignmentOptions.Center;
            _destinationLabel.fontSize = 0.055f;
            _destinationLabel.fontStyle = FontStyles.Bold;
            _destinationLabel.enableWordWrapping = false;
            _destinationLabel.color = Color.white;
        }

        protected override void Bind(UIIntent intent)
        {
            _qualified = TryReadRoute(intent, out _route, out _);
            if (!_qualified)
            {
                SetGeometryEnabled(false);
                return;
            }
            BuildRouteGeometry();
            SetGeometryEnabled(true);
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            _accent = truth.Accent;
            ApplyColors(CurrentAlpha);
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || !_qualified) return;
            UpdateChevrons(Time.unscaledTime);
            UpdateDestination(Time.unscaledTime);
            ApplyColors(CurrentAlpha);
        }

        protected override void ApplyVisual()
        {
            // World points must never scale during the common glass-card intro.
            // Only their alpha pulses; changing transform scale would move the
            // calibrated path around its origin.
            ApplyColors(CurrentAlpha);
        }

        private LineRenderer MakeLine(string name, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = width;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;
            line.numCornerVertices = 3;
            if (_material != null) line.material = _material;
            return line;
        }

        private void BuildRouteGeometry()
        {
            _cumulative.Clear();
            _cumulative.Add(0f);
            float total = 0f;
            for (int i = 1; i < _route.Points.Count; i++)
            {
                total += Vector3.Distance(_route.Points[i - 1], _route.Points[i]);
                _cumulative.Add(total);
            }

            _ribbon.positionCount = _route.Points.Count;
            for (int i = 0; i < _route.Points.Count; i++)
                _ribbon.SetPosition(i, _route.Points[i] + Vector3.up * GroundLift);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.15f, 0.94f, 1f), 0f),
                    new GradientColorKey(new Color(0.35f, 0.55f, 1f), 0.55f),
                    new GradientColorKey(new Color(1f, 0.35f, 0.82f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.22f, 0f),
                    new GradientAlphaKey(0.92f, 0.08f),
                    new GradientAlphaKey(0.92f, 0.9f),
                    new GradientAlphaKey(0.35f, 1f),
                });
            _ribbon.colorGradient = gradient;

            string line1 = string.IsNullOrWhiteSpace(_route.Destination)
                ? "DESTINATION"
                : _route.Destination.ToUpperInvariant();
            string distance = _route.DistanceM >= 1000f
                ? (_route.DistanceM / 1000f).ToString("0.0") + " KM"
                : Mathf.RoundToInt(_route.DistanceM) + " M";
            _destinationLabel.text = line1 +
                $"\n<size=62%><color=#67F0C1>{distance}" +
                (string.IsNullOrWhiteSpace(_route.Eta) ? "" : "  •  " + _route.Eta) +
                "</color></size>";
        }

        private void UpdateDestination(float now)
        {
            Vector3 end = _route.Points[_route.Points.Count - 1];
            Vector3 approach = (
                end - _route.Points[Mathf.Max(0, _route.Points.Count - 2)]
            ).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, approach).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            float pulse = 1f + Mathf.Sin(now * 3.2f) * 0.08f;
            float radius = _beaconRadius * pulse;
            for (int i = 0; i < _destinationGroundRing.positionCount; i++)
            {
                float angle = i / (float)_destinationGroundRing.positionCount *
                    Mathf.PI * 2f;
                _destinationGroundRing.SetPosition(
                    i,
                    end + Vector3.up * GroundLift +
                    new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            float portalRadius = radius * 1.1f;
            Vector3 portalCenter = end + Vector3.up * (_beaconHeight * 0.52f);
            for (int i = 0; i < _destinationPortal.positionCount; i++)
            {
                float angle = i / (float)_destinationPortal.positionCount *
                    Mathf.PI * 2f;
                _destinationPortal.SetPosition(
                    i,
                    portalCenter +
                    side * Mathf.Cos(angle) * portalRadius +
                    Vector3.up * Mathf.Sin(angle) * portalRadius * 1.55f);
            }
            _destinationBeam.SetPosition(0, end + Vector3.up * GroundLift);
            _destinationBeam.SetPosition(1, end + Vector3.up * _beaconHeight);

            _destinationLabel.transform.position =
                end + Vector3.up * (_beaconHeight + 0.18f);
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam != null)
            {
                Vector3 forward = _destinationLabel.transform.position - cam.transform.position;
                if (forward.sqrMagnitude > 0.0001f)
                    _destinationLabel.transform.rotation =
                        Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        private void UpdateChevrons(float now)
        {
            float total = _cumulative.Count > 0 ? _cumulative[_cumulative.Count - 1] : 0f;
            if (total <= 0.01f) return;
            for (int i = 0; i < _chevrons.Length; i++)
            {
                float unit = Mathf.Repeat(
                    i / (float)_chevrons.Length + now * 0.075f,
                    1f);
                SampleAt(unit * total, out Vector3 point, out Vector3 tangent);
                Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                Vector3 tip = point + Vector3.up * (GroundLift + 0.018f) +
                    tangent * _chevronSize * 0.55f;
                Vector3 tail = point + Vector3.up * (GroundLift + 0.018f) -
                    tangent * _chevronSize * 0.35f;
                _chevrons[i].SetPosition(0, tail + side * _chevronSize * 0.42f);
                _chevrons[i].SetPosition(1, tip);
                _chevrons[i].SetPosition(2, tail - side * _chevronSize * 0.42f);
            }

            // Large world-locked chevrons are the primary FreeGuy guidance.
            // They hover above the route and stream toward the destination. The
            // ground ribbon stays as a quiet cue for peripheral vision.
            for (int i = 0; i < FloatingArrowCount; i++)
            {
                float unit = Mathf.Repeat(
                    (i + 0.5f) / FloatingArrowCount + now * 0.045f,
                    1f);
                SampleAt(unit * total, out Vector3 point, out Vector3 tangent);
                Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                float bob = Mathf.Sin(now * 2.2f + i * 0.7f) * 0.055f;
                Vector3 center = point +
                    Vector3.up * (_floatingArrowHeight + bob);
                Vector3 tip = center + tangent * _floatingArrowSize * 0.72f;
                Vector3 tail = center - tangent * _floatingArrowSize * 0.35f;
                _floatingArrowLeft[i].SetPosition(
                    0, tail + side * _floatingArrowSize * 0.58f);
                _floatingArrowLeft[i].SetPosition(1, tip);
                _floatingArrowRight[i].SetPosition(0, tip);
                _floatingArrowRight[i].SetPosition(
                    1, tail - side * _floatingArrowSize * 0.58f);
            }
        }

        private void SampleAt(float distance, out Vector3 point, out Vector3 tangent)
        {
            int segment = 1;
            while (segment < _cumulative.Count && _cumulative[segment] < distance)
                segment++;
            segment = Mathf.Clamp(segment, 1, _route.Points.Count - 1);
            float start = _cumulative[segment - 1];
            float length = Mathf.Max(0.0001f, _cumulative[segment] - start);
            float t = Mathf.Clamp01((distance - start) / length);
            Vector3 a = _route.Points[segment - 1];
            Vector3 b = _route.Points[segment];
            point = Vector3.Lerp(a, b, t);
            tangent = (b - a).normalized;
        }

        private void ApplyColors(float alpha)
        {
            Color color = _accent;
            color.a = Mathf.Clamp01(alpha);
            if (_ribbon != null)
            {
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.15f, 0.94f, 1f), 0f),
                        new GradientColorKey(new Color(0.35f, 0.55f, 1f), 0.55f),
                        new GradientColorKey(new Color(1f, 0.35f, 0.82f), 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(alpha * 0.16f, 0f),
                        new GradientAlphaKey(alpha * 0.72f, 0.08f),
                        new GradientAlphaKey(alpha * 0.72f, 0.9f),
                        new GradientAlphaKey(alpha * 0.24f, 1f),
                    });
                _ribbon.colorGradient = gradient;
            }
            foreach (LineRenderer line in AllLines())
            {
                if (line == null || ReferenceEquals(line, _ribbon)) continue;
                line.startColor = color;
                line.endColor = color;
            }
            if (_destinationLabel != null)
            {
                Color label = Color.white;
                label.a = color.a;
                _destinationLabel.color = label;
            }
        }

        private IEnumerable<LineRenderer> AllLines()
        {
            yield return _ribbon;
            yield return _destinationGroundRing;
            yield return _destinationPortal;
            yield return _destinationBeam;
            if (_chevrons != null)
                foreach (LineRenderer line in _chevrons) yield return line;
            if (_floatingArrowLeft != null)
                foreach (LineRenderer line in _floatingArrowLeft) yield return line;
            if (_floatingArrowRight != null)
                foreach (LineRenderer line in _floatingArrowRight) yield return line;
        }

        private void SetGeometryEnabled(bool enabled)
        {
            foreach (LineRenderer line in AllLines())
                if (line != null) line.enabled = enabled;
            if (_destinationLabel != null) _destinationLabel.enabled = enabled;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public static bool TryReadRoute(
            UIIntent intent,
            out Route route,
            out string error)
        {
            route = null;
            error = "invalid_route";
            if (intent?.Content == null || intent.Anchor == null)
                return false;
            if (!string.Equals(
                    IntentRead.Anchor(intent, "coordinate_space"),
                    "tracking_local",
                    StringComparison.Ordinal))
            {
                error = "unsupported_coordinate_space";
                return false;
            }
            if (!IntentRead.Flag(intent.Content, "pose_valid") ||
                string.IsNullOrWhiteSpace(IntentRead.Content(intent, "calibration_id")))
            {
                error = "unproven_tracking_calibration";
                return false;
            }
            if (intent.EvidenceRefs == null || intent.EvidenceRefs.Count == 0)
            {
                error = "route_provenance_missing";
                return false;
            }
            float mapQuality = (float)IntentRead.Num(
                intent.Content, "map_quality", double.NaN);
            float routeQuality = (float)IntentRead.Num(
                intent.Content, "route_quality", double.NaN);
            if (
                float.IsNaN(mapQuality) || mapQuality < 0.7f ||
                float.IsNaN(routeQuality) || routeQuality < 0.7f)
            {
                error = "quality_below_threshold";
                return false;
            }
            if (!intent.Content.TryGetValue("route_points", out object raw) || raw == null)
            {
                error = "route_points_missing";
                return false;
            }
            JArray array;
            try { array = raw as JArray ?? JArray.FromObject(raw); }
            catch
            {
                error = "route_points_invalid";
                return false;
            }
            if (array.Count < 2 || array.Count > MaxPoints)
            {
                error = "route_points_cardinality";
                return false;
            }
            var decoded = new Route
            {
                RouteId = IntentRead.Content(intent, "route_id", ""),
                CalibrationId = IntentRead.Content(intent, "calibration_id", ""),
                Destination = IntentRead.Content(intent, "destination", ""),
                Eta = IntentRead.Content(intent, "eta", ""),
                DistanceM = Mathf.Max(0f, (float)IntentRead.Num(
                    intent.Content, "distance_m", 0d)),
                MapQuality = mapQuality,
                RouteQuality = routeQuality,
                DepthValid = IntentRead.Flag(intent.Content, "depth_valid"),
            };
            foreach (JToken token in array)
            {
                if (!(token is JObject point))
                {
                    error = "route_point_not_object";
                    return false;
                }
                float x = point.Value<float?>("x") ?? float.NaN;
                float y = point.Value<float?>("y") ?? float.NaN;
                float z = point.Value<float?>("z") ?? float.NaN;
                if (
                    float.IsNaN(x) || float.IsInfinity(x) ||
                    float.IsNaN(y) || float.IsInfinity(y) ||
                    float.IsNaN(z) || float.IsInfinity(z))
                {
                    error = "route_point_non_finite";
                    return false;
                }
                var vector = new Vector3(x, y, z);
                if (
                    decoded.Points.Count > 0 &&
                    Vector3.Distance(decoded.Points[decoded.Points.Count - 1], vector) > 50f)
                {
                    error = "route_segment_too_long";
                    return false;
                }
                decoded.Points.Add(vector);
            }
            if (string.IsNullOrWhiteSpace(decoded.RouteId))
            {
                error = "route_id_missing";
                return false;
            }
            route = decoded;
            error = null;
            return true;
        }
    }
}
