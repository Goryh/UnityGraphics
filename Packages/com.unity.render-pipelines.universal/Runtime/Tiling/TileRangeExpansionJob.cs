using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    [BurstCompile(FloatMode = FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    struct TileRangeExpansionJob : IJobFor
    {
        public const int wordsPerTile = 4;
        public const int entriesPerTile = 16;   // Entries of a tile in each of the two tile buffers.

        [ReadOnly]
        public NativeArray<InclusiveRange> tileRanges;

        // Each tile is `wordsPerTile` words holding up to `entriesPerTile` bytes. Each byte is the light's index in the
        // AdditionalLights constant buffer, where lights start at 1, and 0 means no light. Entries 16 to 31 of a tile go
        // to the same tile at `overflowWordOffset` (urp_TilesOverflow). Expected to be cleared to 0.
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> tileLightIndices;
        public int overflowWordOffset;

        // Single element, set to 1 when any tile uses its overflow entries. Expected to be cleared to 0. Rows run in
        // parallel but only ever write 1, so they can share it.
        [NativeDisableParallelForRestriction]
        public NativeArray<int> hasOverflow;

        public int rangesPerLight;
        public int lightCount;
        public int2 tileResolution;

        public void Execute(int jobIndex)
        {
            var rowIndex = jobIndex % tileResolution.y;
            var viewIndex = jobIndex / tileResolution.y;
            var rowBaseWordIndex = (viewIndex * tileResolution.y + rowIndex) * tileResolution.x * wordsPerTile;
            var tileLightCounts = new NativeArray<byte>(tileResolution.x, Allocator.Temp);
            bool rowHasOverflow = false;

            // Lights are appended in index order, so when a tile is full the remaining higher indexed lights are dropped.
            // Index order is the culling job's sort by nearest sphere distance, which shaders rely on for their early out.
            for (var lightIndex = 0; lightIndex < lightCount; lightIndex++)
            {
                var range = tileRanges[(viewIndex * lightCount + lightIndex) * rangesPerLight + 1 + rowIndex];
                if (range.isEmpty)
                    continue;

                // Tiled light i is stored at index i + 1 of the AdditionalLights constant buffer.
                var entry = (uint)(lightIndex + 1);
                for (int tileX = range.start; tileX <= range.end; tileX++)
                {
                    int count = tileLightCounts[tileX];
                    if (count == UniversalRenderPipeline.maxLightsPerTile)
                        continue;

                    var wordIndex = rowBaseWordIndex + tileX * wordsPerTile + ((count % entriesPerTile) >> 2);
                    if (count >= entriesPerTile)
                        wordIndex += overflowWordOffset;

                    // Shaders read the overflow tile of every tile whose 16 entries in urp_Tiles are all used, so the
                    // overflow tiles must be uploaded from then on, even if this one stays empty.
                    if (count >= entriesPerTile - 1)
                        rowHasOverflow = true;

                    tileLightIndices[wordIndex] |= entry << ((count & 3) * 8);
                    tileLightCounts[tileX] = (byte)(count + 1);
                }
            }

            // Written once per row, to keep the rows from contending on the shared flag.
            if (rowHasOverflow)
                hasOverflow[0] = 1;

            tileLightCounts.Dispose();
        }
    }
}
