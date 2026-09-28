using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Computes and submits lighting data to the GPU.
    /// </summary>
    public class ForwardLights
    {
        static class LightConstantBuffer
        {
            public static int _MainLightPosition;   // DeferredLights.LightConstantBuffer also refers to the same ShaderPropertyID - TODO: move this definition to a common location shared by other UniversalRP classes
            public static int _MainLightColor;      // DeferredLights.LightConstantBuffer also refers to the same ShaderPropertyID - TODO: move this definition to a common location shared by other UniversalRP classes
            public static int _MainLightOcclusionProbesChannel;    // Deferred?
            public static int _MainLightLayerMask;

            public static int _AdditionalLightsCount;
            public static int _AdditionalLightsPosition;
            public static int _AdditionalLightsColor;
            public static int _AdditionalLightsExtraData2;
        }

        const string k_SetupLightConstants = "Setup Light Constants";
        private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(k_SetupLightConstants);
        private static readonly ProfilingSampler m_ProfilingSamplerFPSetup = new ProfilingSampler("Forward+ Setup");
        private static readonly ProfilingSampler m_ProfilingSamplerFPComplete = new ProfilingSampler("Forward+ Complete");
        private static readonly ProfilingSampler m_ProfilingSamplerFPUpload = new ProfilingSampler("Forward+ Upload");
        MixedLightingSetup m_MixedLightingSetup;

        // Additional lights are point lights only. Their order in these arrays defines the additional light index used by the shaders.
        Vector4[] m_AdditionalLightPositions;   // xyz: position, w: radius (range)
        Vector4[] m_AdditionalLightColors;      // w: extra data 1
        float[] m_AdditionalLightsExtraData2;   // Unity has no support for binding uint arrays. We will use asuint() in the shader instead.
        int[] m_PointLightVisibleIndices;       // Maps an additional light index to its index in lightData.visibleLights.

        bool m_UseForwardPlus;
        int m_ActualTileWidth;
        int2 m_TileResolution;

        JobHandle m_CullingHandle;
        NativeArray<uint> m_TileLightIndices;
        GraphicsBuffer m_TileBuffer;
        int m_UsedTileWords;

        LightCookieManager m_LightCookieManager;
        ReflectionProbeManager m_ReflectionProbeManager;

        internal struct InitParams
        {
            public LightCookieManager lightCookieManager;
            public bool forwardPlus;

            static internal InitParams Create()
            {
                InitParams p;
                {
                    var settings = LightCookieManager.Settings.Create();
                    var asset = UniversalRenderPipeline.asset;
                    if (asset)
                    {
                        settings.atlas.format = asset.additionalLightsCookieFormat;
                        settings.atlas.resolution = asset.additionalLightsCookieResolution;
                    }

                    p.lightCookieManager = new LightCookieManager(ref settings);
                    p.forwardPlus = false;
                }
                return p;
            }
        }

        /// <summary>
        /// Creates a new <c>ForwardLights</c> instance.
        /// </summary>
        public ForwardLights() : this(InitParams.Create()) { }

        internal ForwardLights(InitParams initParams)
        {
            m_UseForwardPlus = initParams.forwardPlus;

            LightConstantBuffer._MainLightPosition = Shader.PropertyToID("_MainLightPosition");
            LightConstantBuffer._MainLightColor = Shader.PropertyToID("_MainLightColor");
            LightConstantBuffer._MainLightOcclusionProbesChannel = Shader.PropertyToID("_MainLightOcclusionProbes");
            LightConstantBuffer._MainLightLayerMask = Shader.PropertyToID("_MainLightLayerMask");
            LightConstantBuffer._AdditionalLightsCount = Shader.PropertyToID("_AdditionalLightsCount");

            LightConstantBuffer._AdditionalLightsPosition = Shader.PropertyToID("_AdditionalLightsPosition");
            LightConstantBuffer._AdditionalLightsColor = Shader.PropertyToID("_AdditionalLightsColor");
            LightConstantBuffer._AdditionalLightsExtraData2 = Shader.PropertyToID("_AdditionalLightsExtraData2");

            int maxLights = UniversalRenderPipeline.maxVisibleAdditionalLights;
            m_AdditionalLightPositions = new Vector4[maxLights];
            m_AdditionalLightColors = new Vector4[maxLights];
            m_AdditionalLightsExtraData2 = new float[maxLights];
            m_PointLightVisibleIndices = new int[maxLights];

            if (m_UseForwardPlus)
            {
                CreateForwardPlusBuffers();
                m_ReflectionProbeManager = ReflectionProbeManager.Create();
            }

            m_LightCookieManager = initParams.lightCookieManager;
        }

        void CreateForwardPlusBuffers()
        {
            m_TileLightIndices = new NativeArray<uint>(UniversalRenderPipeline.maxTiles * TileRangeExpansionJob.wordsPerTile, Allocator.Persistent);
            m_TileBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, UniversalRenderPipeline.maxTiles, UnsafeUtility.SizeOf<uint4>());
            m_TileBuffer.name = "URP Tile Buffer";
        }

        internal ReflectionProbeManager reflectionProbeManager => m_ReflectionProbeManager;

        static int AlignByteCount(int count, int align) => align * ((count + align - 1) / align);

        // Calculate view planes and viewToViewportScaleBias. This handles projection center in case the projection is off-centered
        void GetViewParams(Camera camera, float4x4 viewToClip, out float viewPlaneBot, out float viewPlaneTop, out float4 viewToViewportScaleBias)
        {
            // We want to calculate `fovHalfHeight = tan(fov / 2)`
            // `projection[1][1]` contains `1 / tan(fov / 2)`
            var viewPlaneHalfSizeInv = math.float2(viewToClip[0][0], viewToClip[1][1]);
            var viewPlaneHalfSize = math.rcp(viewPlaneHalfSizeInv);
            var centerClipSpace = camera.orthographic ? -math.float2(viewToClip[3][0], viewToClip[3][1]): math.float2(viewToClip[2][0], viewToClip[2][1]);

            viewPlaneBot = centerClipSpace.y * viewPlaneHalfSize.y - viewPlaneHalfSize.y;
            viewPlaneTop = centerClipSpace.y * viewPlaneHalfSize.y + viewPlaneHalfSize.y;
            viewToViewportScaleBias = math.float4(
                viewPlaneHalfSizeInv * 0.5f,
                -centerClipSpace * 0.5f + 0.5f
            );
        }

        internal void PreSetup(UniversalCameraData cameraData, UniversalLightData lightData)
        {
            if (m_UseForwardPlus)
            {
                using var _ = new ProfilingScope(m_ProfilingSamplerFPSetup);

                if (!m_CullingHandle.IsCompleted)
                {
                    throw new InvalidOperationException("Forward+ jobs have not completed yet.");
                }

                var camera = cameraData.camera;

                var screenResolution = math.int2(cameraData.pixelWidth, cameraData.pixelHeight);
#if ENABLE_VR && ENABLE_XR_MODULE
                var viewCount = cameraData.xr.enabled && cameraData.xr.singlePassEnabled ? 2 : 1;
#else
                var viewCount = 1;
#endif

                // Only point lights are tiled, in the same order as they are uploaded to the additional light arrays.
                var lightCount = GatherPointLights(lightData.visibleLights);

                // Grow the tile size in steps of 16 pixels until all tiles fit into the tile buffer.
                m_ActualTileWidth = 0;
                do
                {
                    m_ActualTileWidth += 16;
                    m_TileResolution = (screenResolution + m_ActualTileWidth - 1) / m_ActualTileWidth;
                }
                while (m_TileResolution.x * m_TileResolution.y * viewCount > UniversalRenderPipeline.maxTiles);

                m_UsedTileWords = m_TileResolution.x * m_TileResolution.y * viewCount * TileRangeExpansionJob.wordsPerTile;
                unsafe
                {
                    UnsafeUtility.MemClear(m_TileLightIndices.GetUnsafePtr(), m_UsedTileWords * sizeof(uint));
                }

                if (lightCount == 0)
                {
                    m_CullingHandle = default;
                    return;
                }

                var pointLights = new NativeArray<VisibleLight>(lightCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < lightCount; ++i)
                    pointLights[i] = lightData.visibleLights[m_PointLightVisibleIndices[i]];

                var worldToViews = new Fixed2<float4x4>(cameraData.GetViewMatrix(0), cameraData.GetViewMatrix(math.min(1, viewCount - 1)));
                var viewToClips = new Fixed2<float4x4>(cameraData.GetProjectionMatrix(0), cameraData.GetProjectionMatrix(math.min(1, viewCount - 1)));

                GetViewParams(camera, viewToClips[0], out float viewPlaneBottom0, out float viewPlaneTop0, out float4 viewToViewportScaleBias0);
                GetViewParams(camera, viewToClips[1], out float viewPlaneBottom1, out float viewPlaneTop1, out float4 viewToViewportScaleBias1);

                // Each light needs 1 range for Y, and a range per row. Align to 128-bytes to avoid false sharing.
                var rangesPerLight = AlignByteCount((1 + m_TileResolution.y) * UnsafeUtility.SizeOf<InclusiveRange>(), 128) / UnsafeUtility.SizeOf<InclusiveRange>();
                var tileRanges = new NativeArray<InclusiveRange>(rangesPerLight * lightCount * viewCount, Allocator.TempJob);
                var tilingJob = new TilingJob
                {
                    lights = pointLights,
                    tileRanges = tileRanges,
                    lightCount = lightCount,
                    rangesPerLight = rangesPerLight,
                    worldToViews = worldToViews,
                    tileScale = (float2)screenResolution / m_ActualTileWidth,
                    tileScaleInv = m_ActualTileWidth / (float2)screenResolution,
                    viewPlaneBottoms = new Fixed2<float>(viewPlaneBottom0, viewPlaneBottom1),
                    viewPlaneTops = new Fixed2<float>(viewPlaneTop0, viewPlaneTop1),
                    viewToViewportScaleBiases = new Fixed2<float4>(viewToViewportScaleBias0, viewToViewportScaleBias1),
                    tileCount = m_TileResolution,
                    near = camera.nearClipPlane,
                    isOrthographic = camera.orthographic
                };

                var tileRangeHandle = tilingJob.ScheduleParallel(lightCount * viewCount, 1, default);

                var expansionJob = new TileRangeExpansionJob
                {
                    tileRanges = tileRanges,
                    tileLightIndices = m_TileLightIndices,
                    rangesPerLight = rangesPerLight,
                    lightCount = lightCount,
                    tileResolution = m_TileResolution,
                };

                var tilingHandle = expansionJob.ScheduleParallel(m_TileResolution.y * viewCount, 1, tileRangeHandle);
                m_CullingHandle = JobHandle.CombineDependencies(tileRanges.Dispose(tilingHandle), pointLights.Dispose(tilingHandle));

                JobHandle.ScheduleBatchedJobs();
            }
        }

        /// <summary>
        /// Sets up the keywords and data for forward lighting.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="renderingData"></param>
        public void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            SetupLights(CommandBufferHelpers.GetUnsafeCommandBuffer(renderingData.commandBuffer), universalRenderingData, cameraData, lightData);
        }

        static ProfilingSampler s_SetupForwardLights = new ProfilingSampler("Setup Forward Lights");
        private class SetupLightPassData
        {
            internal UniversalRenderingData renderingData;
            internal UniversalCameraData cameraData;
            internal UniversalLightData lightData;
            internal ForwardLights forwardLights;
        };
        /// <summary>
        /// Sets up the ForwardLight data for RenderGraph execution
        /// </summary>
        internal void SetupRenderGraphLights(RenderGraph renderGraph, UniversalRenderingData renderingData, UniversalCameraData cameraData, UniversalLightData lightData)
        {
            using (var builder = renderGraph.AddUnsafePass<SetupLightPassData>(s_SetupForwardLights.name, out var passData,
                s_SetupForwardLights))
            {
                passData.renderingData = renderingData;
                passData.cameraData = cameraData;
                passData.lightData = lightData;
                passData.forwardLights = this;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((SetupLightPassData data, UnsafeGraphContext rgContext) =>
                {
                    data.forwardLights.SetupLights(rgContext.cmd, data.renderingData, data.cameraData, data.lightData);
                });
            }
        }

        internal void SetupLights(UnsafeCommandBuffer cmd, UniversalRenderingData renderingData, UniversalCameraData cameraData, UniversalLightData lightData)
        {
            int additionalLightsCount = lightData.additionalLightsCount;
            bool additionalLightsPerVertex = lightData.shadeAdditionalLightsPerVertex;
            using (new ProfilingScope(m_ProfilingSampler))
            {
                if (m_UseForwardPlus)
                {
                    m_ReflectionProbeManager.UpdateGpuData(CommandBufferHelpers.GetNativeCommandBuffer(cmd), ref renderingData.cullResults);

                    using (new ProfilingScope(m_ProfilingSamplerFPComplete))
                    {
                        m_CullingHandle.Complete();
                    }

                    using (new ProfilingScope(m_ProfilingSamplerFPUpload))
                    {
                        // Only the tiles in use are uploaded, the shader never reads past them.
                        var usedTiles = m_UsedTileWords / TileRangeExpansionJob.wordsPerTile;
                        m_TileBuffer.SetData(m_TileLightIndices.Reinterpret<uint4>(UnsafeUtility.SizeOf<uint>()), 0, 0, usedTiles);
                        cmd.SetGlobalConstantBuffer(m_TileBuffer, "urp_TileBuffer", 0, UniversalRenderPipeline.maxTiles * UnsafeUtility.SizeOf<uint4>());
                    }

                    cmd.SetGlobalVector("_FPParams0", math.float4(cameraData.pixelRect.size / m_ActualTileWidth, m_TileResolution.x, 0));
                    cmd.SetGlobalVector("_FPParams1", math.float4(m_TileResolution.x * m_TileResolution.y, 0, 0, 0));
                }

                SetupShaderLightConstants(cmd, lightData);

                bool lightCountCheck = (cameraData.renderer.stripAdditionalLightOffVariants && lightData.supportsAdditionalLights) || additionalLightsCount > 0;
                cmd.SetKeyword(ShaderGlobalKeywords.AdditionalLightsVertex, lightCountCheck && additionalLightsPerVertex && !m_UseForwardPlus);
                cmd.SetKeyword(ShaderGlobalKeywords.AdditionalLightsPixel,  lightCountCheck && !additionalLightsPerVertex && !m_UseForwardPlus);
                cmd.SetKeyword(ShaderGlobalKeywords.ForwardPlus, m_UseForwardPlus);

                // Mixed lighting (shadowmask and subtractive) is only supported for the main light.
                bool isShadowMask = lightData.supportsMixedLighting && m_MixedLightingSetup == MixedLightingSetup.ShadowMask;
                bool isShadowMaskAlways = isShadowMask && QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask;
                bool isSubtractive = lightData.supportsMixedLighting && m_MixedLightingSetup == MixedLightingSetup.Subtractive;
                cmd.SetKeyword(ShaderGlobalKeywords.LightmapShadowMixing, isSubtractive || isShadowMaskAlways);
                cmd.SetKeyword(ShaderGlobalKeywords.ShadowsShadowMask, isShadowMask);
                cmd.SetKeyword(ShaderGlobalKeywords.MixedLightingSubtractive, isSubtractive); // Backward compatibility

                cmd.SetKeyword(ShaderGlobalKeywords.ReflectionProbeBlending, lightData.reflectionProbeBlending);
                cmd.SetKeyword(ShaderGlobalKeywords.ReflectionProbeBoxProjection, lightData.reflectionProbeBoxProjection);

                var asset = UniversalRenderPipeline.asset;
                bool apvIsEnabled = asset != null && asset.lightProbeSystem == LightProbeSystem.ProbeVolumes;
                ProbeVolumeSHBands probeVolumeSHBands = asset.probeVolumeSHBands;

                cmd.SetKeyword(ShaderGlobalKeywords.ProbeVolumeL1, apvIsEnabled && probeVolumeSHBands == ProbeVolumeSHBands.SphericalHarmonicsL1);
                cmd.SetKeyword(ShaderGlobalKeywords.ProbeVolumeL2, apvIsEnabled && probeVolumeSHBands == ProbeVolumeSHBands.SphericalHarmonicsL2);

				// TODO: If we can robustly detect LIGHTMAP_ON, we can skip SH logic.
                var shMode = PlatformAutoDetect.ShAutoDetect(asset.shEvalMode);
                cmd.SetKeyword(ShaderGlobalKeywords.EVALUATE_SH_MIXED, shMode == ShEvalMode.Mixed);
                cmd.SetKeyword(ShaderGlobalKeywords.EVALUATE_SH_VERTEX, shMode == ShEvalMode.PerVertex);

                var stack = VolumeManager.instance.stack;
                bool enableProbeVolumes = ProbeReferenceVolume.instance.UpdateShaderVariablesProbeVolumes(
                    CommandBufferHelpers.GetNativeCommandBuffer(cmd),
                    stack.GetComponent<ProbeVolumesOptions>(),
                    cameraData.IsTemporalAAEnabled() ? Time.frameCount : 0,
                    lightData.supportsLightLayers);

                cmd.SetGlobalInt("_EnableProbeVolumes", enableProbeVolumes ? 1 : 0);
                cmd.SetKeyword(ShaderGlobalKeywords.LightLayers, lightData.supportsLightLayers && !CoreUtils.IsSceneLightingDisabled(cameraData.camera));

                if (m_LightCookieManager != null)
                {
                    m_LightCookieManager.Setup(CommandBufferHelpers.GetNativeCommandBuffer(cmd), lightData);
                }
                else
                {
                    cmd.SetKeyword(ShaderGlobalKeywords.LightCookies, false);
                }
            }
        }

        internal void Cleanup()
        {
            if (m_UseForwardPlus)
            {
                m_CullingHandle.Complete();
                m_TileLightIndices.Dispose();
                m_TileBuffer.Dispose();
                m_TileBuffer = null;
                m_ReflectionProbeManager.Dispose();
            }
            m_LightCookieManager?.Dispose();
            m_LightCookieManager = null;
        }

        void InitializeLightConstants(NativeArray<VisibleLight> lights, int lightIndex, bool supportsLightLayers, out Vector4 lightPos, out Vector4 lightColor, out Vector4 lightAttenuation, out Vector4 lightSpotDir, out Vector4 lightOcclusionProbeChannel, out uint lightLayerMask, out bool isSubtractive)
        {
            UniversalRenderPipeline.InitializeLightConstants_Common(lights, lightIndex, out lightPos, out lightColor, out lightAttenuation, out lightSpotDir, out lightOcclusionProbeChannel);
            lightLayerMask = 0;
            isSubtractive = false;

            // When no lights are visible, main light will be set to -1.
            // In this case we initialize it to default values and return
            if (lightIndex < 0)
                return;

            Light light = lights.UnsafeElementAtMutable(lightIndex).light;
            if (light == null)
                return;

            var lightBakingOutput = light.bakingOutput;
            isSubtractive = lightBakingOutput.isBaked && lightBakingOutput.lightmapBakeType == LightmapBakeType.Mixed && lightBakingOutput.mixedLightingMode == MixedLightingMode.Subtractive;

            if (lightBakingOutput.lightmapBakeType == LightmapBakeType.Mixed &&
                light.shadows != LightShadows.None &&
                m_MixedLightingSetup == MixedLightingSetup.None)
            {
                switch (lightBakingOutput.mixedLightingMode)
                {
                    case MixedLightingMode.Subtractive:
                        m_MixedLightingSetup = MixedLightingSetup.Subtractive;
                        break;
                    case MixedLightingMode.Shadowmask:
                        m_MixedLightingSetup = MixedLightingSetup.ShadowMask;
                        break;
                }
            }

            if (supportsLightLayers)
            {
                var additionalLightData = light.GetUniversalAdditionalLightData();
                lightLayerMask = RenderingLayerUtils.ToValidRenderingLayers(additionalLightData.renderingLayers);
            }
        }

        // Fills m_PointLightVisibleIndices with the visible point lights, which are the only additional lights sent to
        // the shaders, and returns their count. Their order defines the additional light index.
        int GatherPointLights(NativeArray<VisibleLight> visibleLights)
        {
            // Forward+ stores light indices as (index + 1) in a byte, 0 is reserved for "no light".
            int maxCount = math.min(m_PointLightVisibleIndices.Length, UniversalRenderPipeline.maxForwardPlusLights);
            int count = 0;
            for (int i = 0; i < visibleLights.Length && count < maxCount; ++i)
            {
                if (visibleLights.UnsafeElementAtMutable(i).lightType == LightType.Point)
                    m_PointLightVisibleIndices[count++] = i;
            }

            return count;
        }

        void SetupShaderLightConstants(UnsafeCommandBuffer cmd, UniversalLightData lightData)
        {
            m_MixedLightingSetup = MixedLightingSetup.None;

            // Main light has an optimized shader path for main light. This will benefit games that only care about a single light.
            // Universal pipeline also supports only a single shadow light, if available it will be the main light.
            SetupMainLightConstants(cmd, lightData);
            SetupAdditionalLightConstants(cmd, lightData);
        }

        void SetupMainLightConstants(UnsafeCommandBuffer cmd, UniversalLightData lightData)
        {
            Vector4 lightPos, lightColor, lightAttenuation, lightSpotDir, lightOcclusionChannel;
            bool supportsLightLayers = lightData.supportsLightLayers;
            uint lightLayerMask;
            bool isSubtractive;
            InitializeLightConstants(lightData.visibleLights, lightData.mainLightIndex, supportsLightLayers, out lightPos, out lightColor, out lightAttenuation, out lightSpotDir, out lightOcclusionChannel, out lightLayerMask, out isSubtractive);
            lightColor.w = isSubtractive ? 0f : 1f;

            cmd.SetGlobalVector(LightConstantBuffer._MainLightPosition, lightPos);
            cmd.SetGlobalVector(LightConstantBuffer._MainLightColor, lightColor);
            cmd.SetGlobalVector(LightConstantBuffer._MainLightOcclusionProbesChannel, lightOcclusionChannel);

            if (supportsLightLayers)
                cmd.SetGlobalInt(LightConstantBuffer._MainLightLayerMask, (int)lightLayerMask);
        }

        void SetupAdditionalLightConstants(UnsafeCommandBuffer cmd, UniversalLightData lightData)
        {
            var lights = lightData.visibleLights;
            int additionalLightsCount = GatherPointLights(lights);
            if (additionalLightsCount > 0)
            {
                for (int i = 0; i < additionalLightsCount; ++i)
                {
                    ref VisibleLight visibleLight = ref lights.UnsafeElementAtMutable(m_PointLightVisibleIndices[i]);
                    Light light = visibleLight.light;

                    Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
                    position.w = visibleLight.range;
                    m_AdditionalLightPositions[i] = position;

                    float extraData1 = 0.0f;
                    uint extraData2 = 0;
                    if (light != null && light.TryGetComponent(out UniversalAdditionalLightData additionalLightData))
                    {
                        extraData1 = additionalLightData.extraData1;
                        extraData2 = additionalLightData.extraData2;
                    }

                    // VisibleLight.finalColor already returns color in active color space
                    Vector4 color = visibleLight.finalColor;
                    color.w = extraData1;
                    m_AdditionalLightColors[i] = color;
                    m_AdditionalLightsExtraData2[i] = math.asfloat(extraData2);
                }

                cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsPosition, m_AdditionalLightPositions);
                cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsColor, m_AdditionalLightColors);
                cmd.SetGlobalFloatArray(LightConstantBuffer._AdditionalLightsExtraData2, m_AdditionalLightsExtraData2);

                cmd.SetGlobalVector(LightConstantBuffer._AdditionalLightsCount, new Vector4(additionalLightsCount, 0.0f, 0.0f, 0.0f));
            }
            else
            {
                cmd.SetGlobalVector(LightConstantBuffer._AdditionalLightsCount, Vector4.zero);
            }
        }
    }
}
