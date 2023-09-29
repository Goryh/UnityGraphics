#ifndef UNITY_PATH_TRACING_LIGHT_INCLUDED
#define UNITY_PATH_TRACING_LIGHT_INCLUDED

// This is just because it need to be defined, shadow maps are not used.
#define SHADOW_LOW

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/CookieSampling.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightEvaluation.hlsl"

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/ShaderVariablesRaytracingLightLoop.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/Shadows/SphericalQuad.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/Common/AtmosphericScatteringRayTracing.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/PathTracing/Shaders/PathTracingSampling.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/PathTracing/Shaders/PathTracingSkySampling.hlsl"

// Define this to use the Ray Tracing light cluster
#define USE_LIGHT_CLUSTER

#ifdef USE_LIGHT_CLUSTER
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RayTracingLightCluster.hlsl"
#endif

// How many lights (at most) do we support at one given shading point
// FIXME: hardcoded limits are evil, this LightList should instead be put together in C#
#define MAX_LOCAL_LIGHT_COUNT SHADEROPTIONS_PATH_TRACING_MAX_LIGHT_COUNT
#define MAX_DISTANT_LIGHT_COUNT 4

#define DELTA_PDF 1000000.0
#define DOT_PRODUCT_EPSILON 0.001

#define SAMPLE_SOLID_ANGLE

// Supports punctual, spot, rect area and directional lights, in addition to one sky (aka environment)
struct LightList
{
    uint  localCount;
    uint  localPointCount;
    uint  localIndex[MAX_LOCAL_LIGHT_COUNT];
    float localWeight;

    uint  distantCount;
    uint  distantIndex[MAX_DISTANT_LIGHT_COUNT];
    float distantWeight;

    uint  skyCount; // 0 or 1
    float skyWeight;

#ifdef USE_LIGHT_CLUSTER
    uint  cellIndex;
#endif
};

bool IsAreaLightActive(LightData lightData, float3 position, float3 normal)
{
    float3 lightToPosition = position - lightData.positionRWS;

#ifndef USE_LIGHT_CLUSTER
    // Check light range first
    if (Length2(lightToPosition) > Sq(lightData.range))
        return false;
#endif

    // If this is  tube light, we're done
    if (lightData.lightType == GPULIGHTTYPE_TUBE)
        return true;

    // Check that the shading position is in front of the light
    float lightCos = dot(lightToPosition, lightData.forward);
    if (lightCos < 0.0)
        return false;

    // Check that at least part of the light is above the tangent plane
   float lightTangentDist = dot(normal, lightToPosition);
   if (4.0 * lightTangentDist * abs(lightTangentDist) > Sq(lightData.size.x) + Sq(lightData.size.y))
        return false;

    return true;
}

bool IsPointLightActive(LightData lightData, float3 position, float3 normal)
{
    float3 lightToPosition = position - lightData.positionRWS;

#ifndef USE_LIGHT_CLUSTER
    // Check light range first
    if (Length2(lightToPosition) > Sq(lightData.range))
        return false;
#endif

    // Check that at least part of the light is above the tangent plane
    float lightTangentDist = dot(normal, lightToPosition);
    if (lightTangentDist * abs(lightTangentDist) > lightData.size.x)
        return false;

    // If this is an omni-directional point light, we're done
    if (lightData.lightType == GPULIGHTTYPE_POINT)
        return true;

    // Check that we are on the right side of the light plane
    float z = dot(lightToPosition, lightData.forward);
    if (z < 0.0)
        return false;

    if (lightData.lightType == GPULIGHTTYPE_SPOT)
    {
        // Offset the light position towards the back, to account for the radius,
        // then check whether we are still within the dilated cone angle
        float sinTheta2 = 1.0 - Sq(lightData.angleOffset / lightData.angleScale);
        float3 lightRadiusOffset = sqrt(lightData.size.x / sinTheta2) * lightData.forward;
        float lightCos = dot(normalize(lightToPosition + lightRadiusOffset), lightData.forward);

        return lightCos * lightData.angleScale + lightData.angleOffset > 0.0;
    }

    // Our light type is either BOX or PYRAMID
    float x = abs(dot(lightToPosition, lightData.right));
    float y = abs(dot(lightToPosition, lightData.up));

    return (lightData.lightType == GPULIGHTTYPE_PROJECTOR_BOX) ?
        x < 1.0 && y < 1.0 : // BOX
        x < z   && y < z;    // PYRAMID
}

bool IsDistantLightActive(DirectionalLightData lightData, float3 normal)
{
    return dot(normal, lightData.forward) <= sin(lightData.angularDiameter * 0.5);
}

