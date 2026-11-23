Shader "KMB/Minotos Area Slam Telegraph"
{
    Properties
    {
        _Fill ("Fill", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Fill;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float radius = length(input.uv * 2.0 - 1.0);
                float edge = max(fwidth(radius), 0.001);
                float circle = 1.0 - smoothstep(1.0 - edge, 1.0, radius);
                float ring = smoothstep(0.96 - edge, 0.96 + edge, radius);
                float fill = 1.0 - smoothstep(_Fill - edge, _Fill + edge, radius);
                half alpha = circle * lerp(0.12 + 0.45 * fill, 0.88, ring);
                return half4(1.0, 0.035, 0.018, alpha);
            }
            ENDHLSL
        }
    }
}
