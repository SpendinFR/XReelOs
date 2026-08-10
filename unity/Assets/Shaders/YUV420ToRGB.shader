// MLOmega V19 — E22 / Gate G1
// Converts XREAL Eye YUV_420_888 (three planes) to RGB in a single blit.
// GetYUVFormatTextures() returns {Y, U, V}; U and V are half-resolution and are
// sampled with the same normalized UV. BT.601 limited-range coefficients.
Shader "Hidden/MLOmega/YUV420ToRGB"
{
    Properties
    {
        _YTex ("Y", 2D) = "black" {}
        _UTex ("U", 2D) = "gray" {}
        _VTex ("V", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            // Keep this pass byte-for-byte compatible with the XREAL SDK's
            // CaptureBackgroundYUV shader.  The Eye planes are Alpha8, and the
            // device-facing conversion is deliberately BGR ordered.
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _YTex;
            sampler2D _UTex;
            sampler2D _VTex;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 ycol = tex2D(_YTex, i.uv);
                fixed4 ucol = tex2D(_UTex, i.uv);
                fixed4 vcol = tex2D(_VTex, i.uv);

                float r = ycol.a + 1.4022 * vcol.a - 0.7011;
                float g = ycol.a - 0.3456 * ucol.a - 0.7145 * vcol.a + 0.53005;
                float b = ycol.a + 1.771 * ucol.a - 0.8855;

                fixed4 col = fixed4(b, g, r, 1);
                col.rgb = GammaToLinearSpace(col.rgb);
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