LightList CreateLightList(float3 position, float3 normal, uint lightLayers = RENDERING_LAYERS_MASK,
                          bool withPoint = true, bool withArea = true, bool withDistant = true,
                          float3 lightPosition = FLT_MAX)
{
    LightList list = (LightList)0;
    uint i;

    // First take care of local lights (point, area)
    if (withPoint || withArea)
    {
        uint localPointCount, localCount;

#ifdef USE_LIGHT_CLUSTER
        if (PointInsideCluster(position))
        {
            list.cellIndex = GetClusterCellIndex(position);
            localPointCount = GetPunctualLightEndIndexInClusterCell(list.cellIndex);
            localCount = GetAreaLightEndIndexInClusterCell(list.cellIndex);
        }
        else
        {
            localPointCount = 0;
            localCount = 0;
        }
#else
        localPointCount = _PunctualLightCountRT;
        localCount = _PunctualLightCountRT + _AreaLightCountRT;
#endif

        // Do we have an imposed local light (identificed by position), for volumetric scattering?
        bool forceLightPosition = (lightPosition.x != FLT_MAX);

        // First point lights (including spot lights)
        if (withPoint)
        {
            for (i = 0; i < localPointCount && list.localPointCount < MAX_LOCAL_LIGHT_COUNT; i++)
            {
    #ifdef USE_LIGHT_CLUSTER
                const LightData lightData = FetchClusterLightIndex(list.cellIndex, i);
    #else
                const LightData lightData = _LightDatasRT[i];
    #endif

                if (forceLightPosition && any(lightPosition - lightData.positionRWS))
                    continue;

                if (IsMatchingLightLayer(lightData.lightLayers, lightLayers) && IsPointLightActive(lightData, position, normal))
                    list.localIndex[list.localPointCount++] = i;
            }

            list.localCount = list.localPointCount;
        }

        // Then rect area lights
        if (withArea)
        {
            for (i = localPointCount; i < localCount && list.localCount < MAX_LOCAL_LIGHT_COUNT; i++)
            {
    #ifdef USE_LIGHT_CLUSTER
                const LightData lightData = FetchClusterLightIndex(list.cellIndex, i);
    #else
                const LightData lightData = _LightDatasRT[i];
    #endif

                if (forceLightPosition && any(lightPosition - lightData.positionRWS))
                    continue;

                if (IsMatchingLightLayer(lightData.lightLayers, lightLayers) && IsAreaLightActive(lightData, position, normal))
                    list.localIndex[list.localCount++] = i;
            }
        }
    }

    // Then filter the active distant lights (directional)
    list.distantCount = 0;

    if (withDistant)
    {
        for (i = 0; i < _DirectionalLightCount && list.distantCount < MAX_DISTANT_LIGHT_COUNT; i++)
        {
            if (IsMatchingLightLayer(_DirectionalLightDatas[i].lightLayers, lightLayers) && IsDistantLightActive(_DirectionalLightDatas[i], normal))
                list.distantIndex[list.distantCount++] = i;
        }
    }

    // And finally the sky light
    list.skyCount = withDistant && IsSkyEnabled() && IsSkySamplingEnabled() ? 1 : 0;

    // Compute the weights, used for the lights PDF (we split 50/50 between local and distant+sky)
    list.localWeight = list.localCount ? (list.distantCount + list.skyCount ? 0.5 : 1.0) : 0.0;
    float nonLocalWeight = 1.0 - list.localWeight;
    list.distantWeight = list.distantCount ? (list.skyCount ? 0.5 * nonLocalWeight : nonLocalWeight) : 0.0;
    list.skyWeight = nonLocalWeight - list.distantWeight;

    return list;
}

uint GetLightCount(LightList list)
{
    return list.localCount + list.distantCount + list.skyCount;
}

LightData GetLocalLightData(LightList list, uint i)
{
#ifdef USE_LIGHT_CLUSTER
    return FetchClusterLightIndex(list.cellIndex, list.localIndex[i]);
#else
    return _LightDatasRT[list.localIndex[i]];
#endif
}

LightData GetLocalLightData(LightList list, float inputSample)
{
    return GetLocalLightData(list, (uint)(inputSample * list.localCount));
}

DirectionalLightData GetDistantLightData(LightList list, uint i)
{
    return _DirectionalLightDatas[list.distantIndex[i]];
}

DirectionalLightData GetDistantLightData(LightList list, float inputSample)
{
    return GetDistantLightData(list, (uint)(inputSample * list.distantCount));
}

float GetLocalLightWeight(LightList list)
{
    return list.localWeight / list.localCount;
}

float GetDistantLightWeight(LightList list)
{
    return list.distantWeight / list.distantCount;
}

float GetSkyLightWeight(LightList list)
{
    return list.skyWeight;
}

#define PTLIGHT_LOCAL   0
#define PTLIGHT_DISTANT 1
#define PTLIGHT_SKY     2

uint PickLightType(LightList list, inout float theSample)
{
    if (theSample < list.localWeight)
    {
        // We pick local lighting
        theSample /= list.localWeight;
        return PTLIGHT_LOCAL;
    }

    if (theSample < list.localWeight + list.distantWeight)
    {
        // We pick distant lighting
        theSample = (theSample - list.localWeight) / list.distantWeight;
        return PTLIGHT_DISTANT;
    }

    // Otherwise, sky lighting
    theSample = (theSample - list.distantWeight - list.localWeight) / list.skyWeight;
    return PTLIGHT_SKY;
 }

float3 GetPunctualEmission(LightData lightData, float3 outgoingDir, float dist)
{
    float3 emission = lightData.color;

    // Punctual attenuation
    float4 distances = float4(dist, Sq(dist), rcp(dist), -dist * dot(outgoingDir, lightData.forward));
    emission *= PunctualLightAttenuation(distances, lightData.rangeAttenuationScale, lightData.rangeAttenuationBias, lightData.angleScale, lightData.angleOffset);

#ifndef LIGHT_EVALUATION_NO_COOKIE
    if (lightData.cookieMode != COOKIEMODE_NONE)
    {
        LightLoopContext context;
        emission *= EvaluateCookie_Punctual(context, lightData, -dist * outgoingDir).rgb;
    }
#endif

    return emission;
}

