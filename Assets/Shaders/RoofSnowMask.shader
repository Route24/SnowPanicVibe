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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
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
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetCameraPositionWS() - output.positionWS;
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
                float3 N = normalize(input.normalWS);
                float3 V = normalize(input.viewDirWS);
                float3 L = 0;
                float atten = 1;
                #ifdef _MAIN_LIGHT_SHADOWS
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                L = mainLight.direction;
                atten = mainLight.shadowAttenuation;
                #else
                L = normalize(float3(0.3, 0.6, 0.2));
                #endif
                float NdotL = saturate(dot(N, L));
                float soft = pow(NdotL, 1.2);
                float3 ambient = half3(0.42, 0.46, 0.56);
                float3 diffuse = atten * soft;
                float3 finalLight = saturate(ambient + diffuse * 0.6);
                float topBoost = saturate(dot(N, float3(0, 1, 0))) * 0.05;
                float edgeDarken = 1.0 - (1.0 - saturate(dot(N, V))) * 0.1;
                finalLight = saturate(finalLight * edgeDarken + topBoost);
                float3 snowColor = _BaseColor.rgb + float3(0, 0, 0.06);
                return half4(snowColor * finalLight, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
