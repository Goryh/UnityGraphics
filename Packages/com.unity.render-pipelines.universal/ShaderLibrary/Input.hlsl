#ifndef UNIVERSAL_INPUT_INCLUDED
#define UNIVERSAL_INPUT_INCLUDED
// URP package specific shader input variables and defines.
// Unity Engine specific built-in shader input variables are defined in .universal/ShaderLibrary/UnityInput.hlsl

#include "Packages/com.unity.render-pipelines.universal-config/Runtime/ShaderConfig.cs.hlsl"

#define MAX_VISIBLE_LIGHTS_UBO  32
#define MAX_VISIBLE_LIGHTS_SSBO 256

// Keep in sync with RenderingUtils.useStructuredBuffer
#define USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA 0

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderTypes.cs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Deprecated.hlsl"

// Must match: UniversalRenderPipeline.maxVisibleAdditionalLights
#if defined(SHADER_API_MOBILE) && defined(SHADER_API_GLES30)
    #define MAX_VISIBLE_LIGHTS MAX_VISIBLE_LIGHT_COUNT_LOW_END_MOBILE
// WebGPU's minimal limits are based on mobile rather than desktop, so it will need to assume mobile.
#elif defined(SHADER_API_MOBILE) || (defined(SHADER_API_GLCORE) && !defined(SHADER_API_SWITCH) && !defined(SHADER_API_SWITCH2)) || defined(SHADER_API_GLES3) || defined(SHADER_API_WEBGPU) // Workaround because SHADER_API_GLCORE is also defined when SHADER_API_SWITCH is
    #define MAX_VISIBLE_LIGHTS MAX_VISIBLE_LIGHT_COUNT_MOBILE
#else
    #define MAX_VISIBLE_LIGHTS MAX_VISIBLE_LIGHT_COUNT_DESKTOP
#endif

// Match with values in UniversalRenderPipeline.cs
// Forward+ tiles: each tile is one float4 holding up to 16 byte-sized light indices (light index + 1, 0 = no light).
#define MAX_LIGHTS_PER_TILE 16
#define MAX_TILES 4096
// Forward+ reflection probes are not binned into tiles, so only a single probe is kept.
#define MAX_REFLECTION_PROBES 1

struct InputData
{
    float3  positionWS;
    float4  positionCS;
    float3  normalWS;
    half3   viewDirectionWS;
    float4  shadowCoord;
    half    fogCoord;
    half3   vertexLighting;
    half3   bakedGI;
    float2  normalizedScreenSpaceUV;
    half4   shadowMask;
    half3x3 tangentToWorld;

#if defined(DEBUG_DISPLAY)
    half2   dynamicLightmapUV;
    half2   staticLightmapUV;
    float3  vertexSH;

    half3 brdfDiffuse;
    half3 brdfSpecular;

    // Mipmap Streaming Debug
    float2 uv;
    uint mipCount;

    // texelSize :
    // x = 1 / width
    // y = 1 / height
    // z = width
    // w = height
    float4 texelSize;

    // mipInfo :
    // x = quality settings minStreamingMipLevel
    // y = original mip count for texture
    // z = desired on screen mip level
    // w = loaded mip level
    float4 mipInfo;

    // streamInfo :
    // x = streaming priority
    // y = time stamp of the latest texture upload
    // z = streaming status
    // w = 0
    float4 streamInfo;

    float3 originalColor;

    float4 probeOcclusion;
#endif
};

///////////////////////////////////////////////////////////////////////////////
//                      Constant Buffers                                     //
///////////////////////////////////////////////////////////////////////////////

half4 _GlossyEnvironmentColor;
half4 _SubtractiveShadowColor;

half4 _GlossyEnvironmentCubeMap_HDR;
TEXTURECUBE(_GlossyEnvironmentCubeMap);
SAMPLER(sampler_GlossyEnvironmentCubeMap);

#define _InvCameraViewProj unity_MatrixInvVP
float4 _ScaledScreenParams;

// x = Mip Bias
// y = 2.0 ^ [Mip Bias]
float2 _GlobalMipBias;

// 1.0 if it's possible for AlphaToMask to be enabled for this draw and 0.0 otherwise
float _AlphaToMaskAvailable;

float4 _MainLightPosition;
// In Forward+, .a stores whether the main light is using subtractive mixed mode.
half4 _MainLightColor;
half4 _MainLightOcclusionProbes;
uint _MainLightLayerMask;

// x: SSAO Enabled/Disabled (Needed for situations when OFF keyword is stripped out but feature disabled in runtime)
// yz are currently unused
// w: directLightStrength
half4 _AmbientOcclusionParam;

half4 _AdditionalLightsCount;

uint _RenderingLayerMaxInt;
float _RenderingLayerRcpMaxInt;

