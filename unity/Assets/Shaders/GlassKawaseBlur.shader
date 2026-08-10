// MLOmega V19 — E25
// Kawase dual-filter blur used by GlassBlurFeature to produce the frosted
// background sampled by LiquidGlass.shader. Kawase is chosen over a Gaussian
// because it reaches a wide, smooth blur in very few passes (each pass reads 4
// bilinear taps at growing offsets), which matters on a mobile XR GPU where the
// blur runs every frame (ADR §E25).
//
// Pass 0 : downsample + blur (used going down the mip chain).
// Pass 1 : upsample + blur (used coming back up).
// The feature ping-pongs between half/quarter-res targets; _Offset scales the tap
// spread per pass and _BlurStrength maps UITheme's blur amount to an offset gain.
Shader "MLOmega/GlassKawaseBlur"
{
    Properties
    {
        _BlitTexture ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float  _Offset;

        // Kawase downsample: centre + 4 diagonal taps at _Offset spread.
        half4 DownsampleFrag (Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 t = _BlitTexture_TexelSize.xy * _Offset;

            half4 sum = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 4.0;
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x, -t.y));
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( t.x, -t.y));
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x,  t.y));
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( t.x,  t.y));
            return sum / 8.0;
        }

        // Kawase upsample: 8 taps in a ring (diamond + cross) at _Offset spread.
        half4 UpsampleFrag (Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 t = _BlitTexture_TexelSize.xy * _Offset;

            half4 sum = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x * 2.0, 0.0));
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x,  t.y)) * 2.0;
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( 0.0,  t.y * 2.0));
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( t.x,  t.y)) * 2.0;
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( t.x * 2.0, 0.0));
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( t.x, -t.y)) * 2.0;
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( 0.0, -t.y * 2.0));
            sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x, -t.y)) * 2.0;
            return sum / 12.0;
        }
        ENDHLSL

        Pass
        {
            Name "KawaseDownsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DownsampleFrag
            ENDHLSL
        }

        Pass
        {
            Name "KawaseUpsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment UpsampleFrag
            ENDHLSL
        }
    }
}