float3 GetDirectionalEmission(DirectionalLightData lightData, float3 positionRWS)
{
    float3 emission = lightData.color;

#if SHADEROPTIONS_PRECOMPUTED_ATMOSPHERIC_ATTENUATION
    // Nothing to do here
#else
    // Physical sky emission color code, adapted from EvaluateLight_Directional()
    if (asint(lightData.distanceFromCamera) >= 0)
        emission *= EvaluateSunColorAttenuation(positionRWS - _PlanetCenterPosition, -lightData.forward);
#endif

#ifndef LIGHT_EVALUATION_NO_COOKIE
    if (lightData.cookieMode != COOKIEMODE_NONE)
    {
        LightLoopContext context;
        float3 lightToSample = positionRWS - lightData.positionRWS;
        emission *= EvaluateCookie_Directional(context, lightData, lightToSample);
    }
#endif

    return emission;
}

float3 GetAreaEmission(LightData lightData, float centerU, float centerV, float sqDist)
{
    float3 emission = lightData.color;

    // Range windowing (see LightLoop.cs to understand why it is written this way)
    if (lightData.rangeAttenuationBias == 1.0)
        emission *= SmoothDistanceWindowing(sqDist, rcp(Sq(lightData.range)), lightData.rangeAttenuationBias);

#ifndef LIGHT_EVALUATION_NO_COOKIE
    if (lightData.cookieMode != COOKIEMODE_NONE)
    {
        float2 uv = float2(0.5 - centerU, 0.5 + centerV);
        emission *= SampleCookie2D(uv, lightData.cookieScaleOffset);
    }
#endif

    return emission;
}

float3 GetLightTransmission(float3 transmission, float shadowOpacity)
{
    return lerp(float3(1.0, 1.0, 1.0), transmission, shadowOpacity);
}

bool SampleRectAreaLight(LightList lightList, LightData lightData,
                         float3 inputSample,
                         float3 position,
                         float3 normal,
                         bool isSpherical,
                     out float3 outgoingDir,
                     out float3 value,
                     out float pdf,
                     out float dist)
{
    // The lights have already been filtered for "IsActive" in CreateLightList
    /*
    if (!IsAreaLightActive(lightData, position, normal))
        return false;
    */

    // Initialize out values
    outgoingDir = 0;
    value = 0;
    pdf = 0;
    dist = 0;

    float3 lightCenter = lightData.positionRWS;

#ifndef SAMPLE_SOLID_ANGLE
    // Generate a point on the surface of the light
    float centerU = inputSample.x - 0.5;
    float centerV = inputSample.y - 0.5;
    float3 lightSamplePos = lightCenter + centerU * lightData.size.x * lightData.right + centerV * lightData.size.y * lightData.up;

    // And the corresponding direction
    outgoingDir = lightSamplePos - position;
    float sqDist = Length2(outgoingDir);
    dist = sqrt(sqDist);
    outgoingDir /= dist;

    if (!isSpherical && dot(normal, outgoingDir) < DOT_PRODUCT_EPSILON)
        return false;

    float cosTheta = -dot(outgoingDir, lightData.forward);
    if (cosTheta < DOT_PRODUCT_EPSILON)
        return false;

    float lightArea = length(cross(lightData.size.x * lightData.right, lightData.size.y * lightData.up));

    value = GetAreaEmission(lightData, centerU, centerV, sqDist);
    pdf = GetLocalLightWeight(lightList) * sqDist / (lightArea * cosTheta);
#else
    // Solid angle sampling
    float u = inputSample.x;
    float v = inputSample.y;

    SphQuad squad;
    lightCenter = lightCenter - 0.5 * lightData.size.x * lightData.right;
    lightCenter = lightCenter - 0.5 * lightData.size.y * lightData.up;
    SphQuadInit(lightCenter, lightData.size.x * lightData.right, lightData.size.y * lightData.up, position, squad);

    // Generate sample
    // TODO: Move this validity check into the common quad initialization function
    if (squad.S < 0.00001 || isnan(squad.S))
        return false;

    // 1. compute ’cu’
    float au = u * squad.S + squad.k;
    float fu = (cos(au) * squad.b0 - squad.b1) / sin(au);
    float cu = 1 / sqrt(fu * fu + squad.b0sq);// *(fu > 0 ? +1 : -1);
    cu = (fu > 0.0f) ? cu : -cu;
    cu = clamp(cu, -1, 1); // avoid NaNs

    // 2. compute ’xu’
    float xu = -(cu * squad.z0) / sqrt(1 - cu * cu);
    xu = clamp(xu, squad.x0, squad.x1); // avoid Infs

    // 3. compute ’yv’
    float d = sqrt(xu * xu + squad.z0sq);
    float h0 = squad.y0 / sqrt(d * d + squad.y0sq);
    float h1 = squad.y1 / sqrt(d * d + squad.y1sq);
    float hv = h0 + v * (h1 - h0);
    float hv2 = hv * hv;
    float eps = 0.0001;
    float yv = (hv2 < 1.0 - eps) ? (hv * d) / sqrt(1.0 - hv2) : squad.y1;

    // 4. transform (xu,yv,z0) to world coords
    float3 lightSamplePos = (squad.o + xu * squad.x + yv * squad.y + squad.z0 * squad.z);

    // TODO: We should use this function, but we need xu and yv below for cookie evaluation
    // float3 lightSamplePos = SphQuadSample(squad, u, v);

    outgoingDir = lightSamplePos - position;
    float sqDist = Length2(outgoingDir);
    dist = sqrt(sqDist);
    outgoingDir /= dist;

    u = (xu - squad.x0)/(squad.x1 - squad.x0) - 0.5;
    v = (yv - squad.y0)/(squad.y1 - squad.y0) - 0.5;
    value = GetAreaEmission(lightData, u, v, sqDist); // TODO: add rcpPdf term here from lightlist when that PR lands
    pdf = GetLocalLightWeight(lightList) / squad.S;

    if (!isSpherical && dot(normal, outgoingDir) < DOT_PRODUCT_EPSILON)
        return false;

    float cosTheta = -dot(outgoingDir, lightData.forward);
    if (cosTheta < DOT_PRODUCT_EPSILON)
        return false;
#endif

    return true;
}

