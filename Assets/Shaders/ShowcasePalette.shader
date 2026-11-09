// Full-screen 4-colour palette filter (Game Boy style by default) for URP's Full Screen Pass
// renderer feature. Maps luminance to four colours with optional ordered dithering.
Shader "Hidden/Showcase/Palette4"
{
    Properties
    {
        _Color0 ("Darkest", Color) = (0.06, 0.22, 0.06, 1)
        _Color1 ("Dark", Color) = (0.19, 0.38, 0.19, 1)
        _Color2 ("Light", Color) = (0.55, 0.67, 0.06, 1)
        _Color3 ("Lightest", Color) = (0.61, 0.74, 0.06, 1)
        _Strength ("Strength", Range(0, 1)) = 1
        _Dither ("Dither", Range(0, 1)) = 0.5
        _Contrast ("Contrast", Range(0.5, 2)) = 1.25
        _Gamma ("Gamma (higher = darker mids)", Range(0.5, 4)) = 2.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "Palette4"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _Color0, _Color1, _Color2, _Color3;
            float _Strength, _Dither, _Contrast, _Gamma;

            static const float kBayer[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                // Quantise in perceptual (sRGB-ish) space so the four bands are evenly spread.
                float luma = dot(LinearToSRGB(saturate(src.rgb)), float3(0.299, 0.587, 0.114));
                // Toon-shaded scenes are mostly bright; the gamma curve spreads them over all four bands.
                luma = pow(saturate(luma), _Gamma);
                luma = saturate((luma - 0.5) * _Contrast + 0.5);

                uint2 p = uint2(input.positionCS.xy) % 4;
                float threshold = (kBayer[p.y * 4 + p.x] / 16.0 - 0.5) * _Dither * 0.33;
                int band = (int)floor(saturate(luma + threshold) * 3.999);

                float3 pal = band == 0 ? _Color0.rgb : band == 1 ? _Color1.rgb : band == 2 ? _Color2.rgb : _Color3.rgb;
                return float4(lerp(src.rgb, pal, _Strength), src.a);
            }
            ENDHLSL
        }
    }
}
