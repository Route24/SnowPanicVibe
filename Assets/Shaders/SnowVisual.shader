Shader "SnowVisual/SoftSnow"
{
    Properties
    {
        _BaseColor ("Snow Color", Color) = (0.95, 0.97, 1, 1)
        _BlueTint ("Blue Tint", Range(0, 0.3)) = 0.08
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
        _Softness ("Lighting Softness", Range(0.5, 2)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _BlueTint;
            float _Smoothness;
            float _Softness;
            CBUFFER_END

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
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 N = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 L = mainLight.direction;
                float NdotL = saturate(dot(N, L));
                float soft = pow(NdotL, _Softness);
                float3 ambient = half3(0.4, 0.45, 0.55);
                float3 diffuse = mainLight.color * mainLight.shadowAttenuation * soft;
                float3 finalLight = saturate(ambient + diffuse);
                float3 snowColor = _BaseColor.rgb + float3(0, 0, _BlueTint);
                return half4(snowColor * finalLight, 1);
            }
            ENDHLSL
        }

    }
    FallBack "Universal Render Pipeline/Lit"
}
