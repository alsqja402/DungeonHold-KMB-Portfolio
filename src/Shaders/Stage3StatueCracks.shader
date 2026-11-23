Shader "KMB/Stage 3 Statue Cracks"
{
    Properties
    {
        [HDR] _CrackColor("Crack Color", Color) = (1, 0.1, 0.025, 1)
        _CrackProgress("Crack Progress", Range(0, 1)) = 0
        [HideInInspector] _LightRayMode("Light Ray Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4x4 _MeshToStatue;
                float4 _StatueBounds;
                half4 _CrackColor;
                float _CrackProgress;
                float _LightRayMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 statuePosition : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.statuePosition = mul(_MeshToStatue, input.positionOS).xyz;
                output.uv = input.uv;
                return output;
            }

            float3 HashCell(float3 cell)
            {
                float3 hash = frac(cell * float3(0.1031, 0.1030, 0.0973));
                hash += dot(hash, hash.yxz + 33.33);
                return frac((hash.xxy + hash.yxx) * hash.zyx);
            }

            float DistanceToCrack(float3 samplePosition)
            {
                float3 cell = floor(samplePosition);
                float3 cellPosition = frac(samplePosition);
                float nearest = 10.0;
                float secondNearest = 10.0;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 neighbor = float3(x, y, z);
                    float3 seed = neighbor + lerp(0.18, 0.82, HashCell(cell + neighbor));
                    float3 offset = seed - cellPosition;
                    float distanceSquared = dot(offset, offset);
                    secondNearest = min(secondNearest, max(nearest, distanceSquared));
                    nearest = min(nearest, distanceSquared);
                }
                return sqrt(secondNearest) - sqrt(nearest);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_LightRayMode > 0.5)
                {
                    float center = 1.0 - saturate(abs(input.uv.y * 2.0 - 1.0));
                    float core = pow(center, 3.25);
                    float halo = center * center * 0.24;
                    float lengthFade = 1.0 - smoothstep(0.28, 1.0, input.uv.x);
                    return half4(_CrackColor.rgb * (core + halo) * lengthFade, 0);
                }

                float3 position = (input.statuePosition - _StatueBounds.xyz) / max(_StatueBounds.w, 0.001);

                // 밑에서 올라오는 균열에 위치별 시차를 줘 석상 여러 곳으로 번지게 합니다.
                float height = saturate(position.y);
                float spread = height * 0.58 + (0.5 + 0.5 * sin(dot(position, float3(12.0, -7.0, 15.0)))) * 0.18;
                float revealed = smoothstep(spread, spread + 0.14, _CrackProgress);
                float3 warped = position * 10.0 + sin(position.zxy * 66.0) * 0.10;
                float edge = DistanceToCrack(warped);
                float width = lerp(0.022, 0.055, revealed);
                float aliasWidth = min(fwidth(edge), 0.06);
                float core = 1.0 - smoothstep(width, width + aliasWidth + 0.01, edge);
                float glow = 1.0 - smoothstep(width, width + 0.16, edge);
                return half4(_CrackColor.rgb * (core + glow * 0.24) * revealed, 0);
            }
            ENDHLSL
        }
    }
}
