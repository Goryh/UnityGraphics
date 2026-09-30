
#ifndef UNIVERSAL_REALTIME_LIGHTS_INCLUDED
#define UNIVERSAL_REALTIME_LIGHTS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LightCookie/LightCookie.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Clustering.hlsl"

// Abstraction over Light shading data.
struct Light
{
    half3   direction;
    half3   color;
    float   distanceAttenuation; // full-float precision required on some platforms
    half    shadowAttenuation;
    uint    layerMask;
};

#if USE_FORWARD_PLUS
    #define LIGHT_LOOP_BEGIN(lightCount) { \
    ClusterIterator _urp_internal_clusterIterator; \
    if (ClusterInitPixel(inputData.positionCS.xy, _urp_internal_clusterIterator)) { \
    uint lightIndex; \
    [loop] while (ClusterNext(_urp_internal_clusterIterator, lightIndex)) {
    #define LIGHT_LOOP_END } } }
#else
    #define LIGHT_LOOP_BEGIN(lightCount) \
    for (uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex) {
    #define LIGHT_LOOP_END }
#endif

///////////////////////////////////////////////////////////////////////////////
//                        Attenuation Functions                               /
///////////////////////////////////////////////////////////////////////////////

// Matches Unity Vanilla HINT_NICE_QUALITY attenuation
// Attenuation smoothly decreases to light range.
float DistanceAttenuation(float distanceSqr, half2 distanceAttenuation)
{
    // We use a shared distance attenuation for additional directional and puctual lights
    // for directional lights attenuation will be 1
    float lightAtten = rcp(distanceSqr);
    float2 distanceAttenuationFloat = float2(distanceAttenuation);

    // Use the smoothing factor also used in the Unity lightmapper.
    half factor = half(distanceSqr * distanceAttenuationFloat.x);
    half smoothFactor = saturate(half(1.0) - factor * factor);
    smoothFactor = smoothFactor * smoothFactor;

    return lightAtten * smoothFactor;
}

// Distance attenuation of a point light, given 1 / range^2.
// Matches DistanceAttenuation() with distanceAttenuation.x = invRangeSq.
float PointLightDistanceAttenuation(float distanceSqr, float invRangeSq)
{
    float lightAtten = rcp(distanceSqr);

    // Use the smoothing factor also used in the Unity lightmapper.
    half factor = half(distanceSqr * invRangeSq);
    half smoothFactor = saturate(half(1.0) - factor * factor);
    smoothFactor = smoothFactor * smoothFactor;

    return lightAtten * smoothFactor;
}

half AngleAttenuation(half3 spotDirection, half3 lightDirection, half2 spotAttenuation)
{
    // Spot Attenuation with a linear falloff can be defined as
    // (SdotL - cosOuterAngle) / (cosInnerAngle - cosOuterAngle)
    // This can be rewritten as
    // invAngleRange = 1.0 / (cosInnerAngle - cosOuterAngle)
    // SdotL * invAngleRange + (-cosOuterAngle * invAngleRange)
    // SdotL * spotAttenuation.x + spotAttenuation.y

    // If we precompute the terms in a MAD instruction
    half SdotL = dot(spotDirection, lightDirection);
    half atten = saturate(SdotL * spotAttenuation.x + spotAttenuation.y);
    return atten * atten;
}

///////////////////////////////////////////////////////////////////////////////
//                      Light Abstraction                                    //
///////////////////////////////////////////////////////////////////////////////

Light GetMainLight()
{
    Light light;
    light.direction = half3(_MainLightPosition.xyz);
#if USE_FORWARD_PLUS
#if defined(LIGHTMAP_ON) && defined(LIGHTMAP_SHADOW_MIXING)
    light.distanceAttenuation = _MainLightColor.a;
#else
    light.distanceAttenuation = 1.0;
#endif
#else
    light.distanceAttenuation = unity_LightData.z; // unity_LightData.z is 1 when not culled by the culling mask, otherwise 0.
#endif
    light.shadowAttenuation = 1.0;
    light.color = _MainLightColor.rgb;

    light.layerMask = _MainLightLayerMask;

    return light;
}

Light GetMainLight(float4 shadowCoord)
{
    Light light = GetMainLight();
    light.shadowAttenuation = MainLightRealtimeShadow(shadowCoord);
    return light;
}

Light GetMainLight(float4 shadowCoord, float3 positionWS, half4 shadowMask)
{
    Light light = GetMainLight();
    light.shadowAttenuation = MainLightShadow(shadowCoord, positionWS, shadowMask, _MainLightOcclusionProbes);

    #if defined(_LIGHT_COOKIES)
        real3 cookieColor = SampleMainLightCookie(positionWS);
        light.color *= cookieColor;
    #endif

    return light;
}

Light GetMainLight(InputData inputData, half4 shadowMask, AmbientOcclusionFactor aoFactor)
{
    Light light = GetMainLight(inputData.shadowCoord, inputData.positionWS, shadowMask);

    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
    {
        light.color *= aoFactor.directAmbientOcclusion;
    }
    #endif

    return light;
}

// Internal radius of the emitting sphere of a spherical light (SphericalLight.areaRadius).
half GetAdditionalLightAreaRadius(int perObjectLightIndex)
{
    return half(f16tof32(asuint(ADDITIONAL_LIGHT_COLOR_PACKED(perObjectLightIndex).a)));
}

// SphericalLight.exclusionMask, 16 bits.
uint GetAdditionalLightExclusionMask(int perObjectLightIndex)
{
    return asuint(ADDITIONAL_LIGHT_COLOR_PACKED(perObjectLightIndex).a) >> 16;
}

// Fills a light struct given a perObjectLightIndex. Additional lights are always spherical lights, lit like point lights.
Light GetAdditionalPerObjectLight(int perObjectLightIndex, float3 positionWS)
{
    float4 lightPositionWS = ADDITIONAL_LIGHT_POSITION_INV_RANGE_SQ(perObjectLightIndex); // w: 1 / range^2
    half3 color = half3(ADDITIONAL_LIGHT_COLOR_PACKED(perObjectLightIndex).rgb);

    float3 lightVector = lightPositionWS.xyz - positionWS;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
    // full-float precision required on some platforms
    float attenuation = PointLightDistanceAttenuation(distanceSqr, lightPositionWS.w);

    Light light;
    light.direction = lightDirection;
    light.distanceAttenuation = attenuation;
    light.shadowAttenuation = 1.0; // Additional lights don't cast shadows.
    light.color = color;
    light.layerMask = 0xFFFFFFFFu; // Additional lights affect all rendering layers.

    return light;
}

uint GetPerObjectLightIndexOffset()
{
    return 0;
}

// Returns a per-object index given a loop index.
// This abstract the underlying data implementation for storing lights/light indices
int GetPerObjectLightIndex(uint index)
{
    // since index is uint shader compiler will implement
    // div & mod as bitfield ops (shift and mask).

    // TODO: Can we index a float4? Currently compiler is
    // replacing unity_LightIndicesX[i] with a dp4 with identity matrix.
    // u_xlat16_40 = dot(unity_LightIndices[int(u_xlatu13)], ImmCB_0_0_0[u_xlati1]);
    // This increases both arithmetic and register pressure.
    //
    // NOTE: min16float4 bug workaround.
    // Take the "vec4" part into float4 tmp variable in order to force float4 math.
    // It appears indexing half4 as min16float4 on DX11 can fail. (dp4 {min16f})
    float4 tmp = unity_LightIndices[index / 4];
    return int(tmp[index % 4]);
}

// Fills a light struct given a loop i index. This will convert the i
// index to a perObjectLightIndex
Light GetAdditionalLight(uint i, float3 positionWS)
{
#if USE_FORWARD_PLUS
    int lightIndex = i;
#else
    int lightIndex = GetPerObjectLightIndex(i);
#endif
    return GetAdditionalPerObjectLight(lightIndex, positionWS);
}

Light GetAdditionalLight(uint i, float3 positionWS, half4 shadowMask)
{
#if USE_FORWARD_PLUS
    int lightIndex = i;
#else
    int lightIndex = GetPerObjectLightIndex(i);
#endif
    // Additional lights have neither shadows (realtime or baked) nor cookies, so shadowMask is unused.
    return GetAdditionalPerObjectLight(lightIndex, positionWS);
}

Light GetAdditionalLight(uint i, InputData inputData, half4 shadowMask, AmbientOcclusionFactor aoFactor)
{
    Light light = GetAdditionalLight(i, inputData.positionWS, shadowMask);

    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
    {
        light.color *= aoFactor.directAmbientOcclusion;
    }
    #endif

    return light;
}

int GetAdditionalLightsCount()
{
#if USE_FORWARD_PLUS
    // Counting the number of lights in clustered requires traversing the bit list, and is not needed up front.
    return 0;
#else
    // TODO: we need to expose in SRP api an ability for the pipeline cap the amount of lights
    // in the culling. This way we could do the loop branch with an uniform
    // This would be helpful to support baking exceeding lights in SH as well
    return int(min(_AdditionalLightsCount.x, unity_LightData.y));
#endif
}

half4 CalculateShadowMask(InputData inputData)
{
    // To ensure backward compatibility we have to avoid using shadowMask input, as it is not present in older shaders
    #if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
    half4 shadowMask = inputData.shadowMask; // Shadowmask was sampled from lightmap
    #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    half4 shadowMask = inputData.shadowMask; // Shadowmask (probe occlusion) was sampled from APV
    #elif !defined (LIGHTMAP_ON)
    half4 shadowMask = unity_ProbesOcclusion; // Sample shadowmask (probe occlusion) from legacy probes
    #else
    half4 shadowMask = half4(1, 1, 1, 1); // Fallback shadowmask, fully unoccluded
    #endif

    return shadowMask;
}

#endif
