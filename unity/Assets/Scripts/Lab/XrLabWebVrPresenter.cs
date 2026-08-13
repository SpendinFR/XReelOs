using System;
using UnityEngine;
using UnityEngine.UI;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Presents the already-running TLab WebView video texture as an immersive
    /// stereo panorama. The WebView remains the decoder/source of truth, while
    /// Unity/XREAL keeps ownership of head tracking and stereo rendering.
    ///
    /// Projection conventions follow the MIT-licensed mpv-android-vr mpv360
    /// shader and vr2xr's VR180/VR360 renderer, adapted to Unity single-pass XR.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrLabWebVrPresenter : MonoBehaviour
    {
        public enum ProjectionMode
        {
            Vr180Sbs = 0,
            Vr360Sbs = 1,
            Vr360Mono = 2,
            DualFisheyeSbs = 3,
        }

        public enum StereoLayout
        {
            LeftRight = 0,
            RightLeft = 1,
            TopBottom = 2,
            BottomTop = 3,
        }

        private static readonly int SourceRectId = Shader.PropertyToID("_SourceRect");
        private static readonly int ProjectionId = Shader.PropertyToID("_Projection");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int StereoLayoutId = Shader.PropertyToID("_StereoLayout");
        private static readonly int ZoomId = Shader.PropertyToID("_Zoom");

        private Camera _camera;
        private Texture _sourceTexture;
        private GameObject _dome;
        private MeshRenderer _renderer;
        private Material _material;
        private Rect _sourceRect = new Rect(0f, 0f, 1f, 1f);
        private Quaternion _referenceRotation = Quaternion.identity;

        public bool Active { get; private set; }
        public ProjectionMode Mode { get; private set; } = ProjectionMode.Vr180Sbs;
        public StereoLayout Layout { get; private set; } = StereoLayout.LeftRight;
        public float Zoom { get; private set; } = 1f;
        public string ZoomLabel => Mathf.RoundToInt(Zoom * 100f) + "%";

        public string ModeLabel
        {
            get
            {
                switch (Mode)
                {
                    case ProjectionMode.Vr360Sbs:
                        return "360S";
                    case ProjectionMode.Vr360Mono:
                        return "360M";
                    case ProjectionMode.DualFisheyeSbs:
                        return "FISH";
                    default:
                        return "180";
                }
            }
        }

        public string StereoLabel
        {
            get
            {
                switch (Layout)
                {
                    case StereoLayout.RightLeft:
                        return "180R";
                    case StereoLayout.TopBottom:
                        return "180TB";
                    case StereoLayout.BottomTop:
                        return "180BT";
                    default:
                        return "180";
                }
            }
        }

        public bool Enter(
            Camera camera,
            RawImage source,
            Rect sourceRect,
            int videoWidth,
            int videoHeight,
            string sourceHint)
        {
            return Enter(
                camera,
                source != null ? source.texture : null,
                sourceRect,
                videoWidth,
                videoHeight,
                sourceHint);
        }

        public bool Enter(
            Camera camera,
            Texture source,
            Rect sourceRect,
            int videoWidth,
            int videoHeight,
            string sourceHint)
        {
            if (camera == null || source == null)
            {
                Debug.LogWarning("[XrLabVR] source texture or XR camera unavailable.");
                return false;
            }

            Shader shader = Resources.Load<Shader>("XrLabWebVr");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError("[XrLabVR] immersive projection shader unavailable.");
                return false;
            }

            _camera = camera;
            _sourceTexture = source;
            _sourceRect = ClampRect(sourceRect);
            Mode = DetectProjection(videoWidth, videoHeight, sourceRect, sourceHint);
            Layout = DetectStereoLayout(sourceHint);

            EnsureDome(shader);
            Recenter();
            UpdateMaterial();
            Active = true;
            _renderer.enabled = true;
            Debug.Log(
                $"[XrLabVR] immersive web video active mode={ModeLabel} " +
                $"video={videoWidth}x{videoHeight} uv={_sourceRect}.");
            return true;
        }

        public void Exit()
        {
            Active = false;
            if (_renderer != null) _renderer.enabled = false;
            Debug.Log("[XrLabVR] immersive web video stopped.");
        }

        public void Recenter()
        {
            if (_camera == null) return;
            Vector3 euler = _camera.transform.rotation.eulerAngles;
            _referenceRotation = Quaternion.Euler(euler.x, euler.y, 0f);
            if (_dome != null)
                _dome.transform.SetPositionAndRotation(
                    _camera.transform.position,
                    _referenceRotation);
            Debug.Log("[XrLabVR] view recentered.");
        }

        public void SetMode(ProjectionMode mode)
        {
            Mode = mode;
            if (_material != null) _material.SetFloat(ProjectionId, (float)Mode);
            Debug.Log("[XrLabVR] projection=" + ModeLabel);
        }

        public void CycleWideProjection()
        {
            switch (Mode)
            {
                case ProjectionMode.Vr360Sbs:
                    SetMode(ProjectionMode.Vr360Mono);
                    break;
                case ProjectionMode.Vr360Mono:
                    SetMode(ProjectionMode.DualFisheyeSbs);
                    break;
                default:
                    SetMode(ProjectionMode.Vr360Sbs);
                    break;
            }
        }

        public void CycleVr180Layout()
        {
            if (Mode != ProjectionMode.Vr180Sbs)
            {
                SetMode(ProjectionMode.Vr180Sbs);
                return;
            }
            Layout = (StereoLayout)(((int)Layout + 1) % 4);
            if (_material != null)
                _material.SetFloat(StereoLayoutId, (float)Layout);
            Debug.Log("[XrLabVR] stereo-layout=" + StereoLabel);
        }

        public void AdjustZoom(float delta) => SetZoom(Zoom + delta);

        public void ResetZoom() => SetZoom(1f);

        public void SetZoom(float zoom)
        {
            Zoom = Mathf.Clamp(zoom, .70f, 1.60f);
            if (_material != null) _material.SetFloat(ZoomId, Zoom);
            Debug.Log("[XrLabVR] projection zoom=" + ZoomLabel);
        }

        private void LateUpdate()
        {
            if (!Active || _camera == null || _dome == null) return;

            // Translation must not create parallax inside a prerecorded sphere.
            // Keep the dome centred on the eyes while preserving the calibrated
            // world rotation so head rotation explores the panorama naturally.
            _dome.transform.SetPositionAndRotation(
                _camera.transform.position,
                _referenceRotation);
            UpdateMaterial();
        }

        private void EnsureDome(Shader shader)
        {
            if (_dome != null) return;

            _dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dome.name = "XReel Web VR Dome";
            // The browser window is a millimetre-scaled world-space Canvas.
            // Parenting the dome to it would collapse the 24 m projection to a
            // few centimetres. Keep the immersive renderer at scene-root scale;
            // this component still owns and destroys it explicitly.
            _dome.transform.SetParent(null, false);
            _dome.transform.localScale = Vector3.one * 24f;

            Collider collider = _dome.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            _renderer = _dome.GetComponent<MeshRenderer>();
            _material = new Material(shader)
            {
                name = "XReel Web VR Runtime Material",
                hideFlags = HideFlags.DontSave,
            };
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;
        }

        private void UpdateMaterial()
        {
            if (_material == null || _sourceTexture == null) return;
            _material.SetTexture(MainTexId, _sourceTexture);
            _material.SetVector(
                SourceRectId,
                new Vector4(
                    _sourceRect.x,
                    _sourceRect.y,
                    _sourceRect.width,
                    _sourceRect.height));
            _material.SetFloat(ProjectionId, (float)Mode);
            _material.SetFloat(StereoLayoutId, (float)Layout);
            _material.SetFloat(ZoomId, Zoom);
        }

        private static ProjectionMode DetectProjection(
            int videoWidth,
            int videoHeight,
            Rect sourceRect,
            string sourceHint)
        {
            string hint = (sourceHint ?? string.Empty).ToLowerInvariant();
            if (hint.Contains("fisheye") || hint.Contains("fish-eye"))
                return ProjectionMode.DualFisheyeSbs;

            bool hints360 = hint.Contains("360") || hint.Contains("equirect");
            bool hintsMono = hint.Contains("mono") && !hint.Contains("stereo");
            if (hints360 && hintsMono) return ProjectionMode.Vr360Mono;
            if (hints360) return ProjectionMode.Vr360Sbs;
            if (hint.Contains("180") || hint.Contains("vr180"))
                return ProjectionMode.Vr180Sbs;

            float aspect = videoHeight > 0
                ? videoWidth / (float)videoHeight
                : sourceRect.height > .0001f
                    ? sourceRect.width / sourceRect.height
                    : 2f;

            // Two full equirectangular eyes side by side are normally close to
            // 1:1 overall. Two square VR180 eyes are normally close to 2:1.
            if (aspect > .72f && aspect < 1.30f)
                return ProjectionMode.Vr360Sbs;
            return ProjectionMode.Vr180Sbs;
        }

        private static StereoLayout DetectStereoLayout(string sourceHint)
        {
            string hint = (sourceHint ?? string.Empty)
                .ToLowerInvariant()
                .Replace('-', '_');
            if (
                hint.Contains("bottom_top") || hint.Contains("bottomtop") ||
                hint.Contains("_bt_") || hint.Contains("tb_rl"))
                return StereoLayout.BottomTop;
            if (
                hint.Contains("top_bottom") || hint.Contains("topbottom") ||
                hint.Contains("over_under") || hint.Contains("_tb_") ||
                hint.Contains("stereo_mode=tb"))
                return StereoLayout.TopBottom;
            if (
                hint.Contains("right_left") || hint.Contains("rightleft") ||
                hint.Contains("_rl_") || hint.Contains("sbs_rl") ||
                hint.Contains("stereo_mode=rl"))
                return StereoLayout.RightLeft;
            return StereoLayout.LeftRight;
        }

        private static Rect ClampRect(Rect rect)
        {
            float x = Mathf.Clamp01(rect.x);
            float y = Mathf.Clamp01(rect.y);
            float width = Mathf.Clamp(rect.width, .001f, 1f - x);
            float height = Mathf.Clamp(rect.height, .001f, 1f - y);
            return new Rect(x, y, width, height);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_dome != null) Destroy(_dome);
        }
    }
}
