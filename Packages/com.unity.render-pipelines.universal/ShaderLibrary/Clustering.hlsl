#ifndef UNIVERSAL_CLUSTERING_INCLUDED
#define UNIVERSAL_CLUSTERING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#if USE_FORWARD_PLUS

// Each screen tile is a single uint4 holding up to MAX_LIGHTS_PER_TILE byte-sized entries, packed from the lowest byte
// of x upwards. An entry is the index of the light in _AdditionalLightsData, where lights start at index 1. A zero byte
// terminates the list, and all bytes after it are zero as well.

// Iterate a tile with a rotated loop, so the only test per light is at the bottom:
//
//     ClusterIterator it;
//     uint lightIndex;
//     if (ClusterInitPixel(positionCS, it, lightIndex))
//     {
//         do { ... } while (ClusterNext(it, lightIndex));
//     }
//
// `continue` in the body jumps to ClusterNext(), so it still moves on to the next light.

// internal
struct ClusterIterator
{
    // Remaining entries of the tile. The entries of x are consumed first, from its lowest byte; when x is used up, the
    // next word moves in. After the end of the list all bytes are zero, so moving in more words keeps returning 0.
    uint4 entries;
};

// internal
// Returns the next entry of the tile, 0 at the end of the list.
uint ClusterPop(inout ClusterIterator it)
{
    uint lightIndex = it.entries.x & 0xFF;
    it.entries.x >>= 8;
    return lightIndex;
}

// Foveated rendering (non-uniform raster) and single-pass stereo are not supported: tiles cover a single view.

// internal
ClusterIterator ClusterInitTile(uint2 tileCoord)
{
    ClusterIterator it;
    it.entries = urp_Tiles[tileCoord.y * URP_FP_TILE_COUNT_X + tileCoord.x];
    return it;
}

// internal
// Tile coordinates of a pixel, given its SV_Position: the integer part is the tile, the fractional part the position
// inside it, with y going up. Applies the y flip for the current render target orientation.
float2 ClusterPixelToTile(float2 positionCS)
{
    return positionCS * URP_FP_PIXEL_TO_TILE_SCALE + URP_FP_PIXEL_TO_TILE_OFFSET;
}

// internal
// Finds the tile of a pixel, given its SV_Position, and returns its first light index. Returns false if the tile has
// no lights. Goes from the pixel position straight to tile coordinates with a single multiply-add, which also applies
// the y flip for the current render target orientation.
bool ClusterInitPixel(float2 positionCS, out ClusterIterator it)
{
    it = ClusterInitTile(uint2(ClusterPixelToTile(positionCS)));
    return it.entries.x != 0;
}

// internal
// Moves to the next light of the tile and returns its index, directly usable with the additional light accessors.
// Returns false at the end of the list.
bool ClusterNext(inout ClusterIterator it, out uint lightIndex)
{
    // The current word is used up (4 lights, or the end of the list): move the next one in. This happens at most once
    // every 4 lights, and in the same way for all pixels of a tile.
    if (it.entries.x == 0)
        it.entries = uint4(it.entries.yzw, 0);

    lightIndex = ClusterPop(it);
    return lightIndex != 0;
}

#endif

#endif
