Shader "Game/VisionFog"
{
    Properties
    {
        _VisionMask ("Vision Mask", 2D) = "white" {}
        _FogColor ("Fog Color", Color) = (0.015, 0.025, 0.045, 0.82)
        _VisionArea ("Area (originXZ, 1/size)", Vector) = (0, 0, 0.025, 0.025)
    }

    SubShader
    {
        // 모든 불투명/반투명 뒤에 그린다. 깊이는 읽기만 하고 쓰지 않는다.
        Tags { "RenderType" = "Overlay" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VisionFog"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_VisionMask);
            SAMPLER(sampler_VisionMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _VisionMask_ST;
                float4 _VisionArea;
                float4 _FogColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.screenPos.xy / IN.screenPos.w;

                // 깊이에서 이 픽셀이 실제로 월드 어디인지 복원한다. 그래서 바닥뿐 아니라
                // 벽과 좀비도 자기가 서 있는 칸의 밝기로 어두워진다.
                float depth = SampleSceneDepth(uv);
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                float2 maskUv = (world.xz - _VisionArea.xy) * _VisionArea.zw;
                float inside = step(0.0, maskUv.x) * step(maskUv.x, 1.0)
                             * step(0.0, maskUv.y) * step(maskUv.y, 1.0);

                float visible = SAMPLE_TEXTURE2D(_VisionMask, sampler_VisionMask, saturate(maskUv)).r * inside;

                return half4(_FogColor.rgb, _FogColor.a * (1.0 - visible));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
