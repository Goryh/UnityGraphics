namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Persistent, per camera storage for the internal color grading LUT.
    ///
    /// The LUT baked by <c>ColorGradingLutPass</c> only depends on the color grading volume components,
    /// on the LUT size/format and on the HDR output settings. None of those change on their own, so the
    /// LUT is rendered on demand instead of every frame:
    /// * cameras updating their volume stack from script (<see cref="VolumeFrameworkUpdateMode.ViaScripting"/>)
    ///   invalidate the cache from <see cref="CameraExtensions.UpdateVolumeStack(Camera, UniversalAdditionalCameraData)"/>,
    /// * cameras updating their volumes every frame invalidate it every frame, which keeps the original behaviour.
    ///
    /// The cache is owned by <see cref="UniversalAdditionalCameraData"/> so it follows the camera lifetime.
    /// </summary>
    internal class ColorGradingLutCache
    {
        const string k_LutName = "_InternalGradingLut";

        RTHandle m_Lut;
        Hash128 m_DescKey;
        // State the LUT depends on but that is not described by the texture descriptor (HDR output).
        Hash128 m_StateKey;
        bool m_Dirty = true;

        /// <summary>
        /// The persistent LUT texture. Only valid after <see cref="UpdateTexture"/> has been called.
        /// </summary>
        public RTHandle lut => m_Lut;

        /// <summary>
        /// Invalidates the cached LUT so that it gets rendered again on the next frame this camera renders.
        /// </summary>
        public void SetDirty()
        {
            m_Dirty = true;
        }

        /// <summary>
        /// Reports whether the cached LUT content is stale, without touching the persistent texture.
        /// Used by Compatibility Mode, where the LUT texture is owned by the renderer instead.
        /// </summary>
        /// <param name="stateKey">A hash of the non-volume state the LUT depends on.</param>
        /// <returns>True when the LUT has to be rendered this frame.</returns>
        public bool CheckDirty(in Hash128 stateKey)
        {
            if (m_StateKey != stateKey)
            {
                m_StateKey = stateKey;
                m_Dirty = true;
            }

            return m_Dirty;
        }

        /// <summary>
        /// Allocates or reallocates the persistent LUT texture and reports whether its content is stale.
        /// </summary>
        /// <param name="descriptor">The descriptor the LUT texture must match.</param>
        /// <param name="filterMode">The filter mode the LUT texture must use.</param>
        /// <param name="stateKey">A hash of the non-volume state the LUT depends on. Must include the descriptor.</param>
        /// <returns>True when the LUT has to be rendered this frame.</returns>
        public bool UpdateTexture(in RenderTextureDescriptor descriptor, FilterMode filterMode, in Hash128 stateKey)
        {
            var desc = descriptor;
            var descKey = Hash128.Compute(ref desc);

            if (m_Lut == null || m_DescKey != descKey)
            {
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_Lut, desc, filterMode, TextureWrapMode.Clamp, anisoLevel: 0, name: k_LutName);
                m_DescKey = descKey;
                m_Dirty = true;
            }

            CheckDirty(stateKey);

            // The render texture content is lost when the graphics device recreates it (device reset, resolution
            // change on some platforms...). Rebuild rather than sample an uninitialized LUT.
            if (!m_Dirty && (m_Lut.rt == null || !m_Lut.rt.IsCreated()))
                m_Dirty = true;

            return m_Dirty;
        }

        /// <summary>
        /// Marks the cached LUT as up to date. Called once the render pass rendering it has been recorded.
        /// </summary>
        public void ClearDirty()
        {
            m_Dirty = false;
        }

        /// <summary>
        /// Releases the persistent LUT texture.
        /// </summary>
        public void Dispose()
        {
            m_Lut?.Release();
            m_Lut = null;
            m_DescKey = default;
            m_StateKey = default;
            m_Dirty = true;
        }
    }
}
