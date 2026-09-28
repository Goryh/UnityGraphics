using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    // Stores the data of all enabled SphericalLights in the layout used by the shaders, so that the renderer can cull
    // and upload them without touching the components. The arrays are packed: removing a light moves the last one
    // into its slot.
    static class SphericalLightRegistry
    {
        const int k_InitialCapacity = 64;

        static SphericalLight[] s_Lights = new SphericalLight[k_InitialCapacity];
        static NativeArray<float4> s_PositionRanges;    // xyz: world position, w: range
        static NativeArray<float4> s_ColorAreaRadii;    // rgb: color * intensity in the active color space, w: area radius
        static NativeArray<uint> s_ExclusionMasks;
        static int s_Count;

        internal static int count => s_Count;
        internal static NativeArray<float4> positionRanges => s_PositionRanges;
        internal static NativeArray<float4> colorAreaRadii => s_ColorAreaRadii;
        internal static NativeArray<uint> exclusionMasks => s_ExclusionMasks;

        internal static void Register(SphericalLight light)
        {
            if (light.registryIndex >= 0)
                return;

            if (!s_PositionRanges.IsCreated || s_Count == s_PositionRanges.Length)
                Resize(math.max(k_InitialCapacity, s_Count * 2));

            int index = s_Count++;
            s_Lights[index] = light;
            light.registryIndex = index;
            Write(index, light);
        }

        internal static void Unregister(SphericalLight light)
        {
            int index = light.registryIndex;
            if (index < 0)
                return;

            int last = --s_Count;
            if (index != last)
            {
                var moved = s_Lights[last];
                s_Lights[index] = moved;
                moved.registryIndex = index;
                s_PositionRanges[index] = s_PositionRanges[last];
                s_ColorAreaRadii[index] = s_ColorAreaRadii[last];
                s_ExclusionMasks[index] = s_ExclusionMasks[last];
            }

            s_Lights[last] = null;
            light.registryIndex = -1;

            // Also frees the native memory before domain reloads, as every light gets disabled first.
            if (s_Count == 0)
                Dispose();
        }

        internal static void Write(int index, SphericalLight light)
        {
            s_PositionRanges[index] = new float4((float3)light.transform.position, light.worldRange);

            // Matches VisibleLight.finalColor for the intensity mode URP sets up (linear intensity in linear color space).
            Color color = light.color;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                color = color.linear;
            color *= light.intensity;
            s_ColorAreaRadii[index] = new float4(color.r, color.g, color.b, light.worldAreaRadius);

            s_ExclusionMasks[index] = light.exclusionMask;
        }

        static void Resize(int capacity)
        {
            var positionRanges = new NativeArray<float4>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            var colorAreaRadii = new NativeArray<float4>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            var exclusionMasks = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if (s_PositionRanges.IsCreated)
            {
                NativeArray<float4>.Copy(s_PositionRanges, positionRanges, s_Count);
                NativeArray<float4>.Copy(s_ColorAreaRadii, colorAreaRadii, s_Count);
                NativeArray<uint>.Copy(s_ExclusionMasks, exclusionMasks, s_Count);
                Dispose();
            }

            s_PositionRanges = positionRanges;
            s_ColorAreaRadii = colorAreaRadii;
            s_ExclusionMasks = exclusionMasks;

            if (s_Lights.Length < capacity)
                System.Array.Resize(ref s_Lights, capacity);
        }

        static void Dispose()
        {
            if (s_PositionRanges.IsCreated)
            {
                s_PositionRanges.Dispose();
                s_ColorAreaRadii.Dispose();
                s_ExclusionMasks.Dispose();
            }
        }
    }
}
