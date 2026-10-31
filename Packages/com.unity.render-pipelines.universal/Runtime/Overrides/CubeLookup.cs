using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// A volume component that holds settings for the cube lookup effect.
    /// </summary>
    /// <remarks>
    /// The lookup texture is a 3D texture imported from a .cube file, see <c>CubeLutImporter</c>. The importer
    /// resamples the .cube onto the LUT space of the internal HDR grading LUT (LogC, input and output), so the
    /// effect is baked into that LUT by <c>LutBuilderHdr</c>, before the YRGB curves. It is only supported with
    /// the High Dynamic Range grading mode.
    /// </remarks>
    [Serializable, VolumeComponentMenu("Post-processing/Cube Lookup")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("integration-with-post-processing")]
    public sealed class CubeLookup : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// A 3D Lookup Texture (LUT) imported from a .cube file.
        /// </summary>
        [Tooltip("A 3D Lookup Texture (LUT) imported from a .cube file.")]
        public TextureParameter texture = new TextureParameter(null, TextureDimension.Tex3D);

        /// <summary>
        /// Controls how much of the lookup texture will contribute to the color grading effect.
        /// </summary>
        [Tooltip("How much of the lookup texture will contribute to the color grading effect.")]
        public ClampedFloatParameter contribution = new ClampedFloatParameter(0f, 0f, 1f);

        /// <inheritdoc/>
        public bool IsActive() => contribution.value > 0f && ValidateLUT();

        /// <inheritdoc/>
        [Obsolete("Unused #from(2023.1)", false)]
        public bool IsTileCompatible() => true;

        /// <summary>
        /// Validates the lookup texture assigned to the volume component.
        /// </summary>
        /// <returns>True if the texture is valid, false otherwise.</returns>
        public bool ValidateLUT()
        {
            return texture.value is Texture3D t && t.width == t.height && t.width == t.depth && t.width > 1;
        }
    }
}
