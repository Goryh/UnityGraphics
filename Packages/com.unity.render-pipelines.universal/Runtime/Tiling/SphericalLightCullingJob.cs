using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    // Culls the registered SphericalLights against the camera frustum(s), sorts the visible ones nearest first and
    // writes up to maxVisibleCount of them into the shader data layout. When a tile overflows, the lights dropped are
    // the ones farthest from the camera.
    [BurstCompile(FloatMode = FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    struct SphericalLightCullingJob : IJob
    {
        internal struct SortEntry : IComparable<SortEntry>
        {
            public float distance;
            public int index;

            public int CompareTo(SortEntry other) => distance.CompareTo(other.distance);
        }

        [ReadOnly] public NativeArray<float4> positionRanges;
        [ReadOnly] public NativeArray<float4> colorAreaRadii;
        [ReadOnly] public NativeArray<uint> exclusionMasks;
        public int lightCount;

        // 6 planes per view, xyz: normal pointing inside, w: distance.
        [ReadOnly] public NativeArray<float4> frustumPlanes;
        public int viewCount;
        public float3 cameraPosition;

        public NativeArray<SortEntry> sortEntries;

        // Laid out as [positions | colors | exclusion masks], each section `lightDataStride` entries long.
        public NativeArray<float4> lightData;
        public int lightDataStride;
        public int maxVisibleCount;
        public NativeArray<int> visibleCount;

        public void Execute()
        {
            int count = 0;
            for (int i = 0; i < lightCount; i++)
            {
                float4 positionRange = positionRanges[i];
                if (IsVisible(positionRange))
                {
                    float distance = math.distance(positionRange.xyz, cameraPosition) - positionRange.w;
                    sortEntries[count++] = new SortEntry { distance = distance, index = i };
                }
            }

            var visible = sortEntries.GetSubArray(0, count);
            visible.Sort();

            count = math.min(count, maxVisibleCount);
            for (int i = 0; i < count; i++)
            {
                int index = visible[i].index;
                lightData[i] = positionRanges[index];
                lightData[lightDataStride + i] = colorAreaRadii[index];
                lightData[2 * lightDataStride + i] = new float4(math.asfloat(exclusionMasks[index]), 0.0f, 0.0f, 0.0f);
            }

            visibleCount[0] = count;
        }

        bool IsVisible(float4 sphere)
        {
            for (int view = 0; view < viewCount; view++)
            {
                bool inside = true;
                for (int p = 0; p < 6 && inside; p++)
                {
                    float4 plane = frustumPlanes[view * 6 + p];
                    inside = math.dot(plane.xyz, sphere.xyz) + plane.w >= -sphere.w;
                }

                if (inside)
                    return true;
            }

            return false;
        }

        // Extracts the frustum planes from a world to clip matrix with OpenGL clip space conventions (Camera.projectionMatrix).
        internal static void GetFrustumPlanes(float4x4 worldToClip, NativeArray<float4> planes, int offset)
        {
            float4 row0 = math.float4(worldToClip.c0.x, worldToClip.c1.x, worldToClip.c2.x, worldToClip.c3.x);
            float4 row1 = math.float4(worldToClip.c0.y, worldToClip.c1.y, worldToClip.c2.y, worldToClip.c3.y);
            float4 row2 = math.float4(worldToClip.c0.z, worldToClip.c1.z, worldToClip.c2.z, worldToClip.c3.z);
            float4 row3 = math.float4(worldToClip.c0.w, worldToClip.c1.w, worldToClip.c2.w, worldToClip.c3.w);

            planes[offset + 0] = NormalizePlane(row3 + row0);
            planes[offset + 1] = NormalizePlane(row3 - row0);
            planes[offset + 2] = NormalizePlane(row3 + row1);
            planes[offset + 3] = NormalizePlane(row3 - row1);
            planes[offset + 4] = NormalizePlane(row3 + row2);
            planes[offset + 5] = NormalizePlane(row3 - row2);
        }

        static float4 NormalizePlane(float4 plane) => plane / math.length(plane.xyz);
    }
}
