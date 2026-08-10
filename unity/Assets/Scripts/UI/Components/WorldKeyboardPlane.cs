using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using TMPro;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Calibrated tabletop holographic keyboard. It renders and hit-tests in
    /// tracking-local metres; a real hand provider must call TryPressWorld.
    /// </summary>
    public sealed class WorldKeyboardPlane : UIComponentBase
    {
        public sealed class Keyboard
        {
            public Vector3 Origin;
            public Vector3 Right;
            public Vector3 Forward;
            public float Width;
            public float Height;
            public float Quality;
            public string CalibrationId;
        }

        private sealed class KeyCell
        {
            public string Value;
            public Vector2 Center;
            public Vector2 Size;
            public TextMeshPro Label;
        }

        private static readonly string[] Rows =
        {
            "AZERTYUIOP",
            "QSDFGHJKLM",
            "WXCVBN",
        };

        private readonly List<LineRenderer> _grid = new List<LineRenderer>();
        private readonly List<KeyCell> _keys = new List<KeyCell>();
        private Material _material;
        private TextMeshPro _header;
        private Keyboard _keyboard;
        private bool _qualified;
        private float _lastPressAt = -10f;

        public override string ComponentKey => "world_keyboard";
        public event Action<string> KeyPressed;

        protected override void OnConfigured()
        {
            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");
            if (shader != null) _material = new Material(shader);
            var headerGo = new GameObject("KeyboardHeader");
            headerGo.transform.SetParent(transform, false);
            _header = headerGo.AddComponent<TextMeshPro>();
            _header.fontStyle = FontStyles.Bold;
            _header.alignment = TextAlignmentOptions.Center;
            _header.fontSize = 0.04f;
            _header.enableWordWrapping = false;
            _header.text = "<color=#70F8FF>CLAVIER SPATIAL</color>";
        }

        protected override void Bind(UIIntent intent)
        {
            _qualified = TryReadKeyboard(intent, out _keyboard, out _);
            SetEnabled(_qualified);
            if (!_qualified) return;
            BuildGrid();
            ApplyAlpha(CurrentAlpha);
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || !_qualified) return;
            ApplyAlpha(CurrentAlpha);
        }

        protected override void ApplyVisual() => ApplyAlpha(CurrentAlpha);

        private void BuildGrid()
        {
            foreach (LineRenderer line in _grid)
                if (line != null) Destroy(line.gameObject);
            foreach (KeyCell key in _keys)
                if (key.Label != null) Destroy(key.Label.gameObject);
            _grid.Clear();
            _keys.Clear();

            Vector3 normal = Vector3.Cross(
                _keyboard.Forward, _keyboard.Right).normalized;
            Vector3 lift = normal * 0.008f;
            float rowHeight = _keyboard.Height / Rows.Length;
            for (int row = 0; row < Rows.Length; row++)
            {
                string values = Rows[row];
                float rowWidth = _keyboard.Width * (row == 2 ? 0.64f : 0.94f);
                float keyWidth = rowWidth / values.Length;
                float offsetX = (_keyboard.Width - rowWidth) * 0.5f;
                for (int column = 0; column < values.Length; column++)
                {
                    float x0 = offsetX + column * keyWidth;
                    float x1 = x0 + keyWidth * 0.92f;
                    float z0 = row * rowHeight;
                    float z1 = z0 + rowHeight * 0.82f;
                    Vector3 p0 = Point(x0, z0) + lift;
                    Vector3 p1 = Point(x0, z1) + lift;
                    Vector3 p2 = Point(x1, z1) + lift;
                    Vector3 p3 = Point(x1, z0) + lift;
                    LineRenderer cell = MakeLine(
                        "Key_" + values[column], true, 4, 0.006f);
                    cell.SetPosition(0, p0);
                    cell.SetPosition(1, p1);
                    cell.SetPosition(2, p2);
                    cell.SetPosition(3, p3);
                    _grid.Add(cell);

                    var labelGo = new GameObject("Label_" + values[column]);
                    labelGo.transform.SetParent(transform, false);
                    var label = labelGo.AddComponent<TextMeshPro>();
                    label.alignment = TextAlignmentOptions.Center;
                    label.fontStyle = FontStyles.Bold;
                    label.fontSize = Mathf.Max(0.018f, keyWidth * 0.24f);
                    label.text = values[column].ToString();
                    label.transform.position =
                        (p0 + p1 + p2 + p3) * 0.25f + normal * 0.004f;
                    label.transform.rotation = Quaternion.LookRotation(normal, _keyboard.Forward);
                    _keys.Add(new KeyCell
                    {
                        Value = values[column].ToString(),
                        Center = new Vector2(
                            (x0 + x1) * 0.5f,
                            (z0 + z1) * 0.5f),
                        Size = new Vector2(x1 - x0, z1 - z0),
                        Label = label,
                    });
                }
            }
            _header.transform.position =
                Point(_keyboard.Width * 0.5f, _keyboard.Height) +
                normal * 0.025f;
            _header.transform.rotation =
                Quaternion.LookRotation(normal, _keyboard.Forward);
        }

        public bool TryPressWorld(
            Vector3 fingertip,
            bool contactConfirmed,
            out string key)
        {
            key = null;
            if (
                !_qualified ||
                !contactConfirmed ||
                Time.unscaledTime - _lastPressAt < 0.12f)
                return false;
            Vector3 offset = fingertip - _keyboard.Origin;
            Vector3 normal = Vector3.Cross(
                _keyboard.Forward, _keyboard.Right).normalized;
            float distanceToPlane = Mathf.Abs(Vector3.Dot(offset, normal));
            if (distanceToPlane > 0.025f) return false;
            Vector2 plane = new Vector2(
                Vector3.Dot(offset, _keyboard.Right),
                Vector3.Dot(offset, _keyboard.Forward));
            foreach (KeyCell cell in _keys)
            {
                if (
                    Mathf.Abs(plane.x - cell.Center.x) <= cell.Size.x * 0.5f &&
                    Mathf.Abs(plane.y - cell.Center.y) <= cell.Size.y * 0.5f)
                {
                    _lastPressAt = Time.unscaledTime;
                    key = cell.Value;
                    KeyPressed?.Invoke(key);
                    RaiseActed(new Dictionary<string, object>
                    {
                        { "kind", "spatial_key" },
                        { "key", key },
                    });
                    return true;
                }
            }
            return false;
        }

        private Vector3 Point(float x, float z) =>
            _keyboard.Origin +
            _keyboard.Right * x +
            _keyboard.Forward * z;

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
            line.numCornerVertices = 3;
            if (_material != null) line.material = _material;
            return line;
        }

        private void ApplyAlpha(float alpha)
        {
            Color edge = new Color(0.22f, 0.94f, 1f, CurrentAlpha * 0.82f);
            foreach (LineRenderer line in _grid)
            {
                line.startColor = edge;
                line.endColor = edge;
            }
            Color text = Color.white;
            text.a = Mathf.Clamp01(alpha);
            foreach (KeyCell key in _keys)
                key.Label.color = text;
            if (_header != null) _header.color = text;
        }

        private void SetEnabled(bool enabled)
        {
            foreach (LineRenderer line in _grid) line.enabled = enabled;
            foreach (KeyCell key in _keys) key.Label.enabled = enabled;
            if (_header != null) _header.enabled = enabled;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public static bool TryReadKeyboard(
            UIIntent intent,
            out Keyboard keyboard,
            out string error)
        {
            keyboard = null;
            if (!WorldContractRead.TrackingGate(
                    intent, true, 0.80f, out float quality, out error))
                return false;
            if (
                !IntentRead.Flag(intent.Content, "explicit_activation") ||
                !IntentRead.Flag(intent.Content, "hand_tracking_valid"))
            {
                error = "keyboard_activation_or_hands_missing";
                return false;
            }
            if (
                !intent.Content.TryGetValue("origin", out object originRaw) ||
                !intent.Content.TryGetValue("right", out object rightRaw) ||
                !intent.Content.TryGetValue("forward", out object forwardRaw) ||
                !WorldContractRead.TryVector(originRaw, out Vector3 origin) ||
                !WorldContractRead.TryVector(rightRaw, out Vector3 right) ||
                !WorldContractRead.TryVector(forwardRaw, out Vector3 forward))
            {
                error = "keyboard_basis_invalid";
                return false;
            }
            right.Normalize();
            forward.Normalize();
            if (
                Mathf.Abs(Vector3.Dot(right, forward)) > 0.05f ||
                Vector3.Cross(right, forward).sqrMagnitude < 0.95f)
            {
                error = "keyboard_basis_not_orthogonal";
                return false;
            }
            float width = (float)IntentRead.Num(
                intent.Content, "width_m", double.NaN);
            float height = (float)IntentRead.Num(
                intent.Content, "height_m", double.NaN);
            if (
                !WorldContractRead.Finite(width) ||
                !WorldContractRead.Finite(height) ||
                width < 0.25f ||
                width > 1.2f ||
                height < 0.12f ||
                height > 0.6f)
            {
                error = "keyboard_size_invalid";
                return false;
            }
            keyboard = new Keyboard
            {
                Origin = origin,
                Right = right,
                Forward = forward,
                Width = width,
                Height = height,
                Quality = quality,
                CalibrationId = IntentRead.Content(
                    intent, "calibration_id", ""),
            };
            error = null;
            return true;
        }
    }
}