bool SampleTubeAreaLight(LightList lightList, LightData lightData,
                         float3 inputSample,
                         float3 position,
                         float3 normal,
                         bool isSpherical,
                     out float3 outgoingDir,
                     out float3 value,
                     out float pdf,
                     out float dist)
{
    float3 lightCenter = lightData.positionRWS;
    float lightLength = lightData.size.x;

    // Generate a point on the line
    // TODO : equiangular sampling might be better than just uniformly sampling along the line.
    float centerU = inputSample.x - 0.5;
    float3 lightSamplePos = lightCenter + centerU * lightLength * lightData.right;

    // And the corresponding direction
    outgoingDir = lightSamplePos - position;
    float sqDist = Length2(outgoingDir);
    dist = sqrt(sqDist);
    outgoingDir /= dist;

    if (!isSpherical && dot(normal, outgoingDir) < DOT_PRODUCT_EPSILON)
        return false;

    float sinTheta = abs(dot(outgoingDir, lightData.right));
    if (sinTheta > sqrt(1.0-DOT_PRODUCT_EPSILON*DOT_PRODUCT_EPSILON))
        return false;
    float cosTheta = sqrt(1.0-sinTheta*sinTheta);

    // The multiplication by 2 is explained by the fact that we are dealing with a tube, although with radius -> 0
    // So the energy coming to a point is actually coming from the half arc of the tube facing the point.
    // This means the total intensity is multiplied by Integral{-PI/2 to PI/2, cos phi} = 2, phi being the
    // angle of the tube surface normal along that half arc. This is empirically verified by comparing visual
    // lighting intensities between the rasterized and path traced versions of the tube area light.
    value = GetAreaEmission(lightData, centerU, 0, sqDist) * 2 * cosTheta / sqDist;
    pdf = GetLocalLightWeight(lightList) / lightLength;

    // Multiply both value and pdf by DELTA_PDF to give more MIS weight for the light,
    // as the line light is ignored in EvaluateLight due to its inifinitesimal surface.
    value *= DELTA_PDF;
    pdf *= DELTA_PDF;

    return true;
}

float2 GetDiscAreaLightCookieUV(LightData lightData, float3 posOnDisk)
{
    float lightDiameter = 2*lightData.size.x;
    float centerU = dot(posOnDisk, lightData.right) / (lightDiameter * Length2(lightData.right));
    float centerV = dot(posOnDisk, lightData.up) / (lightDiameter * Length2(lightData.up));
    return float2(centerU, centerV);
}

bool SampleDiscAreaLight(LightList lightList, LightData lightData,
                         float3 inputSample,
                         float3 position,
                         float3 normal,
                         bool isSpherical,
                     out float3 outgoingDir,
                     out float3 value,
                     out float pdf,
                     out float dist)
{
    float3 lightCenter = lightData.positionRWS;
    float3 lightSamplePos;
    float3 lightNormal; // This is ignored
    float4x4 lightToWorld = float4x4(float4(lightData.right, 0.0), float4(lightData.up, 0.0), float4(lightData.forward, 0.0), float4(lightCenter, 1.0));
    float lightRadius = lightData.size.x;

    SampleDisk(inputSample.xy, lightToWorld, lightRadius, pdf, lightSamplePos, lightNormal);

    // And the corresponding direction
    outgoingDir = lightSamplePos - position;
    float sqDist = Length2(outgoingDir);
    dist = sqrt(sqDist);
    outgoingDir /= dist;

    if (!isSpherical && dot(normal, outgoingDir) < DOT_PRODUCT_EPSILON)
        return false;

    float cosTheta = -dot(outgoingDir, lightData.forward);
    if (cosTheta < DOT_PRODUCT_EPSILON)
        return false;

    float3 diskPos = lightSamplePos-lightCenter;
    float2 centerUV = GetDiscAreaLightCookieUV(lightData, diskPos);

    value = GetAreaEmission(lightData, centerUV.x, centerUV.y, sqDist);
    // 1/(Light area) has already been taken into account in the pdf returned by SampleDisk
    pdf *= GetLocalLightWeight(lightList) * sqDist / cosTheta;

    return true;
}

