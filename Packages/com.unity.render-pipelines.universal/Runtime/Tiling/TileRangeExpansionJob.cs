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

        [ReadOnly]
        public NativeArray<InclusiveRange> tileRanges;

        // Each tile is `wordsPerTile` words holding up to `UniversalRenderPipeline.maxLightsPerTile` bytes. Each byte is
        // (light index + 1), 0 means no light. Expected to be cleared to 0.
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> tileLightIndices;

        public int rangesPerLight;
        public int lightCount;
        public int2 tileResolution;

        public void Execute(int jobIndex)
        {
            var rowIndex = jobIndex % tileResolution.y;
            var viewIndex = jobIndex / tileResolution.y;
            var rowBaseWordIndex = (viewIndex * tileResolution.y + rowIndex) * tileResolution.x * wordsPerTile;
            var tileLightCounts = new NativeArray<byte>(tileResolution.x, Allocator.Temp);

            // Lights are appended in index order, so when a tile is full the remaining higher indexed lights are dropped.
            for (var lightIndex = 0; lightIndex < lightCount; lightIndex++)
            {
                var range = tileRanges[(viewIndex * lightCount + lightIndex) * rangesPerLight + 1 + rowIndex];
                if (range.isEmpty)
                    continue;

                var entry = (uint)(lightIndex + 1);
                for (int tileX = range.start; tileX <= range.end; tileX++)
                {
                    int count = tileLightCounts[tileX];
                    if (count == UniversalRenderPipeline.maxLightsPerTile)
                        continue;

                    var wordIndex = rowBaseWordIndex + tileX * wordsPerTile + (count >> 2);
                    tileLightIndices[wordIndex] |= entry << ((count & 3) * 8);
                    tileLightCounts[tileX] = (byte)(count + 1);
                }
            }

            tileLightCounts.Dispose();
        }
    }
}
