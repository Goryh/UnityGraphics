#ifndef UNIVERSAL_CLUSTERING_INCLUDED
#define UNIVERSAL_CLUSTERING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"

#if USE_FORWARD_PLUS
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"

// Each screen tile is a single uint4 holding up to MAX_LIGHTS_PER_TILE byte-sized entries, packed from the lowest byte
// of x upwards. An entry stores (light index + 1), where the light index is relative to the first non-directional
// additional light. A zero byte terminates the list, and all bytes after it are zero as well.

// internal
struct ClusterIterator
{
    // Remaining entries of the tile, the next one is always in the lowest byte of x.
    uint4 entries;
};

// internal
ClusterIterator ClusterInit(float2 normalizedScreenSpaceUV)
{
#if defined(SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
    UNITY_BRANCH if (_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
    {
#if UNITY_UV_STARTS_AT_TOP
        // RemapFoveatedRenderingNonUniformToLinear expects the UV coordinate to be non-flipped, so we un-flip it before
        // the call, and then flip it back afterwards.
        normalizedScreenSpaceUV.y = 1.0 - normalizedScreenSpaceUV.y;
#endif
        normalizedScreenSpaceUV = RemapFoveatedRenderingNonUniformToLinear(normalizedScreenSpaceUV);
#if UNITY_UV_STARTS_AT_TOP
        normalizedScreenSpaceUV.y = 1.0 - normalizedScreenSpaceUV.y;
#endif
    }
#endif // SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER

    uint2 tileCoord = uint2(normalizedScreenSpaceUV * URP_FP_TILE_SCALE);
    uint tileIndex = tileCoord.y * URP_FP_TILE_COUNT_X + tileCoord.x;
#if defined(USING_STEREO_MATRICES)
    tileIndex += URP_FP_TILE_COUNT * unity_StereoEyeIndex;
#endif

    ClusterIterator it;
    it.entries = urp_Tiles[tileIndex];
    return it;
}

// internal
// Returns the next light index (relative to the first non-directional additional light) of the tile.
bool ClusterNext(inout ClusterIterator it, out uint lightIndex)
{
    uint entry = it.entries.x & 0xFF;
    lightIndex = entry - 1;

    // Shift the whole 128-bit entry list down by one byte.
    it.entries = uint4(
        (it.entries.x >> 8) | (it.entries.y << 24),
        (it.entries.y >> 8) | (it.entries.z << 24),
        (it.entries.z >> 8) | (it.entries.w << 24),
        (it.entries.w >> 8));

    return entry != 0;
}

#endif

#endif
