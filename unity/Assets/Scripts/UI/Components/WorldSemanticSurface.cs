using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Additive FreeGuy tint/outline over one proven convex semantic surface.
    /// Pixels from transformed/synthetic surfaces never return to perception.
    /// </summary>
    public sealed class WorldSemanticSurface : UIComponentBase
    {
        public sealed class Surface
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public string SurfaceId;
            public string CalibrationId;
            public string Kind;
            public string Label;
            public float Quality;
        }

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private LineRenderer _outline;
        private TextMeshPro _label;
        private Material _fillMaterial;
        private Material _lineMaterial;
        private Surface _surface;
        private Color _surfaceColor = new Color(0.2f, 0.9f, 1f, 1f);
        private bool _qualified;

        public override string ComponentKey => "world_surface";
        public bool IsQualified => _qualified;

        protected override void OnConfigured()
        {
            var filter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _mesh = new Mesh { name = "WorldSemanticSurfaceMesh" };
            filter.sharedMesh = _mesh;

            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _fillMaterial = TransparentMaterial(shader);
                _lineMaterial = TransparentMaterial(shader);
                _renderer.sharedMaterial = _fillMaterial;
            }

            var outlineGo = new GameObject("SemanticSurfaceOutline");
            outlineGo.transform.SetParent(transform, false);
            _outline = outlineGo.AddComponent<LineRenderer>();
            _outline.useWorldSpace = true;
            _outline.loop = true;
            _outline.widthMultiplier = 0.022f;
            _outline.numCornerVertices = 4;
            if (_lineMaterial != null) _outline.material = _lineMaterial;

            var labelGo = new GameObject("SemanticSurfaceLabel");
            labelGo.transform.SetParent(transform, false);
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 0.075f;
            _label.enableWordWrapping = false;
        }

        protected override void Bind(UIIntent intent)
        {
            _qualified = TryReadSurface(intent, out _surface, out _);
            if (!_qualified)
            {
                SetEnabled(false);
                return;
            }
            _surfaceColor = SurfaceColor(_surface.Kind);
            _label.text = string.IsNullOrWhiteSpace(_surface.Label)
                ? string.Empty
                : $"<color=#{ColorUtility.ToHtmlStringRGB(_surfaceColor)}>" +
                  _surface.Label.ToUpperInvariant() +
                  "</color>";
            BuildGeometry();
            SetEnabled(true);
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            // Surface kind drives hue; truth drives admission and the textual
            // truth badge upstream. Inferred geometry never clears TryReadSurface.
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || !_qualified) return;
            float pulse = 0.82f + Mathf.Sin(Time.unscaledTime * 1.8f) * 0.18f;
            ApplyColors(CurrentAlpha, pulse);
            BillboardLabel();
        }

        protected override void ApplyVisual()
        {
            ApplyColors(CurrentAlpha, 1f);
        }

        private void BuildGeometry()
        {
            int n = _surface.Points.Count;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < n; i++) center += _surface.Points[i];
            center /= n;
            transform.position = center;

            // Mesh vertices are local to the surface centroid so the shared UI
            // appear/fade scale never drags world geometry toward the XR origin.
            var vertices = new Vector3[n];
            for (int i = 0; i < n; i++)
                vertices[i] = _surface.Points[i] - center;
            var triangles = new int[(n - 2) * 3];
            for (int i = 0; i < n - 2; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            _mesh.Clear();
            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
            _mesh.RecalculateNormals();

            _outline.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                _outline.SetPosition(i, _surface.Points[i]);
            }
            _label.transform.position = center + Vector3.up * 0.12f;
        }

        private void ApplyColors(float alpha, float pulse)
        {
            float a = Mathf.Clamp01(alpha);
            Color fill = _surfaceColor;
            fill.a = a * 0.16f;
            Color edge = _surfaceColor;
            edge.a = a * Mathf.Lerp(0.65f, 0.95f, pulse);
            SetMaterialColor(_fillMaterial, fill);
            SetMaterialColor(_lineMaterial, edge);
            if (_outline != null)
            {
                _outline.startColor = edge;
                _outline.endColor = edge;
            }
            if (_label != null)
            {
                Color text = Color.white;
                text.a = a;
                _label.color = text;
            }
        }

        private void BillboardLabel()
        {
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam == null || _label == null || string.IsNullOrEmpty(_label.text))
                return;
            Vector3 forward = _label.transform.position - cam.transform.position;
            if (forward.sqrMagnitude > 0.0001f)
                _label.transform.rotation =
                    Quaternion.LookRotation(forward, Vector3.up);
        }

        private void SetEnabled(bool enabled)
        {
            if (_renderer != null) _renderer.enabled = enabled;
            if (_outline != null) _outline.enabled = enabled;
            if (_label != null) _label.enabled = enabled;
        }

        private static Material TransparentMaterial(Shader shader)
        {
            var material = new Material(shader);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_fillMaterial != null) Destroy(_fillMaterial);
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }

        public static bool TryReadSurface(
            UIIntent intent,
            out Surface surface,
            out string error)
        {
            surface = null;
            error = "invalid_surface";
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
            if (
                !IntentRead.Flag(intent.Content, "pose_valid") ||
                !IntentRead.Flag(intent.Content, "depth_valid") ||
                !IntentRead.Flag(intent.Content, "convex") ||
                string.IsNullOrWhiteSpace(IntentRead.Content(
                    intent, "calibration_id")))
            {
                error = "unproven_surface_geometry";
                return false;
            }
            float quality = (float)IntentRead.Num(
                intent.Content, "surface_quality", double.NaN);
            if (float.IsNaN(quality) || quality < 0.75f)
            {
                error = "surface_quality_below_threshold";
                return false;
            }
            if (intent.EvidenceRefs == null || intent.EvidenceRefs.Count == 0)
            {
                error = "surface_evidence_missing";
                return false;
            }
            string id = IntentRead.Content(intent, "surface_id", "").Trim();
            string kind = IntentRead.Content(intent, "surface_kind", "").Trim();
            if (string.IsNullOrEmpty(id) || !AllowedKind(kind))
            {
                error = "surface_identity_invalid";
                return false;
            }
            if (
                !intent.Content.TryGetValue("surface_points", out object raw) ||
                raw == null)
            {
                error = "surface_points_missing";
                return false;
            }
            JArray array;
            try { array = raw as JArray ?? JArray.FromObject(raw); }
            catch
            {
                error = "surface_points_invalid";
                return false;
            }
            if (array.Count < 3 || array.Count > 64)
            {
                error = "surface_points_cardinality";
                return false;
            }
            var decoded = new Surface
            {
                SurfaceId = id,
                CalibrationId = IntentRead.Content(intent, "calibration_id", ""),
                Kind = kind,
                Label = IntentRead.Content(intent, "label", ""),
                Quality = quality,
            };
            foreach (JToken token in array)
            {
                if (!(token is JObject point))
                {
                    error = "surface_point_not_object";
                    return false;
                }
                float x = point.Value<float?>("x") ?? float.NaN;
                float y = point.Value<float?>("y") ?? float.NaN;
                float z = point.Value<float?>("z") ?? float.NaN;
                if (!Finite(x) || !Finite(y) || !Finite(z))
                {
                    error = "surface_point_non_finite";
                    return false;
                }
                Vector3 value = new Vector3(x, y, z);
                if (value.sqrMagnitude > 10000f)
                {
                    error = "surface_point_out_of_range";
                    return false;
                }
                decoded.Points.Add(value);
            }
            if (!HasUsableSurface(decoded.Points))
            {
                error = "surface_geometry_degenerate";
                return false;
            }
            surface = decoded;
            error = null;
            return true;
        }

        private static bool AllowedKind(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "building":
                case "storefront":
                case "sign":
                case "road":
                case "sidewalk":
                case "tree":
                case "vehicle":
                case "wall":
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasUsableSurface(List<Vector3> points)
        {
            if (points == null || points.Count < 3) return false;
            Vector3 origin = points[0];
            Vector3 normal = Vector3.zero;
            for (int i = 1; i < points.Count - 1; i++)
            {
                normal = Vector3.Cross(
                    points[i] - origin,
                    points[i + 1] - origin);
                if (normal.sqrMagnitude > 0.0004f) break;
            }
            if (normal.sqrMagnitude <= 0.0004f) return false;
            normal.Normalize();

            // A surface overlay must be a near-planar polygon. A loose 5 cm
            // tolerance accommodates Depth noise without accepting arbitrary 3D
            // point clouds as a facade.
            foreach (Vector3 point in points)
                if (Mathf.Abs(Vector3.Dot(point - origin, normal)) > 0.05f)
                    return false;

            // Convexity is recomputed rather than trusting the provider flag.
            float sign = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[(i + 1) % points.Count] - points[i];
                Vector3 b = points[(i + 2) % points.Count] -
                    points[(i + 1) % points.Count];
                float turn = Vector3.Dot(Vector3.Cross(a, b), normal);
                if (Mathf.Abs(turn) <= 0.0001f) continue;
                float current = Mathf.Sign(turn);
                if (sign == 0f) sign = current;
                else if (current != sign) return false;
            }
            return sign != 0f;
        }

        private static Color SurfaceColor(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "building": return new Color(0.23f, 0.67f, 1f, 1f);
                case "storefront": return new Color(1f, 0.28f, 0.78f, 1f);
                case "sign": return new Color(1f, 0.72f, 0.18f, 1f);
                case "road": return new Color(0.1f, 0.88f, 1f, 1f);
                case "sidewalk": return new Color(0.28f, 1f, 0.65f, 1f);
                case "tree": return new Color(0.23f, 1f, 0.38f, 1f);
                case "vehicle": return new Color(1f, 0.46f, 0.16f, 1f);
                default: return new Color(0.54f, 0.42f, 1f, 1f);
            }
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
