using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    // GPU data of a spherical light. Must match the two float4 per light of _AdditionalLightsData in Input.hlsl.
    [StructLayout(LayoutKind.Sequential)]
    struct SphericalLightData
    {
        public float4 positionRange;    // xyz: world position, w: range
        public float4 color;            // rgb: color * intensity in the active color space, w: packed area radius and exclusion mask
    }

    // Stores the data of all enabled SphericalLights in the layout used by the shaders, so that the renderer can cull
    // and upload them without touching the components. The array is packed: removing a light moves the last one
    // into its slot.
    static class SphericalLightRegistry
    {
        const int k_InitialCapacity = 64;

        // Largest finite half, the area radius is stored as a half.
        const float k_MaxAreaRadius = 65504.0f;

        static SphericalLight[] s_Lights = new SphericalLight[k_InitialCapacity];
        static NativeArray<SphericalLightData> s_Data;
        static int s_Count;

        internal static int count => s_Count;
        internal static NativeArray<SphericalLightData> data => s_Data;

        internal static void Register(SphericalLight light)
        {
            if (light.registryIndex >= 0)
                return;

            if (!s_Data.IsCreated || s_Count == s_Data.Length)
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
                s_Data[index] = s_Data[last];
            }

            s_Lights[last] = null;
            light.registryIndex = -1;

            // Also frees the native memory before domain reloads, as every light gets disabled first.
            if (s_Count == 0)
                Dispose();
        }

        internal static void Write(int index, SphericalLight light)
        {
            // Matches VisibleLight.finalColor for the intensity mode URP sets up (linear intensity in linear color space).
            Color color = light.color;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                color = color.linear;
            color *= light.intensity;

            float areaRadius = light.worldAreaRadius;
            Debug.Assert(areaRadius <= k_MaxAreaRadius, $"Spherical light '{light.name}' area radius {areaRadius} exceeds {k_MaxAreaRadius}, the largest value it can be stored with.", light);

            // The area radius as a half in the low 16 bits, the exclusion mask in the high 16 bits.
            uint areaRadiusAndMask = math.f32tof16(math.min(areaRadius, k_MaxAreaRadius)) | ((uint)light.exclusionMask << 16);

            s_Data[index] = new SphericalLightData
            {
                positionRange = new float4((float3)light.transform.position, light.worldRange),
                color = new float4(color.r, color.g, color.b, math.asfloat(areaRadiusAndMask)),
            };
        }

        static void Resize(int capacity)
        {
            var data = new NativeArray<SphericalLightData>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if (s_Data.IsCreated)
            {
                NativeArray<SphericalLightData>.Copy(s_Data, data, s_Count);
                Dispose();
            }

            s_Data = data;

            if (s_Lights.Length < capacity)
                System.Array.Resize(ref s_Lights, capacity);
        }

        static void Dispose()
        {
            if (s_Data.IsCreated)
                s_Data.Dispose();
        }
    }
}
