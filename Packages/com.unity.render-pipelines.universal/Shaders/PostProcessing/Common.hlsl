#ifndef UNIVERSAL_POSTPROCESSING_COMMON_INCLUDED
#define UNIVERSAL_POSTPROCESSING_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

#if _FXAA
// Notes on FXAA:
// * We now rely on the official FXAA implementation (authored by Timothy Lottes while at NVIDIA)
//   with minimal changes made by Unity to integrate with URP.
// * The following 'Tweakable' defines are used by the FXAA implementation and can be changed if desired:
//   * FXAA_PC set to 1 is the highest quality implementation ("PC" here is a misnomer, it will run on all platforms).
//   * FXAA_PC set to 0 is the cheaper 'FXAA_PC_CONSOLE' variant
//     (it's equivalent to URP's old implementation but less noisy and should run faster than before)
//   * FXAA_GREEN_AS_LUMA can be set to 0 for an extra performance increase but will only antialias edges that have
//     some green in them (will be visually equivalent on the vast majority of scenes).
//   * FXAA_QUALITY__PRESET is used when FXAA_PC is set ot 1. We chose preset 12 as it runs almost as fast on Switch as
//     our old noisy implementation did.
//     On all other platforms we could basically get away with preset 15 which has slightly better edge quality.

// Tweakable params (can be changed to get different performance and quality tradeoffs)
#if (SHADER_API_PS5 || SHADER_API_SWITCH2) && defined(HDR_INPUT)
// The console implementation does not generate artefacts when the input pixels are in nits (monitor HDR range).
#define FXAA_PC 0
#else
#define FXAA_PC 1
#endif
#define FXAA_GREEN_AS_LUMA 0
#define FXAA_QUALITY__PRESET 12

// Fixed params (should not be changed)
#define FXAA_HLSL_5 1
#define FXAA_GATHER4_ALPHA 0
#define FXAA_PC_CONSOLE !FXAA_PC

#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/FXAA3_11.hlsl"
#endif

// ----------------------------------------------------------------------------------
// Utility functions

half GetLuminance(half3 colorLinear)
{
#if _TONEMAP_ACES
    return AcesLuminance(colorLinear);
#else
    return Luminance(colorLinear);
#endif
}

real3 GetSRGBToLinear(real3 c)
{
#if _USE_FAST_SRGB_LINEAR_CONVERSION
    return FastSRGBToLinear(c);
#else
    return SRGBToLinear(c);
#endif
}

real4 GetSRGBToLinear(real4 c)
{
#if _USE_FAST_SRGB_LINEAR_CONVERSION
    return FastSRGBToLinear(c);
#else
    return SRGBToLinear(c);
#endif
}

real3 GetLinearToSRGB(real3 c)
{
#if _USE_FAST_SRGB_LINEAR_CONVERSION
    return FastLinearToSRGB(c);
#else
    return LinearToSRGB(c);
#endif
}

real4 GetLinearToSRGB(real4 c)
{
#if _USE_FAST_SRGB_LINEAR_CONVERSION
    return FastLinearToSRGB(c);
#else
    return LinearToSRGB(c);
#endif
}

// ----------------------------------------------------------------------------------
// Shared functions for uber & fast path (on-tile)
// These should only process an input color, don't sample in neighbor pixels!

// dist: offset from the vignette center, scaled by the intensity and the roundness
half3 ApplyVignette(half3 input, half2 dist, half smoothness, half3 color)
{
    half vfactor = pow(saturate(1.0 - dot(dist, dist)), smoothness);
    return input * lerp(color, (1.0).xxx, vfactor);
}

half3 ApplyVignette(half3 input, float2 uv, float2 center, half intensity, half2 roundness, half smoothness, half3 color)
{
    center = UnityStereoTransformScreenSpaceTex(center);
    // Only the uv delta needs full precision
    half2 dist = half2(uv - center) * (intensity * roundness);
    return ApplyVignette(input, dist, smoothness, color);
}

half3 ApplyTonemap(half3 input)
{
#if _TONEMAP_ACES
    float3 aces = unity_to_ACES(input);
    input = AcesTonemap(aces);
#elif _TONEMAP_NEUTRAL
    input = NeutralTonemap(input);
#endif

    return saturate(input);
}

// Half precision LinearToLogC() for the per-pixel LUT lookup, with the exposure folded into the scale.
// The clamp keeps the result in [0;1] (the LogC range ends at ~58.85) and absorbs a half overflow of the scaled input.
half3 LinearToLogCLutSpace(half3 x, half exposure)
{
    const half logCMax = 327.0; // LogC.a * 58.85 + LogC.b
    return LogC.c * log10(clamp(x * (exposure * LogC.a) + LogC.b, LogC.b, logCMax)) + LogC.d;
}

// Half precision ApplyLut2D(), split so that the caller samples the LUT (as a half texture).
// scaleOffset = (1 / lut_width, 1 / lut_height, lut_height - 1)
// Returns the uv in the first slice, the second one is at uv + float2(scaleOffset.y, 0).
float2 GetLut2DUV(half3 uvw, float3 scaleOffset, out half sliceLerp)
{
    half slice = uvw.z * half(scaleOffset.z);
    half shift = floor(slice);
    sliceLerp = slice - shift;

    // The strip is too wide to be addressed in half precision
    float2 uv = float2(uvw.xy) * (scaleOffset.z * scaleOffset.xy) + scaleOffset.xy * 0.5;
    uv.x += shift * scaleOffset.y;
    return uv;
}

