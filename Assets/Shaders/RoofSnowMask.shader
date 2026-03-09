Shader "SnowPanic/RoofSnowMask"
{
    Properties
    {
        _BaseColor ("Snow Color", Color) = (0.92, 0.95, 1, 1)
        _SnowMask ("Snow Mask (R=1 snow, R=0 hole)", 2D) = "white" {}
        _ClipThreshold ("Clip threshold (mask < this = hole)", Range(0,1)) = 0.5
        _SnowIntensity ("Snow intensity (0=hidden, 1=full). Driven by packedTotal.", Range(0,1)) = 1
        [Header(Roof UV mapping)]
        _RoofCenter ("Roof Center", Vector) = (0,0,0,0)
        _RoofR ("Roof R (right)", Vector) = (1,0,0,0)
        _RoofF ("Roof F (forward)", Vector) = (0,0,1,0)
        _RoofWidth ("Roof Width", Float) = 2
        _RoofLength ("Roof Length", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _RoofCenter;
            float4 _RoofR;
            float4 _RoofF;
            float _RoofWidth;
            float _RoofLength;
            float _ClipThreshold;
            float _SnowIntensity;
            CBUFFER_END
            TEXTURE2D(_SnowMask);
            SAMPLER(sampler_SnowMask);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 d = input.positionWS - _RoofCenter.xyz;
                float u = dot(d, _RoofR.xyz) / max(0.01, _RoofWidth) + 0.5;
                float v = dot(d, _RoofF.xyz) / max(0.01, _RoofLength) + 0.5;
                float2 maskUV = float2(u, v);
                float mask = SAMPLE_TEXTURE2D(_SnowMask, sampler_SnowMask, maskUV).r * _SnowIntensity;
                clip(mask - _ClipThreshold);
                return half4(_BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
