//-----------------------------------------------------------------------------
// Configuration
//-----------------------------------------------------------------------------
using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Project-wide shader configuration options.
    /// </summary>
    /// <remarks>This enum will generate the proper shader defines.</remarks>
    ///<seealso cref="ShaderConfig"/>
    [GenerateHLSL]
    public static class ShaderOptions
    {
        /// <summary>Max number of lights supported on mobile with OpenGL 3.0 and below.</summary>
        public const int k_MaxVisibleLightCountLowEndMobile = 16;

        /// <summary>Max number of lights supported on mobile, OpenGL, and WebGPU platforms.</summary>
        /// <remarks>255 is the Forward+ limit: tiles store light indices as (index + 1) in a byte.</remarks>
        public const int k_MaxVisibleLightCountMobile = 255;

        /// <summary>Max number of lights supported on desktop platforms.</summary>
        public const int k_MaxVisibleLightCountDesktop = 256;
    };
}