bool SamplePunctualLight(LightList lightList, LightData lightData,
                         float3 inputSample,
                         float3 position,
                         float3 normal,
                         bool isSpherical,
                     out float3 outgoingDir,
                     out float3 value,
                     out float pdf,
                     out float dist)
{
    // Direction from shading point to light position
    outgoingDir = lightData.positionRWS - position;
    float sqDist = Length2(outgoingDir);
    dist = sqrt(sqDist);
    outgoingDir /= dist;

    if (lightData.size.x > 0.0) // Stores the square radius
    {
        float3x3 localFrame = GetLocalFrame(outgoingDir);
        SampleCone(inputSample.xy, sqrt(1.0 / (1.0 + lightData.size.x / sqDist)), outgoingDir, pdf); // computes rcpPdf

        outgoingDir = outgoingDir.x * localFrame[0] + outgoingDir.y * localFrame[1] + outgoingDir.z * localFrame[2];
        pdf = min(rcp(pdf), DELTA_PDF);
    }
    else
    {
        // DELTA_PDF represents 1 / area, where the area is infinitesimal
        pdf = DELTA_PDF;
    }

    if (!isSpherical && dot(normal, outgoingDir) < DOT_PRODUCT_EPSILON)
        return false;

    value = GetPunctualEmission(lightData, outgoingDir, dist) * pdf;
    pdf = GetLocalLightWeight(lightList) * pdf;

    return true;
}

void SampleDistanceLight(LightList lightList, DirectionalLightData lightData,
                         float3 inputSample,
                         float3 position,
                     out float3 outgoingDir,
                     out float3 value,
                     out float pdf)
{
    if (lightData.angularDiameter > 0.0)
    {
        SampleCone(inputSample.xy, cos(lightData.angularDiameter * 0.5), outgoingDir, pdf); // computes rcpPdf
        value = GetDirectionalEmission(lightData, position) / pdf;
        pdf = GetDistantLightWeight(lightList) / pdf;
        outgoingDir = normalize(outgoingDir.x * normalize(lightData.right) + outgoingDir.y * normalize(lightData.up) - outgoingDir.z * lightData.forward);
    }
    else
    {
        value = GetDirectionalEmission(lightData, position) * DELTA_PDF;
        pdf = GetDistantLightWeight(lightList) * DELTA_PDF;
        outgoingDir = -lightData.forward;
    }
}

bool SampleLights(LightList lightList,
                  float3 inputSample,
                  float3 position,
                  float3 normal,
                  bool isVolume,
              out float3 outgoingDir,
              out float3 value,
              out float pdf,
              out float dist,
              out float shadowOpacity)
{
    if (!GetLightCount(lightList))
        return false;

    // Are we lighting a spherical (e.g. volume) or a hemi-spherical distribution (e.g. opaque surface)?
    const bool isSpherical = isVolume || !any(normal);

    // Stochastically pick one type of light to sample
    const uint lightType = PickLightType(lightList, inputSample.z);
    if (lightType == PTLIGHT_LOCAL)
    {
        // Pick a local light from the list
        LightData lightData = GetLocalLightData(lightList, inputSample.z);

        switch (lightData.lightType)
        {
        case GPULIGHTTYPE_RECTANGLE:
            if (!SampleRectAreaLight(lightList, lightData, inputSample, position, normal, isSpherical, outgoingDir, value, pdf, dist))
                return false;
            break;

        case GPULIGHTTYPE_TUBE:
            if (!SampleTubeAreaLight(lightList, lightData, inputSample, position, normal, isSpherical, outgoingDir, value, pdf, dist))
                return false;
            break;

        case GPULIGHTTYPE_DISC:
            if (!SampleDiscAreaLight(lightList, lightData, inputSample, position, normal, isSpherical, outgoingDir, value, pdf, dist))
                return false;
            break;

        default:
            if (!SamplePunctualLight(lightList, lightData, inputSample, position, normal, isSpherical, outgoingDir, value, pdf, dist))
                return false;
            break;
        }

        if (isVolume)
        {
            value *= lightData.volumetricLightDimmer;
            shadowOpacity = lightData.volumetricShadowDimmer;
        }
        else
        {
            value *= lightData.lightDimmer;
            shadowOpacity = lightData.shadowDimmer;
        }

#ifndef LIGHT_EVALUATION_NO_HEIGHT_FOG
        ApplyFogAttenuation(position, outgoingDir, dist, value);
#endif
    }
    else // Distant or Sky light
    {
        if (lightType == PTLIGHT_DISTANT)
        {
            // Pick a distant light from the list
            DirectionalLightData lightData = GetDistantLightData(lightList, inputSample.z);

            SampleDistanceLight(lightList, lightData, inputSample, position, outgoingDir, value, pdf);

            if (isVolume)
            {
                value *= lightData.volumetricLightDimmer;
                shadowOpacity = lightData.volumetricShadowDimmer;
            }
            else
            {
                value *= lightData.lightDimmer;
                shadowOpacity = lightData.shadowDimmer;
            }
        }
        else // lightType == PTLIGHT_SKY
        {
            float2 uv = SampleSky(inputSample.xy);
            outgoingDir = MapUVToSkyDirection(uv);
            value = GetSkyValue(outgoingDir);
            pdf = GetSkyLightWeight(lightList) * GetSkyPDFFromValue(value);

            shadowOpacity = 1.0;
        }

        if (!isSpherical && (dot(normal, outgoingDir) < DOT_PRODUCT_EPSILON))
            return false;

        dist = FLT_INF;

#ifndef LIGHT_EVALUATION_NO_HEIGHT_FOG
        ApplyFogAttenuation(position, outgoingDir, value);
#endif
    }

    return any(value) && any(pdf);
}

