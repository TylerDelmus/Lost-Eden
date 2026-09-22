// Effect meshes that carry a colour per vertex (stock draws texture x diffuse, D3DTOP_MODULATE, with the
// diffuse from the vertex). HDRP/Unlit has no vertex colour, so this is HDRP/Unlit's transparent forward
// pass cut down to what the effects use: texture x _UnlitColor x vertex colour, premultiplied in the
// shader and de-exposed as ShaderPassForwardUnlit.hlsl does, blended One + [_DstBlend] (One for
// additive, OneMinusSrcAlpha for alpha). No fog, no lighting, no Z write, no culling.
Shader "Hidden/LostEden/EffectVertexColor"
{
    Properties
    {
        _UnlitColorMap ("Texture", 2D) = "white" {}
        _UnlitColor ("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        _VertexGammaScale ("Vertex Gamma Scale", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_UnlitColorMap);
            SAMPLER(sampler_UnlitColorMap);

            // Every material property in one UnityPerMaterial buffer, so the SRP Batcher can take draws
            // that don't use a MaterialPropertyBlock.
            CBUFFER_START(UnityPerMaterial)
                float4 _UnlitColorMap_ST;
                float4 _UnlitColor;
                float _DstBlend;
                float _VertexGammaScale;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            // Unity's own gamma-to-linear, the one a material colour goes through when it's set
            // (Mathf.GammaToLinearSpace): the sRGB curve below 1 and pow(v, 2.2) from 1 up. The additive
            // boost takes colours past 1, so the plain sRGB curve would not match HDRP/Unlit there.
            float3 UnityGammaToLinear(float3 c)
            {
                float3 low = c / 12.92;
                float3 mid = PositivePow((c + 0.055) / 1.055, 2.4);
                float3 high = PositivePow(c, 2.2);
                return c <= 0.04045 ? low : (c < 1.0 ? mid : high);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv = input.uv * _UnlitColorMap_ST.xy + _UnlitColorMap_ST.zw;
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Vertex colours arrive as stock's D3DCOLOR bytes, in gamma. Unity linearises a material
                // colour when it's set; the vertex colour is linearised here instead, after the gamma-space
                // interpolation D3D's Gouraud shading did. Alpha is linear in both. _VertexGammaScale is the
                // port's additive boost, applied in gamma before linearising, as it is to an HDRP/Unlit
                // colour (EffectBillboardBatch.AdditiveHdrBoost).
                float4 vertex = float4(UnityGammaToLinear(input.color.rgb * _VertexGammaScale), input.color.a);
                float4 c = SAMPLE_TEXTURE2D(_UnlitColorMap, sampler_UnlitColorMap, input.uv) * _UnlitColor * vertex;
                return float4(c.rgb * _DeExposureMultiplier * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
