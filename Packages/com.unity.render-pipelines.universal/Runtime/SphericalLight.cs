namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// A spherical area light rendered by Forward+ as an additional light.
    /// <see cref="range"/> and <see cref="areaRadius"/> are scaled by the x component of the transform's global scale.
    /// Changes to the light's properties or transform are not picked up automatically at runtime, call
    /// <see cref="UpdateLight"/> after moving or scaling the light or changing any of its properties.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Spherical Light")]
    public class SphericalLight : MonoBehaviour
    {
        [SerializeField, ColorUsage(false)] Color m_Color = Color.white;
        [SerializeField, Min(0.0f)] float m_Intensity = 1.0f;
        [SerializeField, Min(0.0f)] float m_Range = 10.0f;
        [SerializeField, Min(0.01f)] float m_AreaRadius = 0.01f;
        [SerializeField] ushort m_ExclusionMask = 0;

        // Index into SphericalLightRegistry, -1 when the light is not registered.
        [System.NonSerialized] internal int registryIndex = -1;

        /// <summary>
        /// The color of the light.
        /// </summary>
        public Color color
        {
            get => m_Color;
            set => m_Color = value;
        }

        /// <summary>
        /// The intensity of the light.
        /// </summary>
        public float intensity
        {
            get => m_Intensity;
            set => m_Intensity = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// The external radius of the light, at which its contribution fades to zero, before scaling.
        /// </summary>
        public float range
        {
            get => m_Range;
            set => m_Range = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// The internal radius of the light's emitting sphere, before scaling. It is clamped to <see cref="range"/> when the light is updated.
        /// </summary>
        public float areaRadius
        {
            get => m_AreaRadius;
            set => m_AreaRadius = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// 16 bit mask passed to shaders, read with GetAdditionalLightExclusionMask().
        /// </summary>
        public ushort exclusionMask
        {
            get => m_ExclusionMask;
            set => m_ExclusionMask = value;
        }

        /// <summary>
        /// The factor applied to <see cref="range"/> and <see cref="areaRadius"/>: the x component of the transform's global scale.
        /// </summary>
        public float radiusScale => Mathf.Abs(transform.lossyScale.x);

        /// <summary>
        /// <see cref="range"/> in world units.
        /// </summary>
        public float worldRange => m_Range * radiusScale;

        /// <summary>
        /// <see cref="areaRadius"/> in world units, clamped to <see cref="worldRange"/>.
        /// </summary>
        public float worldAreaRadius => Mathf.Min(m_AreaRadius, m_Range) * radiusScale;

        /// <summary>
        /// Sends the current transform and properties of the light to the renderer.
        /// Call it after moving or scaling the light or changing any of its properties.
        /// </summary>
        public void UpdateLight()
        {
            if (registryIndex >= 0)
                SphericalLightRegistry.Write(registryIndex, this);
        }

        void OnEnable()
        {
            SphericalLightRegistry.Register(this);
        }

        void OnDisable()
        {
            SphericalLightRegistry.Unregister(this);
        }

#if UNITY_EDITOR
        const string k_GizmoPath = "Packages/com.unity.render-pipelines.universal/Editor/2D/Resources/SceneViewIcons/PointLight.png";

        void OnValidate()
        {
            m_AreaRadius = Mathf.Min(m_AreaRadius, m_Range);
            UpdateLight();
        }

        // Keeps lights in sync while they are moved in the editor. At runtime, including play mode, UpdateLight()
        // has to be called explicitly.
        void Update()
        {
            if (!Application.isPlaying && transform.hasChanged)
            {
                transform.hasChanged = false;
                UpdateLight();
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.DrawIcon(transform.position, k_GizmoPath, true, m_Color);
        }
#endif
    }
}