void EvaluateRectAreaLight(LightList lightList,
                           LightData lightData,
                           RayDesc rayDescriptor,
                           float t,
                           float cosTheta,
                           float3 hitPosition, // Hit position is relative to lightCenter
                     inout float3 value,
                     inout float pdf)
{
    // Then check if we are within the rectangle bounds
    float centerU = dot(hitPosition, lightData.right) / (lightData.size.x * Length2(lightData.right));
    float centerV = dot(hitPosition, lightData.up) / (lightData.size.y * Length2(lightData.up));
    if (abs(centerU) < 0.5 && abs(centerV) < 0.5)
    {
        float3 lightCenter = lightData.positionRWS;
        float t2 = Sq(t);
        float3 lightValue = GetAreaEmission(lightData, centerU, centerV, t2);
#ifndef LIGHT_EVALUATION_NO_HEIGHT_FOG
        ApplyFogAttenuation(rayDescriptor.Origin, rayDescriptor.Direction, t, lightValue);
#endif

#ifndef SAMPLE_SOLID_ANGLE
        float lightArea = length(cross(lightData.size.x * lightData.right, lightData.size.y * lightData.up));
        value += lightValue;
        pdf += GetLocalLightWeight(lightList) * t2 / (lightArea * cosTheta);
#else
        float3 position = rayDescriptor.Origin;

        SphQuad squad;
        lightCenter = lightCenter - 0.5 * lightData.size.x * lightData.right;
        lightCenter = lightCenter - 0.5 * lightData.size.y * lightData.up;
        SphQuadInit(lightCenter, lightData.size.x * lightData.right, lightData.size.y * lightData.up, position, squad);

        // TODO: Move this validity check into the common quad initialization function
        if (!(squad.S < 0.00001 || isnan(squad.S)))
        {
            value += lightValue;
            pdf += GetLocalLightWeight(lightList) / squad.S;
        }
#endif
    }
}

void EvaluateDiscAreaLight(LightList lightList,
                           LightData lightData,
                           RayDesc rayDescriptor,
                           float t,
                           float cosTheta,
                           float3 hitPosition, // Hit position is relative to lightCenter
                     inout float3 value,
                     inout float pdf)
{
    float lightRadius = lightData.size.x;
    float lightRadiusSquared = lightRadius*lightRadius;

    // Then check if we are within the disc bounds
    if (Length2(hitPosition) < lightRadiusSquared)
    {
        float2 centerUV = GetDiscAreaLightCookieUV(lightData, hitPosition);
        float t2 = Sq(t);
        float3 lightValue = GetAreaEmission(lightData, centerUV.x, centerUV.y, t2);
#ifndef LIGHT_EVALUATION_NO_HEIGHT_FOG
        ApplyFogAttenuation(rayDescriptor.Origin, rayDescriptor.Direction, t, lightValue);
#endif

        float lightArea = PI * lightRadiusSquared;
        value += lightValue;
        pdf += GetLocalLightWeight(lightList) * t2 / (lightArea * cosTheta);
    }
}

void EvaluateLights(LightList lightList,
                    RayDesc rayDescriptor,
                    out float3 value,
                    out float pdf)
{
    value = 0.0;
    pdf = 0.0;

    uint i;

    // First local lights (area lights only, as we consider the probability of hitting a point light neglectable)
    for (i = lightList.localPointCount; i < lightList.localCount; i++)
    {
        LightData lightData = GetLocalLightData(lightList, i);
        // Similarly, tube lights are line shaped so have no surface so neglect them as well
        if (lightData.lightType == GPULIGHTTYPE_TUBE)
            continue;

        float t = rayDescriptor.TMax;
        float cosTheta = -dot(rayDescriptor.Direction, lightData.forward);
        float3 lightCenter = lightData.positionRWS;

        // Check if we hit the light plane, at a distance below our tMax (coming from indirect computation)
        if (cosTheta > 0.0 && IntersectPlane(rayDescriptor.Origin, rayDescriptor.Direction, lightCenter, lightData.forward, t))
        {
            if (t < rayDescriptor.TMax)
            {
                float3 hitVec = rayDescriptor.Origin + t * rayDescriptor.Direction - lightCenter;

                if (lightData.lightType == GPULIGHTTYPE_RECTANGLE)
                {
                    EvaluateRectAreaLight(lightList, lightData, rayDescriptor, t, cosTheta, hitVec, value, pdf);
                }
                else
                {
                    EvaluateDiscAreaLight(lightList, lightData, rayDescriptor, t, cosTheta, hitVec, value, pdf);
                }
            }
        }
    }

    // Then distant lights
    for (i = 0; i < lightList.distantCount; i++)
    {
        DirectionalLightData lightData = GetDistantLightData(lightList, i);

        if (lightData.angularDiameter > 0.0 && rayDescriptor.TMax >= FLT_INF)
        {
            float cosHalfAngle = cos(lightData.angularDiameter * 0.5);
            float cosTheta = -dot(rayDescriptor.Direction, lightData.forward);
            if (cosTheta >= cosHalfAngle)
            {
                float3 lightValue = GetDirectionalEmission(lightData, rayDescriptor.Direction);
#ifndef LIGHT_EVALUATION_NO_HEIGHT_FOG
                ApplyFogAttenuation(rayDescriptor.Origin, rayDescriptor.Direction, lightValue);
#endif
                float rcpPdf = TWO_PI * (1.0 - cosHalfAngle);
                value += lightValue / rcpPdf;
                pdf += GetDistantLightWeight(lightList) / rcpPdf;
            }
        }
    }

    // Then sky light
    if (lightList.skyCount && rayDescriptor.TMax >= FLT_INF)
    {
        float3 skyValue = GetSkyValue(rayDescriptor.Direction);
        pdf += GetSkyLightWeight(lightList) * GetSkyPDFFromValue(skyValue);
#ifndef LIGHT_EVALUATION_NO_HEIGHT_FOG
        ApplyFogAttenuation(rayDescriptor.Origin, rayDescriptor.Direction, skyValue);
#endif
        value += skyValue;
    }
}

