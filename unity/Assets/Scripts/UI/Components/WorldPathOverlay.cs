using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// World-locked holographic paths for three bounded modes:
    /// human trajectory hypotheses, RGB motion traces, and recreational throws.
    /// Geometry must already be calibrated; this renderer never estimates it.
    /// </summary>
    public sealed class WorldPathOverlay : UIComponentBase
    {
        public sealed class Path
        {
            public string Id;
            public float Probability;
            public float Quality;
            public readonly List<Vector3> Points = new List<Vector3>();
        }

        public sealed class PathSet
        {
            public string Mode;
            public string CalibrationId;
            public string Label;
            public float HorizonS;
            public float SpatialQuality;
            public readonly List<Path> Paths = new List<Path>();
        }

        private const int MaximumPaths = 18;
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private readonly List<LineRenderer> _ghosts = new List<LineRenderer>();
        private Material _material;
        private TextMeshPro _label;
        private PathSet _set;
        private bool _qualified;

        public override string ComponentKey => "world_path";
        public bool IsQualified => _qualified;

        protected override void OnConfigured()
        {
            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");
            if (shader != null) _material = new Material(shader);
            var labelGo = new GameObject("WorldPathLabel");
            labelGo.transform.SetParent(transform, false);
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 0.06f;
            _label.enableWordWrapping = false;
        }

        protected override void Bind(UIIntent intent)
        {
            _qualified = TryReadPaths(intent, out _set, out _);
            SetEnabled(_qualified);
            if (!_qualified) return;
            EnsureCapacity(_set.Paths.Count);
            Color accent = Accent(_set.Mode);
            for (int i = 0; i < _set.Paths.Count; i++)
            {
                Path path = _set.Paths[i];
                LineRenderer line = _lines[i];
                line.positionCount = path.Points.Count;
                line.SetPositions(path.Points.ToArray());
                line.widthMultiplier = Width(_set.Mode, path.Probability);
                line.enabled = true;
                ConfigureGradient(line, accent, path.Probability);
                DrawTerminal(_ghosts[i], path, _set.Mode, accent);
            }
            for (int i = _set.Paths.Count; i < _lines.Count; i++)
            {
                _lines[i].enabled = false;
                _ghosts[i].enabled = false;
            }
            Path primary = _set.Paths[0];
            Vector3 endpoint = primary.Points[primary.Points.Count - 1];
            _label.transform.position = endpoint + Vector3.up * 1.95f;
            _label.text =
                $"<color=#{ColorUtility.ToHtmlStringRGB(accent)}>{ModeTitle(_set.Mode)}</color>" +
                (string.IsNullOrWhiteSpace(_set.Label)
                    ? string.Empty
                    : "\n<size=68%>" + _set.Label + "</size>") +
                $"\n<size=58%>{_set.HorizonS:0.0} S • " +
                $"{Mathf.RoundToInt(primary.Probability * 100f)}%</size>";
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || !_qualified) return;
            Camera cam = Context != null ? Context.Camera : Camera.main;
            if (cam != null && _label != null)
            {
                Vector3 forward = _label.transform.position - cam.transform.position;
                if (forward.sqrMagnitude > 0.0001f)
                    _label.transform.rotation =
                        Quaternion.LookRotation(forward, Vector3.up);
            }
            ApplyAlpha(CurrentAlpha);
        }

        protected override void ApplyVisual() => ApplyAlpha(CurrentAlpha);

        private void EnsureCapacity(int count)
        {
            while (_lines.Count < count)
            {
                _lines.Add(MakeLine("FuturePath", false, 2));
                _ghosts.Add(MakeLine("FutureGhost", true, 18));
            }
        }

        private LineRenderer MakeLine(string name, bool loop, int positions)
        {
            var go = new GameObject(name + _lines.Count);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = positions;
            line.alignment = LineAlignment.View;
            line.numCornerVertices = 4;
            line.numCapVertices = 3;
            if (_material != null) line.material = _material;
            return line;
        }

        private void DrawTerminal(
            LineRenderer ghost,
            Path path,
            string mode,
            Color accent)
        {
            Vector3 end = path.Points[path.Points.Count - 1];
            bool human = string.Equals(
                mode, "trajectory_forecast", StringComparison.Ordinal);
            ghost.positionCount = human ? 18 : 28;
            ghost.loop = true;
            ghost.widthMultiplier = human ? 0.026f : 0.018f;
            if (human)
            {
                // A vertical capsule reads as a translucent future silhouette,
                // while remaining abstract/probabilistic rather than a fake body.
                Camera cam = Context != null ? Context.Camera : Camera.main;
                Vector3 right = cam != null ? cam.transform.right : Vector3.right;
                Vector3 up = Vector3.up;
                for (int i = 0; i < ghost.positionCount; i++)
                {
                    float a = i / (float)ghost.positionCount * Mathf.PI * 2f;
                    float radius = a < Mathf.PI ? 0.24f : 0.34f;
                    float height = 0.85f + Mathf.Sin(a) * 0.85f;
                    ghost.SetPosition(
                        i,
                        end + right * Mathf.Cos(a) * radius + up * height);
                }
            }
            else
            {
                for (int i = 0; i < ghost.positionCount; i++)
                {
                    float a = i / (float)ghost.positionCount * Mathf.PI * 2f;
                    ghost.SetPosition(
                        i,
                        end + new Vector3(
                            Mathf.Cos(a) * 0.28f,
                            0.025f,
                            Mathf.Sin(a) * 0.28f));
                }
            }
            Color terminal = accent;
            terminal.a = Mathf.Lerp(0.35f, 0.9f, path.Probability);
            ghost.startColor = terminal;
            ghost.endColor = terminal;
            ghost.enabled = true;
        }

        private void ApplyAlpha(float alpha)
        {
            float a = Mathf.Clamp01(alpha);
            foreach (LineRenderer line in _lines)
                if (line != null && line.enabled)
                {
                    Color start = line.startColor;
                    Color end = line.endColor;
                    start.a = a * 0.25f;
                    end.a = a * 0.9f;
                    line.startColor = start;
                    line.endColor = end;
                }
            foreach (LineRenderer ghost in _ghosts)
                if (ghost != null && ghost.enabled)
                {
                    Color color = ghost.startColor;
                    color.a = a * 0.62f;
                    ghost.startColor = color;
                    ghost.endColor = color;
                }
            if (_label != null)
            {
                Color color = Color.white;
                color.a = a;
                _label.color = color;
            }
        }

        private void SetEnabled(bool enabled)
        {
            foreach (LineRenderer line in _lines) line.enabled = enabled;
            foreach (LineRenderer ghost in _ghosts) ghost.enabled = enabled;
            if (_label != null) _label.enabled = enabled;
        }

        private static void ConfigureGradient(
            LineRenderer line,
            Color accent,
            float probability)
        {
            var gradient = new Gradient();
            Color faint = accent * 0.65f;
            faint.a = 1f;
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(faint, 0f),
                    new GradientColorKey(accent, 0.65f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.12f, 0f),
                    new GradientAlphaKey(
                        Mathf.Lerp(0.32f, 0.92f, probability), 1f),
                });
            line.colorGradient = gradient;
        }

        private static float Width(string mode, float probability)
        {
            float basis = string.Equals(
                mode, "ballistic_preview", StringComparison.Ordinal)
                ? 0.026f
                : 0.045f;
            return basis * Mathf.Lerp(0.65f, 1.25f, probability);
        }

        private static Color Accent(string mode)
        {
            switch (mode)
            {
                case "trajectory_forecast":
                    return new Color(0.21f, 0.93f, 1f, 1f);
                case "event_motion":
                    return new Color(1f, 0.28f, 0.78f, 1f);
                default:
                    return new Color(0.4f, 1f, 0.45f, 1f);
            }
        }

        private static string ModeTitle(string mode)
        {
            switch (mode)
            {
                case "trajectory_forecast": return "FUTURS POSSIBLES";
                case "event_motion": return "TRACE DE MOUVEMENT";
                default: return "TRAJECTOIRE LUDIQUE";
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public static bool TryReadPaths(
            UIIntent intent,
            out PathSet set,
            out string error)
        {
            set = null;
            string mode = IntentRead.Content(intent, "mode", "").Trim();
            float threshold;
            bool requiresDepth;
            switch (mode)
            {
                case "trajectory_forecast":
                    threshold = 0.65f;
                    requiresDepth = true;
                    break;
                case "event_motion":
                    threshold = 0.70f;
                    requiresDepth = true;
                    if (!IntentRead.Flag(intent?.Content, "rgb_motion_valid") ||
                        !IntentRead.Flag(
                            intent?.Content, "head_motion_compensated"))
                    {
                        error = "motion_compensation_required";
                        return false;
                    }
                    break;
                case "ballistic_preview":
                    threshold = 0.75f;
                    requiresDepth = true;
                    if (
                        !IntentRead.Flag(intent?.Content, "hand_pose_valid") ||
                        !string.Equals(
                            IntentRead.Content(intent, "safety_class", ""),
                            "recreational",
                            StringComparison.Ordinal) ||
                        !AllowedTarget(IntentRead.Content(
                            intent, "target_kind", "")) ||
                        IntentRead.Flag(intent?.Content, "weapon"))
                    {
                        error = "unsafe_ballistic_contract";
                        return false;
                    }
                    break;
                default:
                    error = "unsupported_world_path_mode";
                    return false;
            }
            if (!WorldContractRead.TrackingGate(
                    intent,
                    requiresDepth,
                    threshold,
                    out float quality,
                    out error))
                return false;
            float horizon = (float)IntentRead.Num(
                intent.Content, "horizon_s", double.NaN);
            float maximumHorizon = mode == "trajectory_forecast" ? 5f : 4f;
            if (
                !WorldContractRead.Finite(horizon) ||
                horizon < 0.1f ||
                horizon > maximumHorizon)
            {
                error = "invalid_horizon";
                return false;
            }
            if (
                !intent.Content.TryGetValue("paths", out object raw) ||
                !WorldContractRead.TryArray(raw, out JArray array) ||
                array.Count < 1 ||
                array.Count > MaximumPaths)
            {
                error = "invalid_path_cardinality";
                return false;
            }
            var decoded = new PathSet
            {
                Mode = mode,
                CalibrationId = IntentRead.Content(
                    intent, "calibration_id", ""),
                Label = IntentRead.Content(intent, "label", ""),
                HorizonS = horizon,
                SpatialQuality = quality,
            };
            foreach (JToken token in array)
            {
                if (!(token is JObject obj))
                {
                    error = "path_not_object";
                    return false;
                }
                string id = obj.Value<string>("path_id")?.Trim();
                float probability = obj.Value<float?>("probability") ??
                    float.NaN;
                float pathQuality = obj.Value<float?>("quality") ??
                    float.NaN;
                if (
                    string.IsNullOrWhiteSpace(id) ||
                    !WorldContractRead.Finite(probability) ||
                    probability < 0f ||
                    probability > 1f ||
                    !WorldContractRead.Finite(pathQuality) ||
                    pathQuality < threshold ||
                    !WorldContractRead.TryVectorList(
                        obj["points"], 2, 64, out List<Vector3> points))
                {
                    error = "invalid_path";
                    return false;
                }
                var path = new Path
                {
                    Id = id,
                    Probability = probability,
                    Quality = pathQuality,
                };
                path.Points.AddRange(points);
                decoded.Paths.Add(path);
            }
            decoded.Paths.Sort(
                (a, b) => b.Probability.CompareTo(a.Probability));
            set = decoded;
            error = null;
            return true;
        }

        private static bool AllowedTarget(string value) =>
            string.Equals(value, "play_target", StringComparison.Ordinal) ||
            string.Equals(value, "inanimate", StringComparison.Ordinal);
    }
}