// grain in range [0;1] with neutral at 0.5
half3 ApplyGrain(half3 input, half grain, half intensity, half response, half oneOverPaperWhite)
{
    // Remap [-1;1]
    grain = (grain - 0.5) * 2.0;

    // Noisiness response curve based on scene luminance
    half lum = Luminance(input);
    #ifdef HDR_INPUT
    lum *= oneOverPaperWhite;
    #endif
    lum = 1.0 - sqrt(lum);
    lum = lerp(1.0, lum, response);

    return input + input * (grain * intensity * lum);
}

half3 ApplyGrain(half3 input, float2 uv, TEXTURE2D_PARAM(GrainTexture, GrainSampler), float intensity, float response, float2 scale, float2 offset, float oneOverPaperWhite)
{
    half grain = SAMPLE_TEXTURE2D(GrainTexture, GrainSampler, uv * scale + offset).w;
    return ApplyGrain(input, grain, intensity, response, oneOverPaperWhite);
}

// noise in range [0;1], uniformly distributed
half3 ApplyDithering(half3 input, half noise, half paperWhite, half oneOverPaperWhite)
{
    // Symmetric triangular distribution on [-1,1] with maximal density at 0
    noise = noise * 2.0 - 1.0;
    half noiseSign = noise >= 0.0 ? 1.0 : -1.0;
    noise = noiseSign * (1.0 - sqrt(1.0 - abs(noise))) * (1.0 / 255.0);

#if UNITY_COLORSPACE_GAMMA
    input += noise;
#else
    // The noise is applied in gamma 2.0 as a fast stand-in for sRGB: a sqrt and a mul instead of two pow() per channel,
    // and unlike the "fast" sRGB conversions it doesn't clamp the HDR values.
  #if defined(HDR_INPUT)
    // (sqrt(input / paperWhite) + noise)^2 * paperWhite
    noise *= sqrt(paperWhite);
  #endif
    half3 encoded = max(sqrt(max(input, 0.0)) + noise, 0.0);
    input = encoded * encoded;
#endif

    return input;
}

half3 ApplyDithering(half3 input, float2 uv, TEXTURE2D_PARAM(BlueNoiseTexture, BlueNoiseSampler), float2 scale, float2 offset, float paperWhite, float oneOverPaperWhite)
{
    half noise = SAMPLE_TEXTURE2D(BlueNoiseTexture, BlueNoiseSampler, uv * scale + offset).a;
    return ApplyDithering(input, noise, paperWhite, oneOverPaperWhite);
}

#if _FXAA
static const FxaaFloat kSubpixelBlendAmount = 0.65;
static const FxaaFloat kRelativeContrastThreshold = 0.15;
static const FxaaFloat kAbsoluteContrastThreshold = 0.03;
#endif

half3 ApplyFXAA(half3 color, float2 positionNDC, int2 positionSS, float4 sourceSize, TEXTURE2D_X(inputTexture), float paperWhite, float oneOverPaperWhite)
{
#if _FXAA
    FxaaTex tex = {sampler_LinearClamp, _BlitTexture};
    FxaaFloat4 kUnusedFloat4 = FxaaFloat4(0, 0, 0, 0);

    FxaaFloat4 fxaaConsolePos = 0;
    FxaaFloat4 kFxaaConsoleRcpFrameOpt = 0;
    FxaaFloat4 kFxaaConsoleRcpFrameOpt2 = 0;
    FxaaFloat kFxaaConsoleEdgeSharpness = 0;
    FxaaFloat kFxaaConsoleEdgeThreshold = 0;
    FxaaFloat kFxaaConsoleEdgeThresholdMin = 0;
    FxaaFloat2 fxaaHDROutputPaperWhiteNits = 0;

#if FXAA_PC_CONSOLE == 1
    fxaaConsolePos = FxaaFloat4(positionNDC.xy - 0.5*sourceSize.zw, positionNDC.xy + 0.5*sourceSize.zw);
    kFxaaConsoleRcpFrameOpt = 0.5*FxaaFloat4(sourceSize.zw, -sourceSize.zw);
    kFxaaConsoleRcpFrameOpt2 = 2.0*FxaaFloat4(-sourceSize.zw, sourceSize.zw);
    kFxaaConsoleEdgeSharpness = 8.0;
    kFxaaConsoleEdgeThreshold = 0.125;
    kFxaaConsoleEdgeThresholdMin = 0.05;
#endif
    fxaaHDROutputPaperWhiteNits = FxaaFloat2(paperWhite, oneOverPaperWhite);

    return FxaaPixelShader(
        positionNDC,
        FxaaFloat4(color, 0),
        fxaaConsolePos,
        tex,
        tex,
        tex,
        sourceSize.zw,
        kFxaaConsoleRcpFrameOpt,
        kFxaaConsoleRcpFrameOpt2,
        kUnusedFloat4,
        kSubpixelBlendAmount,
        kRelativeContrastThreshold,
        kAbsoluteContrastThreshold,
        kFxaaConsoleEdgeSharpness,
        kFxaaConsoleEdgeThreshold,
        kFxaaConsoleEdgeThresholdMin,
        kUnusedFloat4,
        fxaaHDROutputPaperWhiteNits
    ).rgb;
#else
    return color;
#endif
}

#endif // UNIVERSAL_POSTPROCESSING_COMMON_INCLUDED