// Functions used by volumetric sampling

bool GetSphereInterval(float3 lightToRayOrigin, float radius, float3 rayDirection, out float tMin, out float tMax)
{
    // We consider Direction to be normalized => a = 1
    float b = 2.0 * dot(rayDirection, lightToRayOrigin);
    float c = Length2(lightToRayOrigin) - Sq(radius);

    float2 t;
    if (!SolveQuadraticEquation(1.0, b, c, t))
        return false;

    tMin = max(t.x, 0.0);
    tMax = max(t.y, 0.0);

    return tMin < tMax;
}

bool GetRectAreaLightInterval(LightData lightData, float3 rayOrigin, float3 rayDirection, out float tMin, out float tMax)
{
    if (lightData.volumetricLightDimmer < 0.001)
        return false;

    float3 lightToRayOrigin = rayOrigin - lightData.positionRWS;

    if (!GetSphereInterval(lightToRayOrigin, lightData.range, rayDirection, tMin, tMax))
        return false;

    float LdotD = dot(lightData.forward, rayDirection);
    float t = -dot(lightData.forward, lightToRayOrigin) / LdotD;
    if (LdotD > 0.0)
        tMin = max(tMin, t);
    else
        tMax = min(tMax, t);

    return tMin < tMax;
}

void Sort(inout float x, inout float y)
{
    if (x > y) Swap(x, y);
}

void GetFrontInterval(float oz, float dz, float t1, float t2, inout float tMin, inout float tMax)
{
    bool t1Valid = oz + t1 * dz > 0.0;
    bool t2Valid = oz + t2 * dz > 0.0;

    if (t1Valid)
    {
        if (t2Valid)
        {
            tMin = max(t1, tMin);
            tMax = min(t2, tMax);
        }
        else
        {
            tMax = min(t1, tMax);
        }
    }
    else
    {
        tMin = t2Valid ? max(t2, tMin) : tMax;
    }
}

bool GetPointLightInterval(LightData lightData, float3 rayOrigin, float3 rayDirection, out float tMin, out float tMax)
{
    if (lightData.volumetricLightDimmer < 0.001)
        return false;

    float3 lightToRayOrigin = rayOrigin - lightData.positionRWS;

    if (!GetSphereInterval(lightToRayOrigin, lightData.range, rayDirection, tMin, tMax))
        return false;

    // This is just a point light (no spot cone angle)
    if (lightData.lightType == GPULIGHTTYPE_POINT)
        return true;

    // We are dealing with either a cone, a pyramid or a box
    float3 localOrigin = float3(dot(lightToRayOrigin, lightData.right),
                                dot(lightToRayOrigin, lightData.up),
                                dot(lightToRayOrigin, lightData.forward));
    float3 localDirection = float3(dot(rayDirection, lightData.right),
                                   dot(rayDirection, lightData.up),
                                   dot(rayDirection, lightData.forward));

    if (lightData.lightType == GPULIGHTTYPE_PROJECTOR_BOX)
    {
        // Compute intersections with planes x=-1 and x=1
        float tx1 = (-1.0 - localOrigin.x) / localDirection.x;
        float tx2 = (1.0 - localOrigin.x) / localDirection.x;
        Sort(tx1, tx2);

        // Compute intersections with planes y=-1 and y=1
        float ty1 = (-1.0 - localOrigin.y) / localDirection.y;
        float ty2 = (1.0 - localOrigin.y) / localDirection.y;
        Sort(ty1, ty2);

        // Compute intersection with plane z=0
        float tz = -localOrigin.z / localDirection.z;

        float t1 = max(tx1, ty1);
        float t2 = min(tx2, ty2);

        // Check validity of the intersections (we want them only in front of the light)
        bool t1Valid = localOrigin.z + t1 * localDirection.z > 0.0;
        bool t2Valid = localOrigin.z + t2 * localDirection.z > 0.0;

        tMin = t1Valid ? max(t1, tMin) : tz;
        tMax = t2Valid ? min(t2, tMax) : tz;
    }
    else if (lightData.lightType == GPULIGHTTYPE_PROJECTOR_PYRAMID)
    {
        // Compute intersections with planes x=-z and x=z
        float tx1 = -(localOrigin.x - localOrigin.z) / (localDirection.x - localDirection.z);
        float tx2 = -(localOrigin.x + localOrigin.z) / (localDirection.x + localDirection.z);
        Sort(tx1, tx2);

        // Check validity of the intersections (we want them only in front of the light)
        GetFrontInterval(localOrigin.z, localDirection.z, tx1, tx2, tMin, tMax);

        if (tMin < tMax)
        {
            // Compute intersections with planes y=-1 and y=1
            float ty1 = -(localOrigin.y - localOrigin.z) / (localDirection.y - localDirection.z);
            float ty2 = -(localOrigin.y + localOrigin.z) / (localDirection.y + localDirection.z);
            Sort(ty1, ty2);

            // Check validity of the intersections (we want them only in front of the light)
            GetFrontInterval(localOrigin.z, localDirection.z, ty1, ty2, tMin, tMax);
        }
    }
    else // lightData.lightType == GPULIGHTTYPE_SPOT
    {
        float cosTheta2 = Sq(lightData.angleOffset / lightData.angleScale);

        // Offset light origin to account for light radius
        localOrigin.z += sqrt(lightData.size.x / (1.0 - cosTheta2));

        // Account for non-normalized local basis
        float3 normalizedLocalOrigin = float3(localOrigin.x / Length2(lightData.right),
                                              localOrigin.y / Length2(lightData.up),
                                              localOrigin.z);

        float a = Sq(localDirection.z) - cosTheta2;
        float b = 2.0 * (localOrigin.z * localDirection.z - dot(normalizedLocalOrigin, localDirection) * cosTheta2);
        float c = Sq(localOrigin.z) - dot(normalizedLocalOrigin, localOrigin) * cosTheta2;

        float2 t;
        if (!SolveQuadraticEquation(a, b, c, t))
            return false;

        // Check validity of the intersections (we want them only in front of the light)
        GetFrontInterval(localOrigin.z, localDirection.z, t.x, t.y, tMin, tMax);
    }

    return tMin < tMax;
}

