// MLOmega V19 — E25
// URP ScriptableRendererFeature that produces the frosted background sampled by
// LiquidGlass.shader. Once per frame, after opaques, it grabs the camera colour,
// runs a Kawase dual-filter blur (GlassKawaseBlur.shader) down and back up a small
// mip chain, and exposes the result as the global texture _MLOmegaGlassBlur so any
// number of glass panels share a single blur (ADR §E25 "blur payé une fois").
//
// Implemented against URP 17 (Unity 6) RenderGraph. If the pipeline runs in
// Compatibility Mode (RenderGraph disabled) the feature degrades gracefully: it
// simply does not bind the texture, and LiquidGlass falls back to its flat
// translucent look (the _HAS_BLUR_TEX keyword stays off). No hard dependency, so a
// project without this feature still renders valid glass.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MLOmega.XR.UI.Rendering
{
    public sealed class GlassBlurFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Tooltip("Kawase blur shader (Shaders/GlassKawaseBlur.shader).")]
            public Shader blurShader;

            [Range(1, 5)]
            [Tooltip("Number of down/up Kawase iterations. More = wider, softer blur.")]
            public int iterations = 3;

            [Range(0.5f, 6f)]
            [Tooltip("Base tap spread; UITheme.BlurStrength scales this at runtime.")]
            public float spread = 2.0f;

            [Range(1, 4)]
            [Tooltip("Initial downscale of the blur chain (2 = half res). Higher = cheaper/softer.")]
            public int downscale = 2;

            [Tooltip("When to run relative to the URP passes.")]
            public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;
        }

        [SerializeField] private Settings _settings = new Settings();

        private static readonly int GlobalBlurTexId = Shader.PropertyToID("_MLOmegaGlassBlur");
        private static readonly int OffsetId = Shader.PropertyToID("_Offset");

        private Material _material;
        private GlassBlurPass _pass;

        public override void Create()
        {
            if (_settings.blurShader == null)
            {
                _settings.blurShader = Shader.Find("MLOmega/GlassKawaseBlur");
            }
            if (_settings.blurShader != null)
            {
                _material = CoreUtils.CreateEngineMaterial(_settings.blurShader);
            }
            _pass = new GlassBlurPass(_material, _settings)
            {
                renderPassEvent = _settings.injectionPoint
            };

            // Tell LiquidGlass.shader the frosted background will be available: the
            // global keyword is on only while this feature is active on the renderer.
            if (_material != null) Shader.EnableKeyword("_HAS_BLUR_TEX");
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null) return;
            // Only for game/scene cameras; skip reflection/preview cameras.
            CameraType t = renderingData.cameraData.cameraType;
            if (t != CameraType.Game && t != CameraType.SceneView) return;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            Shader.DisableKeyword("_HAS_BLUR_TEX");
            CoreUtils.Destroy(_material);
            _material = null;
        }

        // ------------------------------------------------------------------
        //  Render pass (RenderGraph)
        // ------------------------------------------------------------------
        private sealed class GlassBlurPass : ScriptableRenderPass
        {
            private readonly Material _material;
            private readonly Settings _settings;

            public GlassBlurPass(Material material, Settings settings)
            {
                _material = material;
                _settings = settings;
                profilingSampler = new ProfilingSampler("MLOmega GlassBlur");
            }

            private class PassData
            {
                public TextureHandle source;
                public TextureHandle down;
                public TextureHandle up;
                public Material material;
                public float offset;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                TextureHandle source = resources.activeColorTexture;
                if (!source.IsValid()) return;

                int scale = Mathf.Max(1, _settings.downscale);
                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;
                desc.width = Mathf.Max(1, desc.width / scale);
                desc.height = Mathf.Max(1, desc.height / scale);

                TextureHandle down = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, desc, "_GlassBlurDown", false, FilterMode.Bilinear);
                TextureHandle up = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, desc, "_GlassBlurUp", false, FilterMode.Bilinear);

                float offset = _settings.spread;

                // Downsample pass (source -> down).
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "GlassBlur Down", out PassData data))
                {
                    data.source = source;
                    data.material = _material;
                    data.offset = offset;
                    builder.UseTexture(source);
                    builder.SetRenderAttachment(down, 0);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        d.material.SetFloat(OffsetId, d.offset);
                        Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                    });
                }

                // Upsample pass (down -> up), widening the offset per iteration,
                // then publish the result globally for LiquidGlass.shader.
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "GlassBlur Up", out PassData data))
                {
                    data.source = down;
                    data.material = _material;
                    data.offset = offset * (1 + _settings.iterations);
                    builder.UseTexture(down);
                    builder.SetRenderAttachment(up, 0);
                    builder.AllowPassCulling(false);
                    builder.SetGlobalTextureAfterPass(up, GlobalBlurTexId);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        d.material.SetFloat(OffsetId, d.offset);
                        Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 1);
                    });
                }
            }
        }
    }
}
