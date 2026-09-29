#ifndef UNIVERSAL_CLUSTERING_INCLUDED
#define UNIVERSAL_CLUSTERING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#if USE_FORWARD_PLUS

// Each screen tile is a single uint4 holding up to MAX_LIGHTS_PER_TILE byte-sized entries, packed from the lowest byte
// of x upwards. An entry is the index of the light in _AdditionalLightsData, where lights start at index 1. A zero byte
// terminates the list, and all bytes after it are zero as well.

// internal
struct ClusterIterator
{
    // Remaining entries of the tile, the next one is always in the lowest byte of x.
    uint4 entries;
};

// Foveated rendering (non-uniform raster) and single-pass stereo are not supported: tiles cover a single view.

// internal
ClusterIterator ClusterInitTile(uint2 tileCoord)
{
    ClusterIterator it;
    it.entries = urp_Tiles[tileCoord.y * URP_FP_TILE_COUNT_X + tileCoord.x];
    return it;
}

// internal
// Finds the tile of a pixel, given its SV_Position. Goes from the pixel position straight to tile coordinates with a
// single scale, giving the same tile as scaling GetNormalizedScreenSpaceUV(positionCS).
ClusterIterator ClusterInitPixel(float2 positionCS)
{
    float2 tile = positionCS * URP_FP_TILES_PER_PIXEL;
#if UNITY_UV_STARTS_AT_TOP
    // Same flip as TransformNormalizedScreenUV(), in tile units. _ScaleBiasRt depends on the render target, so the flip
    // can't be folded into the scale on the CPU.
    tile.y = URP_FP_TILE_SCALE_Y - (tile.y * _ScaleBiasRt.x + _ScaleBiasRt.y * URP_FP_TILE_SCALE_Y);
#endif
    return ClusterInitTile(uint2(tile));
}

// internal
// Returns the next light index of the tile, directly usable with the additional light accessors.
bool ClusterNext(inout ClusterIterator it, out uint lightIndex)
{
    uint entry = it.entries.x & 0xFF;
    lightIndex = entry;

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