// This function has been deprecated in favor of PickLocalLightInterval() right below
// float GetLocalLightsInterval(float3 rayOrigin, float3 rayDirection, out float tMin, out float tMax)
// {
//     tMin = FLT_MAX;
//     tMax = 0.0;

//     float tLightMin, tLightMax;

//     // First process point lights
//     uint i = 0, n = _PunctualLightCountRT, localCount = 0;
//     for (; i < n; i++)
//     {
//         if (GetPointLightInterval(_LightDatasRT[i], rayOrigin, rayDirection, tLightMin, tLightMax))
//         {
//             tMin = min(tMin, tLightMin);
//             tMax = max(tMax, tLightMax);
//             localCount++;
//         }
//     }

//     // Then area lights
//     n += _AreaLightCountRT;
//     for (; i < n; i++)
//     {
//         if (GetRectAreaLightInterval(_LightDatasRT[i], rayOrigin, rayDirection, tLightMin, tLightMax))
//         {
//             tMin = min(tMin, tLightMin);
//             tMax = max(tMax, tLightMax);
//             localCount++;
//         }
//     }

//     uint lightCount = localCount + _DirectionalLightCount;

//     return lightCount ? float(localCount) / lightCount : -1.0;
// }

float GetLocalLightWeight(LightData lightData, float3 rayOrigin, float3 rayDirection, float tMin, float tMax)
{
    float tDist = clamp(dot(lightData.positionRWS - rayOrigin, rayDirection), tMin, tMax);
    float3 vDist = rayOrigin + tDist * rayDirection - lightData.positionRWS;

    // By offsetting the square distance by 1.0, we reduce the range of the weight to ]0.0,  1.0],
    // while avoiding a singularity when distance goes towards 0.0.
    float distSq = 1.0 + Length2(vDist);

    return rcp(distSq);
}

float PickLocalLightInterval(float3 rayOrigin, float3 rayDirection, inout float inputSample, out float3 lightPosition, out float lightWeight, out float tMin, out float tMax)
{
    tMin = FLT_MAX;
    tMax = 0.0;

    float tLightMin, tLightMax;
    float wLight, wSum = 0.0;

    // First process point lights
    uint i = 0, n = _PunctualLightCountRT, localCount = 0;
    for (; i < n; i++)
    {
        if (GetPointLightInterval(_LightDatasRT[i], rayOrigin, rayDirection, tLightMin, tLightMax))
        {
            wLight = GetLocalLightWeight(_LightDatasRT[i], rayOrigin, rayDirection, tLightMin, tLightMax);

            if (wLight > 0.0)
            {
                wSum += wLight;
                wLight /= wSum;

                if (inputSample < wLight)
                {
                    lightPosition = _LightDatasRT[i].positionRWS;
                    lightWeight = wLight;
                    tMin = tLightMin;
                    tMax = tLightMax;

                    inputSample = RescaleSampleUnder(inputSample, wLight);
                }
                else
                {
                    lightWeight *= 1.0 - wLight;

                    inputSample = RescaleSampleOver(inputSample, wLight);
                }

                localCount++;
            }
        }
    }

    // Then area lights
    n += _AreaLightCountRT;
    for (; i < n; i++)
    {
        if (GetRectAreaLightInterval(_LightDatasRT[i], rayOrigin, rayDirection, tLightMin, tLightMax))
        {
            wLight = GetLocalLightWeight(_LightDatasRT[i], rayOrigin, rayDirection, tLightMin, tLightMax);

            if (wLight > 0.0)
            {
                wSum += wLight;
                wLight /= wSum;

                if (inputSample < wLight)
                {
                    lightPosition = _LightDatasRT[i].positionRWS;
                    lightWeight = wLight;
                    tMin = tLightMin;
                    tMax = tLightMax;

                    inputSample = RescaleSampleUnder(inputSample, wLight);
                }
                else
                {
                    lightWeight *= 1.0 - wLight;

                    inputSample = RescaleSampleOver(inputSample, wLight);
                }

                localCount++;
            }
        }
    }

    uint lightCount = localCount + _DirectionalLightCount + (IsSkyEnabled() && IsSkySamplingEnabled() ? 1 : 0);

    return lightCount ? float(localCount) / lightCount : -1.0;
}

LightList CreateLightList(float3 position, bool sampleLocalLights, float3 lightPosition = FLT_MAX)
{
    return CreateLightList(position, 0.0, RENDERING_LAYERS_MASK, sampleLocalLights, sampleLocalLights, !sampleLocalLights, lightPosition);
}

#endif // UNITY_PATH_TRACING_LIGHT_INCLUDED
