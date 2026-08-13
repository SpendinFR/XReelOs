Shader "MLOmega/XReel Web VR"
{
    Properties
    {
        _MainTex ("Web video", 2D) = "black" {}
        _SourceRect ("Source rect", Vector) = (0, 0, 1, 1)
        _Projection ("Projection", Float) = 0
        _StereoLayout ("Stereo layout", Float) = 0
        _Zoom ("Projection zoom", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Geometry-20" "RenderType"="Opaque" }
        Cull Front
        ZWrite Off
        ZTest LEqual
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _SourceRect;
            float _Projection;
            float _StereoLayout;
            float _Zoom;

            static const float PI = 3.14159265358979323846;
            static const float HALF_PI = 1.57079632679489661923;
            static const float TWO_PI = 6.28318530717958647692;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.direction = normalize(input.vertex.xyz);
                return output;
            }

            float2 mapVr180(float3 ray)
            {
                // A Unity Transform's forward axis is local +Z. The dome is
                // recentered with that axis aligned to the XR camera, so the
                // middle of a VR180 source must also map to +Z. Using -Z here
                // puts the complete hemisphere exactly behind the wearer.
                float yaw = atan2(ray.x, ray.z);
                float pitch = asin(clamp(ray.y, -1.0, 1.0));
                if (abs(yaw) > HALF_PI) return float2(-1.0, -1.0);
                // TLab's Texture2D is already upright in Unity (unlike the
                // vertically flipped Android SurfaceTexture used by vr2xr).
                return float2((yaw / PI) + 0.5, 0.5 + (pitch / PI));
            }

            float2 mapVr360(float3 ray)
            {
                float yaw = atan2(ray.x, ray.z);
                float pitch = asin(clamp(ray.y, -1.0, 1.0));
                return float2(frac((yaw / TWO_PI) + 0.5), 0.5 + (pitch / PI));
            }

            float2 mapDualFisheye(float3 ray, float sourceEye)
            {
                float theta = acos(clamp(ray.z, -1.0, 1.0));
                float phi = atan2(ray.y, ray.x);
                float radius = theta / HALF_PI;
                if (radius > 1.0) return float2(-1.0, -1.0);
                float2 position = float2(cos(phi), sin(phi)) * radius;
                return float2(
                    (sourceEye < 0.5 ? 0.25 : 0.75) + position.x * 0.25,
                    0.5 + position.y * 0.5);
            }

            float2 applyStereoLayout(float2 uv, float sourceEye)
            {
                if (_StereoLayout < 0.5)
                {
                    uv.x = uv.x * 0.5 + sourceEye * 0.5;
                }
                else if (_StereoLayout < 1.5)
                {
                    uv.x = uv.x * 0.5 + (1.0 - sourceEye) * 0.5;
                }
                else if (_StereoLayout < 2.5)
                {
                    uv.y = uv.y * 0.5 + (1.0 - sourceEye) * 0.5;
                }
                else
                {
                    uv.y = uv.y * 0.5 + sourceEye * 0.5;
                }
                return uv;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float sourceEye = unity_StereoEyeIndex > 0 ? 1.0 : 0.0;
                // Zoom is projection-only: source resolution and stereo
                // separation remain untouched. Values below 1 widen the field
                // of view (zoom out), values above 1 magnify it.
                float3 ray = normalize(float3(
                    input.direction.x / max(0.01, _Zoom),
                    input.direction.y / max(0.01, _Zoom),
                    input.direction.z));
                float2 uv;

                if (_Projection < 0.5)
                {
                    uv = mapVr180(ray);
                    uv = applyStereoLayout(uv, sourceEye);
                }
                else if (_Projection < 1.5)
                {
                    uv = mapVr360(ray);
                    uv = applyStereoLayout(uv, sourceEye);
                }
                else if (_Projection < 2.5)
                {
                    uv = mapVr360(ray);
                }
                else
                {
                    float fisheyeEye =
                        (_StereoLayout > 0.5 && _StereoLayout < 1.5)
                            ? 1.0 - sourceEye
                            : sourceEye;
                    uv = mapDualFisheye(ray, fisheyeEye);
                }

                if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)
                    return fixed4(0.0, 0.0, 0.0, 1.0);

                uv = _SourceRect.xy + saturate(uv) * _SourceRect.zw;
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
    Fallback Off
}
