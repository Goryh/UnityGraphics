using System;
using System.Diagnostics;
using UnityEngine;

namespace UnityEngine.Rendering.HighDefinition
{
    [Serializable]
    public sealed class CapsuleShadowResolutionParameter : VolumeParameter<CapsuleShadowResolution>
    {
        public CapsuleShadowResolutionParameter(CapsuleShadowResolution value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenuForRenderPipeline("Shadowing/Capsule Shadows", typeof(HDRenderPipeline))]
    public class CapsuleShadowsVolumeComponent : VolumeComponent
    {
        /// <summary>
        /// Choose what resolution to use when rendering capsules shadows after the depth pre-pass.
        /// </summary>
        public CapsuleShadowResolutionParameter resolution = new CapsuleShadowResolutionParameter(CapsuleShadowResolution.Half);

        /// <summary>
        /// When enabled, capsules cast shadows for supported lights.
        /// </summary>
        public BoolParameter enableDirectShadows = new BoolParameter(true);

        /// <summary>
        /// Whether to fade out self-shadowing artifacts from capsules.
        /// </summary>
        public BoolParameter fadeSelfShadow = new BoolParameter(true);

        /// <summary>
        /// Whether to use an improved occlusion term that is more accurate for longer capsules.
        /// </summary>
        public BoolParameter fullCapsuleOcclusion = new BoolParameter(true);

        /// <summary>
        /// When enabled, capsules produce indirect shadows or ambient occlusion.
        /// </summary>
        public BoolParameter enableIndirectShadows = new BoolParameter(false);

        /// <summary>
        /// The minimium amount of visibility that must remain after indirect shadows.
        /// </summary>
        public FloatParameter indirectMinVisibility = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);

        /// <summary>
        /// The range of indirect shadows from capsules, in multiples of the capsule radius.
        /// </summary>
        public FloatParameter indirectRangeFactor = new MinFloatParameter(4.0f, 0.0f);

        /// <summary>
        /// Whether to use an improved ambient occlusion term that is more accurate for longer capsules.
        /// </summary>
        public BoolParameter fullCapsuleAmbientOcclusion = new BoolParameter(true);

        CapsuleShadowsVolumeComponent()
        {
            displayName = "Capsule Shadows";
        }
    }
}
