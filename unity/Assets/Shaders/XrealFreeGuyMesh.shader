Shader "MLOmega/XREAL FreeGuy Mesh"
{
    Properties
    {
        _BaseColor ("Base", Color) = (0.05, 0.82, 1.0, 0.055)
        _BaseMap ("Imported GLB texture", 2D) = "white" {}
        _GridColor ("Grid", Color) = (0.35, 1.0, 0.8, 0.22)
        _GridScale ("Grid scale", Float) = 2.4
        _ScanSpeed ("Scan speed", Float) = 0.55
        [HDR] _EmissionColor ("Imported emission", Color) = (0.3, 1.0, 0.85, 1.0)
        _EmissionStrength ("Emission strength", Range(0, 8)) = 2.6
        _GlowWidth ("Optical glow width", Range(0, 0.04)) = 0.008
        _GlowStrength ("Optical glow strength", Range(0, 4)) = 0.75
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+5"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Name "FreeGuyWorld"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _BaseColor;
            half4 _GridColor;
            half4 _EmissionColor;
            float _GridScale;
            float _ScanSpeed;
            float _EmissionStrength;
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 gridUVW = abs(frac(input.positionWS * _GridScale) - 0.5);
                float grid = 1.0 - smoothstep(0.44, 0.49, min(min(gridUVW.x, gridUVW.y), gridUVW.z));
                float scan = pow(saturate(1.0 - abs(frac(
                    input.positionWS.y * 0.28 - _Time.y * _ScanSpeed) - 0.5) * 2.0), 16.0);
                float fresnel = pow(1.0 - saturate(dot(
                    normalize(input.normalWS),
                    normalize(_WorldSpaceCameraPos - input.positionWS))), 2.0);
                half4 imported = SAMPLE_TEXTURE2D(
                    _BaseMap, sampler_BaseMap, input.uv);
                half4 baseColor = _BaseColor * imported;
                half4 color = lerp(baseColor, _GridColor, saturate(
                    grid * 0.35 + scan + fresnel * 0.45));
                color.rgb += imported.rgb * _EmissionColor.rgb *
                    _EmissionStrength *
                    (0.18 + grid * 0.12 + scan * 0.22 + fresnel * 0.18);
                color.a = saturate(
                    baseColor.a + grid * 0.05 + scan * 0.16 + fresnel * 0.08);
                return color;
            }
            ENDHLSL
        }
    }

    // XREAL SDK 3.x currently builds the product on Unity's classic render
    // pipeline. This fallback is intentionally self-contained: it gives every
    // runtime-imported GLB its transparent hologram shading and an additive,
    // geometry-backed halo without requiring a URP renderer asset or a per-asset
    // material rebuild.
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+5"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "FreeGuyWorldBuiltin"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            fixed4 _BaseColor;
            fixed4 _GridColor;
            fixed4 _EmissionColor;
            float _GridScale;
            float _ScanSpeed;
            float _EmissionStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition =
                    mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal =
                    UnityObjectToWorldNormal(input.normal);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 gridUVW =
                    abs(frac(input.worldPosition * _GridScale) - 0.5);
                float grid = 1.0 - smoothstep(
                    0.44,
                    0.49,
                    min(min(gridUVW.x, gridUVW.y), gridUVW.z));
                float scan = pow(saturate(
                    1.0 - abs(frac(
                        input.worldPosition.y * 0.28 -
                        _Time.y * _ScanSpeed) - 0.5) * 2.0), 16.0);
                float fresnel = pow(
                    1.0 - saturate(dot(
                        normalize(input.worldNormal),
                        normalize(_WorldSpaceCameraPos -
                            input.worldPosition))),
                    2.0);
                fixed4 imported = tex2D(_BaseMap, input.uv);
                fixed4 baseColor = _BaseColor * imported;
                fixed4 color = lerp(
                    baseColor,
                    _GridColor,
                    saturate(grid * 0.35 + scan + fresnel * 0.45));
                color.rgb += imported.rgb * _EmissionColor.rgb *
                    _EmissionStrength *
                    (0.18 + grid * 0.12 + scan * 0.22 + fresnel * 0.18);
                color.a = saturate(
                    baseColor.a + grid * 0.05 +
                    scan * 0.16 + fresnel * 0.08);
                return color;
            }
            ENDCG
        }

        Pass
        {
            Name "FreeGuyOpticalGlow"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Front

            CGPROGRAM
            #pragma vertex glowVert
            #pragma fragment glowFrag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _EmissionColor;
            float _GlowWidth;
            float _GlowStrength;
            float _ScanSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f glowVert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 expanded = input.vertex;
                expanded.xyz += normalize(input.normal) * _GlowWidth;
                output.position = UnityObjectToClipPos(expanded);
                output.worldPosition =
                    mul(unity_ObjectToWorld, expanded).xyz;
                output.worldNormal =
                    UnityObjectToWorldNormal(input.normal);
                return output;
            }

            fixed4 glowFrag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float fresnel = pow(
                    1.0 - saturate(dot(
                        normalize(input.worldNormal),
                        normalize(_WorldSpaceCameraPos -
                            input.worldPosition))),
                    2.0);
                float scan = 0.65 + 0.35 * sin(
                    input.worldPosition.y * 24.0 -
                    _Time.y * _ScanSpeed * 5.0);
                float strength =
                    _GlowStrength * (0.18 + fresnel * 0.82) * scan;
                return fixed4(_EmissionColor.rgb * strength, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
