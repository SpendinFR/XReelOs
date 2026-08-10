// MLOmega V19 — E25
// Liquid-glass material for every world-space UI panel (design system §13.1,
// handoff Lot 3). Renders a translucent, rounded, rim-lit panel whose interior
// samples a pre-blurred copy of the scene behind it (the "frosted" look), with a
// subtle animated film grain. All look parameters are driven from UITheme via the
// component's MaterialPropertyBlock — nothing is hard-coded in the shader.
//
// Background blur: the actual Kawase blur is produced once per frame by
// GlassBlurFeature (a URP ScriptableRendererFeature) into the global texture
// _MLOmegaGlassBlur. This shader samples that texture in screen space, so the
// blur cost is paid once regardless of how many panels are on screen. When the
// feature is absent (blur disabled, or the RendererData has no feature), the
// _HasBlurTex keyword is off and the shader falls back to a flat translucent tint
// + rim + grain — still a valid glass look, never a hard error (ADR §E25).
Shader "MLOmega/LiquidGlass"
{
    Properties
    {
        [MainColor] _PanelTint    ("Panel tint (rgb, a=opacity)", Color) = (0.10, 0.13, 0.20, 0.42)
        _RimColor                 ("Rim colour",                 Color) = (0.55, 0.80, 1.00, 0.85)
        _AccentColor              ("Truth accent (rim tint)",    Color) = (0.55, 0.80, 1.00, 1.00)
        _BlurStrength             ("Blur mix (0..1)",            Range(0,1)) = 0.6
        _Grain                    ("Grain amount",               Range(0,1)) = 0.06
        _RimWidth                 ("Rim width",                  Range(0,4)) = 1.4
        _CornerRadius             ("Corner radius (px of unit)", Range(0,0.5)) = 0.12
        _AccentMix                ("Accent into rim (0..1)",     Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LiquidGlass"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Global keyword: enabled by GlassBlurFeature while the frosted
            // background texture is being produced; off => flat-translucent fallback.
            #pragma multi_compile _ _HAS_BLUR_TEX

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                float4 color       : COLOR;
            };

            // Global blur target written by GlassBlurFeature.
            TEXTURE2D_X(_MLOmegaGlassBlur);
            SAMPLER(sampler_MLOmegaGlassBlur);

            CBUFFER_START(UnityPerMaterial)
                float4 _PanelTint;
                float4 _RimColor;
                float4 _AccentColor;
                float  _BlurStrength;
                float  _Grain;
                float  _RimWidth;
                float  _CornerRadius;
                float  _AccentMix;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(pos.positionCS);
                OUT.color = IN.color;
                return OUT;
            }

            // Signed distance to a rounded box centred on the quad (uv in 0..1).
            // Returns the distance in uv units; negative inside.
            float RoundedBoxSDF(float2 uv, float radius)
            {
                float2 p = abs(uv - 0.5) - (0.5 - radius);
                float2 q = max(p, 0.0);
                return length(q) + min(max(p.x, p.y), 0.0) - radius;
            }

            // Cheap hash-based grain, animated by _TimeParameters so it shimmers
            // very slightly rather than being a static dither pattern.
            float GrainNoise(float2 uv)
            {
                float2 seed = uv * float2(443.897, 397.297) + _TimeParameters.x * 13.0;
                float n = frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453);
                return n - 0.5;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float radius = max(_CornerRadius, 1e-4);
                float sdf = RoundedBoxSDF(IN.uv, radius);

                // Antialiased rounded-rect mask via fwidth of the SDF.
                float aa = fwidth(sdf) + 1e-5;
                float insideMask = 1.0 - smoothstep(-aa, aa, sdf);
                if (insideMask <= 0.0)
                {
                    discard;
                }

                // Base translucent body.
                half3 body = _PanelTint.rgb;
                half bodyAlpha = _PanelTint.a;

                // Frosted background: blend the pre-blurred scene behind the panel.
            #if defined(_HAS_BLUR_TEX)
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                half3 blurred = SAMPLE_TEXTURE2D_X(
                    _MLOmegaGlassBlur, sampler_MLOmegaGlassBlur, screenUV).rgb;
                // Tint the frosted scene toward the panel colour so it reads as glass.
                body = lerp(body, blurred * 0.85 + body * 0.15, _BlurStrength);
                bodyAlpha = lerp(bodyAlpha, saturate(bodyAlpha + _BlurStrength * 0.35), _BlurStrength);
            #endif

                // Luminous rim: a soft band just inside the edge, tinted by the
                // truth-level accent so the border encodes the truth level.
                float rimBand = 1.0 - smoothstep(0.0, _RimWidth * 0.03 + 1e-4, -sdf);
                float rimInner = smoothstep(-_RimWidth * 0.045 - 1e-4, 0.0, -sdf);
                float rim = saturate(rimBand * rimInner);
                half3 rimCol = lerp(_RimColor.rgb, _AccentColor.rgb, _AccentMix);
                half3 col = body + rimCol * rim * _RimColor.a;

                // Film grain over the body (kept subtle, spec Lot 3 "grain léger").
                col += GrainNoise(IN.uv) * _Grain;

                // Vertex colour carries the component's animated fade/scale alpha.
                half outAlpha = saturate(bodyAlpha + rim * _RimColor.a) * insideMask * IN.color.a;
                col *= IN.color.rgb;

                return half4(col, outAlpha);
            }
            ENDHLSL
        }
    }

    // Official XREAL 3.1 template profile: Unity Built-in pipeline. This pass
    // deliberately keeps the optical panel translucent and self-contained;
    // background blur is disabled because the glasses compositor already shows
    // the real world behind the alpha surface.
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LiquidGlassBuiltin"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _PanelTint;
            fixed4 _RimColor;
            fixed4 _AccentColor;
            float _Grain;
            float _RimWidth;
            float _CornerRadius;
            float _AccentMix;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float RoundedBoxSDF(float2 uv, float radius)
            {
                float2 p = abs(uv - 0.5) - (0.5 - radius);
                float2 q = max(p, 0.0);
                return length(q) +
                    min(max(p.x, p.y), 0.0) - radius;
            }

            float GrainNoise(float2 uv)
            {
                float2 seed =
                    uv * float2(443.897, 397.297) + _Time.y * 13.0;
                return frac(
                    sin(dot(seed, float2(12.9898, 78.233))) *
                    43758.5453) - 0.5;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float radius = max(_CornerRadius, 1e-4);
                float sdf = RoundedBoxSDF(input.uv, radius);
                float aa = fwidth(sdf) + 1e-5;
                float insideMask =
                    1.0 - smoothstep(-aa, aa, sdf);
                clip(insideMask - 1e-4);

                float rimBand = 1.0 - smoothstep(
                    0.0,
                    _RimWidth * 0.03 + 1e-4,
                    -sdf);
                float rimInner = smoothstep(
                    -_RimWidth * 0.045 - 1e-4,
                    0.0,
                    -sdf);
                float rim = saturate(rimBand * rimInner);
                fixed3 rimColor = lerp(
                    _RimColor.rgb,
                    _AccentColor.rgb,
                    _AccentMix);
                fixed3 color =
                    _PanelTint.rgb +
                    rimColor * rim * _RimColor.a +
                    GrainNoise(input.uv) * _Grain;
                fixed alpha = saturate(
                    _PanelTint.a + rim * _RimColor.a) *
                    insideMask * input.color.a;
                return fixed4(color * input.color.rgb, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
