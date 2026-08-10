using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MLOmega.Contracts.V19;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Bounded procedural FreeGuy decoration attached to geometry proven by the
    /// XREAL pose/depth provider. It deliberately uses no opaque screen-space quad:
    /// every primitive lives in tracking-local space and disappears with its intent.
    /// </summary>
    public sealed class WorldHologram : UIComponentBase
    {
        public sealed class Hologram
        {
            public Vector3 Position;
            public string Id;
            public string CalibrationId;
            public string TemplateId;
            public string ArchetypeId;
            public string AnimationId;
            public string Label;
            public string Subtitle;
            public float Quality;
            public bool DepthValid;
            public Color Accent;
            public Color Secondary;
            public Vector3 Scale;
            public Quaternion AnchorRotation;
            public string AssetMime;
            public string AssetSha256;
            public string AssetBase64;
            public string AssetFilePath;
            public string MotionPath;
            public float MotionRadiusM;
            public float MotionSpeed;
            public float MotionHeightM;
            public float MaxRenderDistanceM;
        }

        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private Material _lineMaterial;
        private Material _panelMaterial;
        private MeshRenderer _panel;
        private TextMeshPro _label;
        private Hologram _hologram;
        private bool _qualified;
        private Color _accent;
        private Color _secondary;
        private Texture2D _assetTexture;
        private GameObject _assetModel;
        private string _assetSha256 = string.Empty;
        private bool _assetIsModel;

        public override string ComponentKey => "world_hologram";
        public bool IsQualified => _qualified;
        public string TemplateId => _hologram?.TemplateId ?? string.Empty;

        protected override void OnConfigured()
        {
            Shader shader = Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _lineMaterial = TransparentMaterial(shader);
                _panelMaterial = TransparentMaterial(shader);
            }

            for (int i = 0; i < 5; i++)
                _lines.Add(MakeLine("HoloLine" + i, i == 0 ? 0.022f : 0.012f));

            var panelGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panelGo.name = "HoloPanel";
            panelGo.transform.SetParent(transform, false);
            Collider collider = panelGo.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _panel = panelGo.GetComponent<MeshRenderer>();
            if (_panelMaterial != null) _panel.sharedMaterial = _panelMaterial;

            var labelGo = new GameObject("HoloLabel");
            labelGo.transform.SetParent(transform, false);
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontStyle = FontStyles.Bold;
            _label.fontSize = 0.065f;
            _label.enableWordWrapping = false;
        }

        protected override void Bind(UIIntent intent)
        {
            _qualified = TryReadHologram(intent, out _hologram, out _);
            SetEnabled(_qualified);
            if (!_qualified) return;

            _accent = _hologram.Accent;
            _secondary = _hologram.Secondary;
            BindAsset();
            if (_assetModel != null)
            {
                _assetModel.transform.position = _hologram.Position;
                _assetModel.transform.rotation = _hologram.AnchorRotation;
                _assetModel.transform.localScale = _hologram.Scale;
            }
            _label.text =
                $"<color=#{ColorUtility.ToHtmlStringRGB(_accent)}>" +
                CleanLabel(_hologram.Label).ToUpperInvariant() +
                "</color>" +
                (string.IsNullOrWhiteSpace(_hologram.Subtitle)
                    ? string.Empty
                    : "\n<size=56%><color=#C8E8F2>" +
                      CleanLabel(_hologram.Subtitle) +
                      "</color></size>");
        }

        protected override void OnTruth(TruthDescriptor truth)
        {
            // Admission is fail-closed in TryReadHologram. Colour denotes the
            // visual template rather than attempting to hide truth state.
        }

        protected override void Update()
        {
            base.Update();
            if (Phase == UIComponentPhase.Idle || !_qualified) return;
            Draw(Time.unscaledTime);
        }

        protected override void ApplyVisual() =>
            ApplyColor(CurrentAlpha, 1f);

        private void Draw(float now)
        {
            now = AnimatedTime(now);
            Camera cam = Context != null ? Context.Camera : Camera.main;
            Vector3 origin = MotionPosition(now);
            if (cam != null)
            {
                float distance = Vector3.Distance(
                    cam.transform.position,
                    origin);
                // A LineRenderer whose segment reaches the stereo eye/near
                // plane expands into a screen-filling wedge. Apart from being
                // unreadable, the alternating cyan/violet preview then looks
                // like an opaque background on optical glasses. Fail closed
                // for any hologram too close to the user's eyes.
                float minimumSafeDistance = Mathf.Max(
                    0.55f,
                    cam.nearClipPlane + 0.25f);
                if (
                    distance < minimumSafeDistance ||
                    distance > _hologram.MaxRenderDistanceM)
                {
                    SetEnabled(false);
                    return;
                }
            }
            SetEnabled(true);
            Vector3 toCamera = cam == null
                ? Vector3.back
                : cam.transform.position - origin;
            Vector3 anchoredUp = _hologram.AnchorRotation * Vector3.up;
            Vector3 anchoredForward =
                _hologram.AnchorRotation * Vector3.forward;
            Vector3 flatForward =
                Vector3.ProjectOnPlane(anchoredForward, anchoredUp);
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.ProjectOnPlane(toCamera, anchoredUp);
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;
            flatForward.Normalize();
            Vector3 right =
                Vector3.Cross(anchoredUp, flatForward).normalized;
            float pulse = AnimatedPulse(now);
            if (_assetModel != null)
            {
                string animation = (_hologram.AnimationId ?? string.Empty)
                    .Trim().ToLowerInvariant();
                float hover = animation == "scan" || animation == "data_rain"
                    ? Mathf.Sin(now * 1.8f) * .025f
                    : 0f;
                float yaw = animation == "orbit"
                    ? Mathf.Repeat(now * 22f, 360f)
                    : 0f;
                float scalePulse = animation == "soft_pulse"
                    ? 1f + Mathf.Sin(now * 2.2f) * .025f
                    : 1f;
                _assetModel.transform.position =
                    origin + anchoredUp * hover;
                _assetModel.transform.rotation =
                    _hologram.AnchorRotation *
                    Quaternion.AngleAxis(yaw, Vector3.up);
                _assetModel.transform.localScale =
                    _hologram.Scale * scalePulse;
            }

            switch (_hologram.TemplateId)
            {
                case "holo_billboard":
                    DrawBillboard(origin, right, flatForward, now);
                    break;
                case "vehicle_fx":
                    DrawVehicleFx(origin, right, flatForward, now);
                    break;
                case "poi_beacon":
                    DrawBeacon(origin, right, flatForward, now);
                    break;
                case "memory_echo":
                    DrawMemoryEcho(origin, right, flatForward, now);
                    break;
                case "portal_arch":
                    DrawPortal(origin, right, flatForward, now);
                    break;
                case "sky_drone":
                    DrawDrone(origin, right, flatForward, now);
                    break;
                case "giant_hologram":
                    DrawGiantHologram(origin, right, flatForward, now);
                    break;
                case "direction_arrow":
                    DrawDirectionArrow(origin, right, flatForward, now);
                    break;
                case "building_crown":
                    DrawBuildingCrown(origin, right, flatForward, now);
                    break;
                case "window_display":
                    DrawWindowDisplay(origin, right, flatForward, now);
                    break;
                case "particle_column":
                    DrawParticleColumn(origin, right, flatForward, now);
                    break;
                case "street_totem":
                    DrawStreetTotem(origin, right, flatForward, now);
                    break;
                case "home_widget":
                    DrawHomeWidget(origin, right, flatForward, now);
                    break;
                case "room_boundary":
                    DrawRoomBoundary(origin, right, flatForward, now);
                    break;
                case "logo_orbit":
                    DrawLogoOrbit(origin, right, flatForward, now);
                    break;
                case "warning_barrier":
                    DrawWarningBarrier(origin, right, flatForward, now);
                    break;
                default:
                    DrawNeonSign(origin, right, flatForward, now);
                    break;
            }
            ApplyColor(CurrentAlpha, pulse);
        }

        private float AnimatedTime(float now)
        {
            switch ((_hologram?.AnimationId ?? string.Empty)
                    .Trim().ToLowerInvariant())
            {
                case "scan": return now * 1.45f;
                case "orbit": return now * .72f;
                case "data_rain": return now * 2.1f;
                default: return now;
            }
        }

        private float AnimatedPulse(float now)
        {
            switch ((_hologram?.AnimationId ?? string.Empty)
                    .Trim().ToLowerInvariant())
            {
                case "scan":
                    return .72f + .28f * Mathf.PingPong(now, 1f);
                case "orbit":
                    return .82f + .18f * Mathf.Sin(now * 1.7f);
                case "data_rain":
                    return .68f + .32f *
                        Mathf.Abs(Mathf.Sin(now * 4.6f));
                default:
                    return .75f + .25f * Mathf.Sin(now * 3.1f);
            }
        }

        private void DrawNeonSign(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            float hover =
                (0.62f + Mathf.Sin(now * 2.2f) * 0.025f) * Sy;
            Vector3 center = origin + Vector3.up * hover;
            Rectangle(
                _lines[0], center, right, Vector3.up,
                0.48f * Sx, 0.16f * Sy);
            Rectangle(
                _lines[1], center, right, Vector3.up,
                0.43f * Sx, 0.125f * Sy);
            Ring(
                _lines[2], origin + Vector3.up * 0.025f,
                right, forward, 0.16f * Sx, 32);
            SetLine(
                _lines[3], origin,
                center - right * 0.48f * Sx,
                center + right * 0.48f * Sx);
            SetLine(
                _lines[4],
                center - Vector3.up * 0.16f * Sy,
                center + Vector3.up * 0.16f * Sy);
            PlacePanel(
                center, right, Vector3.up,
                0.88f * Sx, 0.25f * Sy, forward);
            PlaceLabel(center, forward);
        }

        private void DrawBillboard(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            Vector3 center =
                origin + Vector3.up *
                (0.48f + Mathf.Sin(now * 1.7f) * 0.02f) * Sy;
            Rectangle(
                _lines[0], center, right, Vector3.up,
                0.58f * Sx, 0.28f * Sy);
            Rectangle(
                _lines[1], center, right, Vector3.up,
                0.53f * Sx, 0.23f * Sy);
            for (int i = 2; i < _lines.Count; i++)
            {
                float x =
                    Mathf.Lerp(-0.48f, 0.48f, (i - 1) / 4f) * Sx;
                SetLine(
                    _lines[i],
                    center + right * x - Vector3.up * 0.2f * Sy,
                    center + right * x + Vector3.up * 0.2f * Sy);
            }
            PlacePanel(
                center, right, Vector3.up,
                1.04f * Sx, 0.45f * Sy, forward);
            PlaceLabel(center, forward);
        }

        private void DrawVehicleFx(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            Vector3 basePoint = origin + Vector3.up * 0.12f * Sy;
            for (int i = 0; i < _lines.Count; i++)
            {
                float lane = (i - 2f) * 0.07f * Sx;
                float phase = Mathf.Repeat(now * 0.9f + i * 0.17f, 1f);
                float length = Mathf.Lerp(0.16f, 0.62f, phase) * Sz;
                Vector3 start = basePoint + right * lane;
                Vector3 end =
                    start + forward * length +
                    Vector3.up *
                    (0.04f + Mathf.Sin(now * 4f + i) * 0.035f) * Sy;
                SetLine(_lines[i], start, Vector3.Lerp(start, end, 0.48f), end);
            }
            _panel.enabled = false;
            _label.transform.position = basePoint + Vector3.up * 0.34f;
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawBeacon(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            float height =
                (1.15f + Mathf.Sin(now * 2.1f) * 0.08f) * Sy;
            Vector3 top = origin + Vector3.up * height;
            SetLine(_lines[0], origin, top);
            Ring(
                _lines[1], origin + Vector3.up * 0.03f,
                right, forward, 0.26f * Sx, 36);
            Ring(_lines[2], origin + Vector3.up * 0.05f, right, forward,
                (0.42f + Mathf.Sin(now * 2f) * 0.04f) * Sx, 36);
            Ring(
                _lines[3], top, right, Vector3.up, 0.18f * Sx, 32);
            SetLine(
                _lines[4],
                top - right * 0.22f * Sx,
                top + right * 0.22f * Sx);
            PlacePanel(
                top, right, Vector3.up,
                0.62f * Sx, 0.22f * Sy, forward);
            PlaceLabel(top, forward);
        }

        private void DrawMemoryEcho(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                float radius = (0.13f + i * 0.075f +
                    Mathf.Sin(now * 1.7f + i) * 0.015f) * Sx;
                Ring(
                    _lines[i],
                    origin + Vector3.up * (0.08f + i * 0.06f) * Sy,
                    right,
                    forward,
                    radius,
                    32);
            }
            _panel.enabled = false;
            _label.transform.position = origin + Vector3.up * 0.62f;
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawPortal(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            float w = 0.62f * Sx;
            float h = 1.25f * Sy;
            Vector3 left = origin - right * w;
            Vector3 rightFoot = origin + right * w;
            SetLine(
                _lines[0],
                left,
                left + Vector3.up * h,
                origin + Vector3.up * (h + .32f * Sy),
                rightFoot + Vector3.up * h,
                rightFoot);
            SetLine(
                _lines[1],
                left + right * .12f,
                left + right * .12f + Vector3.up * h,
                rightFoot - right * .12f + Vector3.up * h,
                rightFoot - right * .12f);
            Ring(
                _lines[2],
                origin + Vector3.up * .05f,
                right,
                forward,
                (.45f + Mathf.Sin(now * 2.4f) * .05f) * Sx,
                36);
            SetLine(_lines[3], origin, origin + Vector3.up * (h + .3f));
            Ring(
                _lines[4],
                origin + Vector3.up * h,
                right,
                Vector3.up,
                .22f * Sx,
                28);
            _panel.enabled = false;
            _label.transform.position = origin + Vector3.up * (h + .55f * Sy);
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawDrone(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            float hover = Mathf.Sin(now * 2.3f) * .08f * Sy;
            Vector3 center = origin + Vector3.up * (.75f * Sy + hover);
            SetLine(
                _lines[0],
                center - right * .5f * Sx,
                center,
                center + right * .5f * Sx);
            SetLine(
                _lines[1],
                center - Vector3.up * .30f * Sy,
                center,
                center + Vector3.up * .30f * Sy);
            Ring(_lines[2], center - right * .5f * Sx, right, forward, .17f * Sx, 24);
            Ring(_lines[3], center + right * .5f * Sx, right, forward, .17f * Sx, 24);
            SetLine(
                _lines[4],
                center,
                center - Vector3.up * (.75f + Mathf.PingPong(now, .5f)) * Sy);
            _panel.enabled = false;
            _label.transform.position = center + Vector3.up * .32f * Sy;
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawGiantHologram(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            float h = 2.4f * Sy;
            Vector3 head = origin + Vector3.up * h;
            Ring(_lines[0], head, right, Vector3.up, .26f * Sx, 32);
            SetLine(
                _lines[1],
                head - Vector3.up * .3f,
                origin + Vector3.up * 1.35f * Sy,
                origin + Vector3.up * .3f);
            SetLine(
                _lines[2],
                origin + Vector3.up * 1.7f * Sy - right * .75f * Sx,
                origin + Vector3.up * 1.85f * Sy,
                origin + Vector3.up * 1.7f * Sy + right * .75f * Sx);
            SetLine(
                _lines[3],
                origin + Vector3.up * .35f,
                origin - right * .42f * Sx,
                origin + right * .42f * Sx);
            for (int i = 0; i < 18; i++)
            {
                float y = i / 17f * h;
                float glitch = Mathf.Sin(now * 7f + i * 1.7f) * .07f * Sx;
                if (i == 0)
                    _lines[4].positionCount = 18;
                _lines[4].SetPosition(
                    i,
                    origin + Vector3.up * y + right * glitch);
            }
            _panel.enabled = false;
            _label.transform.position = head + Vector3.up * .45f;
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawDirectionArrow(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            float travel = Mathf.Repeat(now * .8f, 1f) * 1.1f * Sz;
            Vector3 center = origin + forward * travel + Vector3.up * .08f;
            SetLine(
                _lines[0],
                center - forward * .42f * Sz,
                center + forward * .42f * Sz);
            SetLine(
                _lines[1],
                center + forward * .42f * Sz,
                center + forward * .1f * Sz - right * .28f * Sx);
            SetLine(
                _lines[2],
                center + forward * .42f * Sz,
                center + forward * .1f * Sz + right * .28f * Sx);
            Ring(_lines[3], origin, right, forward, .22f * Sx, 28);
            SetLine(_lines[4], origin, center);
            _panel.enabled = false;
            _label.transform.position = origin + Vector3.up * .58f * Sy;
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawBuildingCrown(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            Vector3 center = origin + Vector3.up * .3f * Sy;
            float radius = (.65f + Mathf.Sin(now * 1.8f) * .04f) * Sx;
            Ring(_lines[0], center, right, forward, radius, 40);
            Ring(_lines[1], center + Vector3.up * .22f * Sy, right, forward, radius * .82f, 40);
            Ring(_lines[2], center + Vector3.up * .45f * Sy, right, forward, radius * .58f, 40);
            SetLine(_lines[3], center - right * radius, center + Vector3.up * .45f, center + right * radius);
            SetLine(_lines[4], center - forward * radius, center + Vector3.up * .45f, center + forward * radius);
            _panel.enabled = false;
            _label.transform.position = center + Vector3.up * .75f * Sy;
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawWindowDisplay(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            Vector3 center = origin + Vector3.up * .65f * Sy;
            Rectangle(_lines[0], center, right, Vector3.up, .72f * Sx, .48f * Sy);
            for (int i = 1; i < 5; i++)
            {
                float y = Mathf.Lerp(-.34f, .34f, (i - 1) / 3f) * Sy;
                float scan = i == 4
                    ? Mathf.Repeat(now * .7f, 1f) * .68f * Sy - .34f * Sy
                    : y;
                SetLine(
                    _lines[i],
                    center - right * .65f * Sx + Vector3.up * scan,
                    center + right * .65f * Sx + Vector3.up * scan);
            }
            PlacePanel(center, right, Vector3.up, 1.35f * Sx, .86f * Sy, forward);
            PlaceLabel(center, forward);
        }

        private void DrawParticleColumn(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            for (int line = 0; line < 5; line++)
            {
                _lines[line].loop = false;
                _lines[line].positionCount = 14;
                for (int i = 0; i < 14; i++)
                {
                    float t = i / 13f;
                    float phase = now * (1.1f + line * .08f) + line * 1.7f + t * 9f;
                    _lines[line].SetPosition(
                        i,
                        origin +
                        Vector3.up * (t * 1.8f * Sy) +
                        right * Mathf.Sin(phase) * (.18f + line * .035f) * Sx +
                        forward * Mathf.Cos(phase * .83f) * .16f * Sz);
                }
            }
            _panel.enabled = false;
            _label.transform.position = origin + Vector3.up * 2.1f * Sy;
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawStreetTotem(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            float h = 1.35f * Sy;
            SetLine(_lines[0], origin, origin + Vector3.up * h);
            Rectangle(
                _lines[1],
                origin + Vector3.up * h,
                right,
                Vector3.up,
                .4f * Sx,
                .18f * Sy);
            Ring(_lines[2], origin + Vector3.up * .04f, right, forward, .25f * Sx, 32);
            Ring(_lines[3], origin + Vector3.up * h, right, forward, .13f * Sx, 28);
            SetLine(
                _lines[4],
                origin + Vector3.up * .25f,
                origin + Vector3.up * (h - .25f));
            PlacePanel(
                origin + Vector3.up * h,
                right,
                Vector3.up,
                .72f * Sx,
                .28f * Sy,
                forward);
            PlaceLabel(origin + Vector3.up * h, forward);
        }

        private void DrawHomeWidget(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            Vector3 center = origin + Vector3.up * .45f * Sy;
            Ring(_lines[0], center, right, Vector3.up, .42f * Sx, 36);
            Ring(_lines[1], center, right, Vector3.up, .31f * Sx, 36);
            float angle = now * .8f;
            SetLine(
                _lines[2],
                center,
                center + right * Mathf.Cos(angle) * .28f * Sx +
                Vector3.up * Mathf.Sin(angle) * .28f * Sy);
            SetLine(_lines[3], origin, center - right * .42f, center + right * .42f);
            SetLine(_lines[4], center - Vector3.up * .42f, center + Vector3.up * .42f);
            _panel.enabled = false;
            _label.transform.position = center;
            PlaceLabel(center, forward);
        }

        private void DrawRoomBoundary(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            float x = .8f * Sx;
            float z = .8f * Sz;
            float h = 1.2f * Sy;
            Rectangle(_lines[0], origin, right, forward, x, z);
            Rectangle(_lines[1], origin + Vector3.up * h, right, forward, x, z);
            SetLine(_lines[2], origin - right * x - forward * z, origin - right * x - forward * z + Vector3.up * h);
            SetLine(_lines[3], origin + right * x - forward * z, origin + right * x - forward * z + Vector3.up * h);
            SetLine(_lines[4], origin + right * x + forward * z, origin + right * x + forward * z + Vector3.up * h);
            _panel.enabled = false;
            _label.transform.position = origin + Vector3.up * (h + .25f);
            PlaceLabel(_label.transform.position, forward);
        }

        private void DrawLogoOrbit(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            Vector3 center = origin + Vector3.up * .55f * Sy;
            Ring(_lines[0], center, right, Vector3.up, .36f * Sx, 40);
            Ring(_lines[1], center, forward, Vector3.up, .48f * Sz, 40);
            Ring(_lines[2], center, right, forward, .58f * Sx, 40);
            Vector3 satellite = center +
                right * Mathf.Cos(now) * .58f * Sx +
                forward * Mathf.Sin(now) * .58f * Sz;
            Ring(_lines[3], satellite, right, Vector3.up, .08f * Sx, 20);
            SetLine(_lines[4], center, satellite);
            _panel.enabled = false;
            _label.transform.position = center;
            PlaceLabel(center, forward);
        }

        private void DrawWarningBarrier(
            Vector3 origin, Vector3 right, Vector3 forward, float now)
        {
            Vector3 center = origin + Vector3.up * .55f * Sy;
            Rectangle(_lines[0], center, right, Vector3.up, .85f * Sx, .45f * Sy);
            for (int i = 1; i < 5; i++)
            {
                float x = Mathf.Lerp(-.72f, .72f, (i - 1) / 3f) * Sx;
                SetLine(
                    _lines[i],
                    center + right * (x - .18f * Sx) - Vector3.up * .36f * Sy,
                    center + right * (x + .18f * Sx) + Vector3.up * .36f * Sy);
            }
            PlacePanel(center, right, Vector3.up, 1.6f * Sx, .82f * Sy, forward);
            PlaceLabel(center, forward);
        }

        private void PlacePanel(
            Vector3 center,
            Vector3 right,
            Vector3 up,
            float width,
            float height,
            Vector3 forward)
        {
            _panel.enabled = true;
            _panel.transform.position = center + forward * 0.008f;
            _panel.transform.rotation = Quaternion.LookRotation(-forward, up);
            _panel.transform.localScale = new Vector3(width, height, 1f);
        }

        private void PlaceLabel(Vector3 center, Vector3 forward)
        {
            _label.transform.position = center - forward * 0.012f;
            _label.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        }

        private void ApplyColor(float alpha, float pulse)
        {
            Color edge = _accent;
            edge.a = Mathf.Clamp01(alpha) * Mathf.Lerp(0.62f, 0.98f, pulse);
            Color fill = _accent;
            fill.a = Mathf.Clamp01(alpha) *
                (_assetTexture == null ? 0.13f : 0.82f);
            if (_assetTexture != null) fill = new Color(1f, 1f, 1f, fill.a);
            foreach (LineRenderer line in _lines)
            {
                int index = _lines.IndexOf(line);
                Color lineColor = index % 2 == 0 ? edge : _secondary;
                lineColor.a = edge.a;
                line.startColor = lineColor;
                line.endColor = lineColor;
            }
            SetMaterialColor(_lineMaterial, edge);
            SetMaterialColor(_panelMaterial, fill);
            if (_label != null)
            {
                Color label = Color.white;
                label.a = Mathf.Clamp01(alpha);
                _label.color = label;
            }
        }

        private LineRenderer MakeLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.alignment = LineAlignment.View;
            line.numCornerVertices = 4;
            if (_lineMaterial != null) line.sharedMaterial = _lineMaterial;
            return line;
        }

        private static void SetLine(LineRenderer line, params Vector3[] points)
        {
            line.loop = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
        }

        private static void Rectangle(
            LineRenderer line,
            Vector3 center,
            Vector3 right,
            Vector3 up,
            float halfWidth,
            float halfHeight)
        {
            line.loop = true;
            line.positionCount = 4;
            line.SetPosition(0, center - right * halfWidth - up * halfHeight);
            line.SetPosition(1, center - right * halfWidth + up * halfHeight);
            line.SetPosition(2, center + right * halfWidth + up * halfHeight);
            line.SetPosition(3, center + right * halfWidth - up * halfHeight);
        }

        private static void Ring(
            LineRenderer line,
            Vector3 center,
            Vector3 axisA,
            Vector3 axisB,
            float radius,
            int count)
        {
            line.loop = true;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f;
                line.SetPosition(
                    i,
                    center +
                    axisA * (Mathf.Cos(angle) * radius) +
                    axisB * (Mathf.Sin(angle) * radius));
            }
        }

        private void SetEnabled(bool enabled)
        {
            foreach (LineRenderer line in _lines) line.enabled = enabled;
            if (_panel != null) _panel.enabled = enabled && !_assetIsModel;
            if (_label != null) _label.enabled = enabled;
            if (_assetModel != null) _assetModel.SetActive(enabled);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
            if (_panelMaterial != null) Destroy(_panelMaterial);
            if (_assetTexture != null) Destroy(_assetTexture);
            if (_assetModel != null) Destroy(_assetModel);
        }

        public static bool TryReadHologram(
            UIIntent intent,
            out Hologram hologram,
            out string error)
        {
            hologram = null;
            if (!WorldSemanticMarker.TryReadMarker(
                    intent,
                    out WorldSemanticMarker.Marker marker,
                    out error))
                return false;
            string template = IntentRead.Content(
                intent, "template_id", "").Trim().ToLowerInvariant();
            if (!AllowedTemplate(template))
            {
                error = "hologram_template_invalid";
                return false;
            }
            hologram = new Hologram
            {
                Position = marker.Position,
                Id = marker.MarkerId,
                CalibrationId = marker.CalibrationId,
                TemplateId = template,
                ArchetypeId = IntentRead.Content(
                    intent, "archetype_id", template),
                AnimationId = IntentRead.Content(
                    intent, "animation_id", "soft_pulse"),
                Label = marker.Label,
                Subtitle = marker.Subtitle,
                Quality = marker.AnchorQuality,
                DepthValid = marker.DepthValid,
                Accent = ReadColor(
                    IntentRead.Content(intent, "accent_hex", ""),
                    TemplateColor(template)),
                Secondary = ReadColor(
                    IntentRead.Content(intent, "secondary_hex", ""),
                    new Color(.48f, .24f, 1f, 1f)),
                Scale = ReadScale(intent),
                AnchorRotation = Quaternion.Euler(ReadEuler(intent)),
                AssetMime = IntentRead.Content(intent, "asset_mime", ""),
                AssetSha256 =
                    IntentRead.Content(intent, "asset_sha256", ""),
                AssetBase64 =
                    IntentRead.Content(intent, "asset_base64", ""),
                AssetFilePath =
                    IntentRead.Content(intent, "asset_file_path", ""),
                MotionPath = WorldMapStore.CleanMotionPath(
                    IntentRead.Content(intent, "motion_path", "static")),
                MotionRadiusM = Mathf.Clamp(
                    (float)IntentRead.Num(
                        intent.Content, "motion_radius_m", 1.5d),
                    .1f,
                    40f),
                MotionSpeed = Mathf.Clamp(
                    (float)IntentRead.Num(
                        intent.Content, "motion_speed", .8d),
                    .05f,
                    5f),
                MotionHeightM = Mathf.Clamp(
                    (float)IntentRead.Num(
                        intent.Content, "motion_height_m", 0d),
                    -20f,
                    20f),
                MaxRenderDistanceM = Mathf.Clamp(
                    (float)IntentRead.Num(
                        intent.Content,
                        "max_render_distance_m",
                        350d),
                    5f,
                    1000f),
            };
            error = null;
            return true;
        }

        private static bool AllowedTemplate(string template)
        {
            switch (template)
            {
                case "neon_sign":
                case "holo_billboard":
                case "vehicle_fx":
                case "poi_beacon":
                case "memory_echo":
                case "annotation":
                case "portal_arch":
                case "sky_drone":
                case "giant_hologram":
                case "direction_arrow":
                case "building_crown":
                case "window_display":
                case "particle_column":
                case "street_totem":
                case "home_widget":
                case "room_boundary":
                case "logo_orbit":
                case "warning_barrier":
                    return true;
                default:
                    return false;
            }
        }

        private static string CleanLabel(string value)
        {
            string clean = (value ?? string.Empty).Replace("<", "‹").Replace(">", "›");
            return clean.Length <= 80 ? clean : clean.Substring(0, 80);
        }

        private static Color TemplateColor(string template)
        {
            switch (template)
            {
                case "holo_billboard": return new Color(1f, 0.24f, 0.78f, 1f);
                case "vehicle_fx": return new Color(1f, 0.34f, 0.1f, 1f);
                case "poi_beacon": return new Color(0.2f, 1f, 0.62f, 1f);
                case "memory_echo": return new Color(0.65f, 0.38f, 1f, 1f);
                default: return new Color(0.15f, 0.92f, 1f, 1f);
            }
        }

        private float Sx => Mathf.Clamp(
            _hologram?.Scale.x ?? 1f, .1f, WorldMapStore.MaxWorldScale);
        private float Sy => Mathf.Clamp(
            _hologram?.Scale.y ?? 1f, .1f, WorldMapStore.MaxWorldScale);
        private float Sz => Mathf.Clamp(
            _hologram?.Scale.z ?? 1f, .1f, WorldMapStore.MaxWorldScale);

        private static Vector3 ReadScale(UIIntent intent)
        {
            if (
                intent?.Content != null &&
                intent.Content.TryGetValue("scale", out object raw) &&
                WorldContractRead.TryVector(raw, out Vector3 scale) &&
                scale.x >= .1f &&
                scale.x <= WorldMapStore.MaxWorldScale &&
                scale.y >= .1f &&
                scale.y <= WorldMapStore.MaxWorldScale &&
                scale.z >= .1f &&
                scale.z <= WorldMapStore.MaxWorldScale)
                return scale;
            return Vector3.one;
        }

        private Vector3 MotionPosition(float now)
        {
            Vector3 basePosition = _hologram.Position;
            string path = WorldMapStore.CleanMotionPath(_hologram.MotionPath);
            if (path == "static") return basePosition;
            float phase =
                (Mathf.Abs((_hologram.Id ?? string.Empty).GetHashCode()) % 1000) *
                .006283185f;
            float t = now * _hologram.MotionSpeed + phase;
            float radius = _hologram.MotionRadiusM;
            Vector3 right = _hologram.AnchorRotation * Vector3.right;
            Vector3 forward = _hologram.AnchorRotation * Vector3.forward;
            Vector3 up = _hologram.AnchorRotation * Vector3.up;
            switch (path)
            {
                case "orbit":
                    return basePosition +
                        right * (Mathf.Cos(t) * radius) +
                        forward * (Mathf.Sin(t) * radius) +
                        up * _hologram.MotionHeightM;
                case "patrol":
                    return basePosition +
                        right * (Mathf.Sin(t) * radius) +
                        up * _hologram.MotionHeightM;
                case "figure8":
                    return basePosition +
                        right * (Mathf.Sin(t) * radius) +
                        forward * (Mathf.Sin(t * 2f) * radius * .5f) +
                        up * _hologram.MotionHeightM;
                case "vertical":
                    return basePosition +
                        up * (
                            _hologram.MotionHeightM +
                            Mathf.Sin(t) * radius);
                default:
                    return basePosition;
            }
        }

        private static Vector3 ReadEuler(UIIntent intent)
        {
            if (
                intent?.Content != null &&
                intent.Content.TryGetValue(
                    "local_euler", out object raw) &&
                WorldContractRead.TryVector(raw, out Vector3 euler))
                return euler;
            return Vector3.zero;
        }

        private void BindAsset()
        {
            string sha = _hologram?.AssetSha256 ?? string.Empty;
            if (string.Equals(
                    sha, _assetSha256, StringComparison.Ordinal))
                return;
            if (_assetTexture != null) Destroy(_assetTexture);
            _assetTexture = null;
            if (_assetModel != null) Destroy(_assetModel);
            _assetModel = null;
            _assetIsModel = false;
            _assetSha256 = string.Empty;
            if (
                !string.IsNullOrWhiteSpace(sha) &&
                _hologram.AssetMime == "model/gltf-binary" &&
                TryReadAssetBytes(out byte[] modelBytes))
            {
                try
                {
                    Shader shader = Shader.Find("MLOmega/XREAL FreeGuy Mesh");
                    if (RuntimeGlbModel.TryInstantiate(
                            modelBytes,
                            transform,
                            shader,
                            out _assetModel,
                            out _))
                    {
                        _assetSha256 = sha;
                        _assetIsModel = true;
                        SetPanelTexture(null);
                        return;
                    }
                }
                catch (Exception)
                {
                    SetPanelTexture(null);
                    return;
                }
            }
            if (
                string.IsNullOrWhiteSpace(sha) ||
                !TryReadAssetBytes(out byte[] bytes) ||
                (_hologram.AssetMime != "image/png" &&
                 _hologram.AssetMime != "image/jpeg"))
            {
                SetPanelTexture(null);
                return;
            }
            try
            {
                if (
                    bytes.Length <= 0 ||
                    bytes.Length > WorldMapStore.MaxAssetBytes)
                    return;
                var texture =
                    new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (
                    !texture.LoadImage(bytes, true) ||
                    texture.width > WorldMapStore.MaxAssetDimension ||
                    texture.height > WorldMapStore.MaxAssetDimension)
                {
                    Destroy(texture);
                    return;
                }
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                _assetTexture = texture;
                _assetSha256 = sha;
                SetPanelTexture(texture);
            }
            catch (Exception)
            {
                SetPanelTexture(null);
            }
        }

        private bool TryReadAssetBytes(out byte[] bytes)
        {
            bytes = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(_hologram.AssetBase64))
                    bytes = Convert.FromBase64String(_hologram.AssetBase64);
                else if (
                    !string.IsNullOrWhiteSpace(_hologram.AssetFilePath) &&
                    File.Exists(_hologram.AssetFilePath))
                    bytes = File.ReadAllBytes(_hologram.AssetFilePath);
                if (
                    bytes == null ||
                    bytes.Length <= 0 ||
                    bytes.Length > WorldMapStore.MaxAssetBytes)
                    return false;
                using (SHA256 hash = SHA256.Create())
                {
                    string digest = BitConverter.ToString(
                        hash.ComputeHash(bytes)).Replace("-", string.Empty)
                        .ToLowerInvariant();
                    return string.Equals(
                        digest,
                        _hologram.AssetSha256,
                        StringComparison.Ordinal);
                }
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        private void SetPanelTexture(Texture texture)
        {
            if (_panelMaterial == null) return;
            if (_panelMaterial.HasProperty("_BaseMap"))
                _panelMaterial.SetTexture("_BaseMap", texture);
            if (_panelMaterial.HasProperty("_MainTex"))
                _panelMaterial.SetTexture("_MainTex", texture);
        }

        private static Color ReadColor(string hex, Color fallback)
        {
            string clean = (hex ?? string.Empty).Trim().TrimStart('#');
            return ColorUtility.TryParseHtmlString("#" + clean, out Color color)
                ? color
                : fallback;
        }

        private static Material TransparentMaterial(Shader shader)
        {
            var material = new Material(shader);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
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
    }
}
