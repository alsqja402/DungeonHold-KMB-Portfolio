Shader "KMB/Installation Preview Outline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.1, 0.67, 1, 1)
        _OutlineWidth ("Outline Width", Float) = 0.025
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZTest LEqual
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                return TransformWorldToHClip(positionWS + normalWS * _OutlineWidth);
            }

            half4 Frag() : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
