using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// A volume component that holds settings for the Post White Balance effect.
    /// </summary>
    /// <remarks>
    /// The same color temperature correction as <see cref="WhiteBalance"/>, applied at the end of the color grading
    /// stack instead of its start: after the YRGB curves, before tonemapping. Both are a von Kries adaptation in LMS
    /// space relative to D65, the white of the working space, so they chain without counting the D65 reference twice:
    /// a temperature of 0 leaves the colors unchanged.
    /// </remarks>
    [Serializable, VolumeComponentMenu("Post-processing/Post White Balance")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("Post-Processing-White-Balance")]
    public sealed class PostWhiteBalance : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// Controls the color temperature URP uses for white balancing at the end of the color grading stack.
        /// </summary>
        [Tooltip("Sets the white balance to a custom color temperature, applied after the color curves.")]
        public ClampedFloatParameter temperature = new ClampedFloatParameter(0f, -100, 100f);

        /// <inheritdoc/>
        public bool IsActive() => temperature.value != 0f;

        /// <inheritdoc/>
        [Obsolete("Unused #from(2023.1)", false)]
        public bool IsTileCompatible() => true;
    }
}