// Screen coord override.
float4 _ScreenCoordScaleBias;
float4 _ScreenSizeOverride;

uint _EnableProbeVolumes;

#if USE_FORWARD_PLUS
float4 _FPParams0;
float4 _FPParams1;

// Scale from screen-space UV [0, 1] to tile coordinates [0, tile resolution].
#define URP_FP_TILE_SCALE ((float2)_FPParams0.xy)
#define URP_FP_TILE_COUNT_X ((uint)_FPParams0.z)

// Tile count of a single view, used to offset into the tiles of the second eye.
#define URP_FP_TILE_COUNT ((uint)_FPParams1.x)

#endif

// Additional lights are the visible SphericalLights. Unity lights other than the main light are not uploaded.
// The buffer is bound from C# (ForwardLights.m_LightDataBuffer), so it must stay a constant buffer and its layout
// must match SphericalLightData: two float4 per light, interleaved.
//   [2 * i]     xyz: position, w: range (external radius)
//   [2 * i + 1] rgb: color, a: bits of the area radius (internal radius) as a half in the low 16 bits and the exclusion
//               mask in the high 16 bits. Declared float so the packed bits are loaded untouched; read them with
//               GetAdditionalLightAreaRadius(), GetAdditionalLightExclusionMask() or IsAdditionalLightExcluded().
CBUFFER_START(AdditionalLights)
float4 _AdditionalLightsData[2 * MAX_VISIBLE_LIGHTS];
CBUFFER_END

#define ADDITIONAL_LIGHT_POSITION_RANGE(lightIndex) _AdditionalLightsData[2 * (lightIndex)]
#define ADDITIONAL_LIGHT_COLOR_PACKED(lightIndex) _AdditionalLightsData[2 * (lightIndex) + 1]

#if USE_FORWARD_PLUS

CBUFFER_START(urp_TileBuffer)
        uint4 urp_Tiles[MAX_TILES];
CBUFFER_END

TEXTURE2D(urp_ReflProbes_Atlas);
float urp_ReflProbes_Count;

// 2023.3 Deprecated. This is for backwards compatibility. Remove in the future.
#define samplerurp_ReflProbes_Atlas sampler_LinearClamp

#ifndef LIGHT_SHADOWS_NO_CBUFFER
CBUFFER_START(urp_ReflectionProbeBuffer)
#endif
float4 urp_ReflProbes_BoxMax[MAX_REFLECTION_PROBES];          // w contains the blend distance
float4 urp_ReflProbes_BoxMin[MAX_REFLECTION_PROBES];          // w contains the importance
float4 urp_ReflProbes_ProbePosition[MAX_REFLECTION_PROBES];   // w is positive for box projection, |w| is max mip level
float4 urp_ReflProbes_MipScaleOffset[MAX_REFLECTION_PROBES * 7];
#ifndef LIGHT_SHADOWS_NO_CBUFFER
CBUFFER_END
#endif

#endif

#define UNITY_MATRIX_M     unity_ObjectToWorld
#define UNITY_MATRIX_I_M   unity_WorldToObject
#define UNITY_MATRIX_V     unity_MatrixV
#define UNITY_MATRIX_I_V   unity_MatrixInvV
#define UNITY_MATRIX_P     OptimizeProjectionMatrix(glstate_matrix_projection)
#define UNITY_MATRIX_I_P   unity_MatrixInvP
#define UNITY_MATRIX_VP    unity_MatrixVP
#define UNITY_MATRIX_I_VP  unity_MatrixInvVP
#define UNITY_MATRIX_MV    mul(UNITY_MATRIX_V, UNITY_MATRIX_M)
#define UNITY_MATRIX_T_MV  transpose(UNITY_MATRIX_MV)
#define UNITY_MATRIX_IT_MV transpose(mul(UNITY_MATRIX_I_M, UNITY_MATRIX_I_V))
#define UNITY_MATRIX_MVP   mul(UNITY_MATRIX_VP, UNITY_MATRIX_M)
#define UNITY_PREV_MATRIX_M   unity_MatrixPreviousM
#define UNITY_PREV_MATRIX_I_M unity_MatrixPreviousMI

// Note: #include order is important here.
// UnityInput.hlsl must be included before UnityInstancing.hlsl, so constant buffer
// declarations don't fail because of instancing macros.
// UniversalDOTSInstancing.hlsl must be included after UnityInstancing.hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalDOTSInstancing.hlsl"

// VFX may also redefine UNITY_MATRIX_M / UNITY_MATRIX_I_M as static per-particle global matrices.
#ifdef HAVE_VFX_MODIFICATION
#include "Packages/com.unity.visualeffectgraph/Shaders/VFXMatricesOverride.hlsl"
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

#endif
