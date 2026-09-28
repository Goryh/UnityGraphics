using System;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace UnityEditor.Rendering.Universal
{
    /// <summary>
    /// Scene view toolbar exposing a subset of the Rendering Debugger view modes as one-click toggles.
    /// The view modes are mutually exclusive; the Gray Albedo keyword toggle is independent of them.
    /// </summary>
    [Overlay(typeof(SceneView), k_Id, "URP Draw Modes", defaultDisplay = true, defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top)]
    class UniversalDrawModesOverlay : ToolbarOverlay
    {
        const string k_Id = "URP/Draw Modes";

        UniversalDrawModesOverlay() : base(
            WireframeGroup.id,
            DebugViewGroup.id,
            NormalViewToggle.id)
        { }
    }

    static class UniversalDrawModes
    {
        public enum Mode
        {
            Normal,
            Wireframe,
            SolidWireframe,
            ShadedWireframe,
            Albedo,
            Emission,
            LightingComplexity,
            VertexColor,
            BakedLightmap,
            Shadowmask,
			GrayAlbedo
        }

        public static event Action changed;

        static GlobalKeyword s_GrayAlbedoKeyword = GlobalKeyword.Create("GRAY_ALBEDO");

        static UniversalRenderPipelineDebugDisplaySettings settings => UniversalRenderPipelineDebugDisplaySettings.Instance;

        public static bool grayAlbedo
        {
            get => Shader.IsKeywordEnabled(s_GrayAlbedoKeyword);
            set
            {
                if (value)
                    Shader.EnableKeyword(s_GrayAlbedoKeyword);
                else
                    Shader.DisableKeyword(s_GrayAlbedoKeyword);
                NotifyChanged();
            }
        }

        static DrawCameraMode GetDrawMode(SceneView sceneView) => sceneView != null ? sceneView.cameraMode.drawMode : DrawCameraMode.Textured;

        public static bool IsActive(Mode mode, SceneView sceneView)
        {
            var rendering = settings.renderingSettings;
            var material = settings.materialSettings;

            switch (mode)
            {
                case Mode.Wireframe: return rendering.wireframeMode == DebugWireframeMode.Wireframe;
                case Mode.SolidWireframe: return rendering.wireframeMode == DebugWireframeMode.SolidWireframe;
                case Mode.ShadedWireframe: return rendering.wireframeMode == DebugWireframeMode.ShadedWireframe;
                case Mode.Albedo: return material.materialDebugMode == DebugMaterialMode.Albedo;
                case Mode.Emission: return material.materialDebugMode == DebugMaterialMode.Emission;
                case Mode.LightingComplexity: return material.materialDebugMode == DebugMaterialMode.LightingComplexity;
                case Mode.VertexColor: return material.vertexAttributeDebugMode == DebugVertexAttributeMode.Color;
                case Mode.BakedLightmap: return GetDrawMode(sceneView) == DrawCameraMode.BakedLightmap;
                case Mode.Shadowmask: return GetDrawMode(sceneView) == DrawCameraMode.ShadowMasks;
				case Mode.GrayAlbedo: return UniversalDrawModes.grayAlbedo;
                case Mode.Normal:
                    return rendering.wireframeMode == DebugWireframeMode.None
                        && material.materialDebugMode == DebugMaterialMode.None
                        && material.vertexAttributeDebugMode == DebugVertexAttributeMode.None
                        && GetDrawMode(sceneView) == DrawCameraMode.Textured
                        && !UniversalDrawModes.grayAlbedo;
                default: return false;
            }
        }

        public static void Activate(Mode mode, SceneView sceneView)
        {
            var rendering = settings.renderingSettings;
            var material = settings.materialSettings;

            rendering.wireframeMode = DebugWireframeMode.None;
            material.materialDebugMode = DebugMaterialMode.None;
            material.vertexAttributeDebugMode = DebugVertexAttributeMode.None;
            UniversalDrawModes.grayAlbedo = false;

            var drawMode = DrawCameraMode.Textured;
            switch (mode)
            {
                case Mode.Wireframe: rendering.wireframeMode = DebugWireframeMode.Wireframe; break;
                case Mode.SolidWireframe: rendering.wireframeMode = DebugWireframeMode.SolidWireframe; break;
                case Mode.ShadedWireframe: rendering.wireframeMode = DebugWireframeMode.ShadedWireframe; break;
                case Mode.Albedo: material.materialDebugMode = DebugMaterialMode.Albedo; break;
                case Mode.Emission: material.materialDebugMode = DebugMaterialMode.Emission; break;
                case Mode.LightingComplexity: material.materialDebugMode = DebugMaterialMode.LightingComplexity; break;
                case Mode.VertexColor: material.vertexAttributeDebugMode = DebugVertexAttributeMode.Color; break;
                case Mode.BakedLightmap: drawMode = DrawCameraMode.BakedLightmap; break;
                case Mode.Shadowmask: drawMode = DrawCameraMode.ShadowMasks; break;
                case Mode.GrayAlbedo: UniversalDrawModes.grayAlbedo = true; break;
            }

            if (sceneView != null && sceneView.cameraMode.drawMode != drawMode)
                sceneView.cameraMode = SceneView.GetBuiltinCameraMode(drawMode);

            NotifyChanged();
        }

        static void NotifyChanged()
        {
            changed?.Invoke();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // Built-in icons live in several folders of the editor resources bundle; FindTexture only resolves some
        // of them. Component icons ("Camera Icon", "Light Icon", ...) are processed .asset files, others are .png.
        static readonly string[] k_IconFolders =
        {
            "Icons/Processed/UnityEngine/",
            "Icons/Processed/UnityEditor/",
            "Icons/Processed/",
            "Icons/Toolbars/",
            "Icons/",
        };

        static readonly string[] k_IconExtensions = { ".asset", ".png" };

        public static Texture2D LoadIcon(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            if (EditorGUIUtility.isProSkin)
            {
                var dark = LoadIconFromFolders("d_" + name);
                if (dark != null)
                    return dark;
            }

            return LoadIconFromFolders(name) ?? EditorGUIUtility.FindTexture(name);
        }

        static Texture2D LoadIconFromFolders(string fileName)
        {
            foreach (var folder in k_IconFolders)
            {
                foreach (var extension in k_IconExtensions)
                {
                    if (EditorGUIUtility.Load(folder + fileName + extension) is Texture2D icon)
                        return icon;
                }
            }
            return null;
        }
    }

    abstract class UniversalDrawModeToggleBase : EditorToolbarToggle, IAccessContainerWindow
    {
        const long k_PollIntervalMs = 500;

        public EditorWindow containerWindow { get; set; }

        protected SceneView sceneView => containerWindow as SceneView ?? SceneView.lastActiveSceneView;

        IVisualElementScheduledItem m_Poll;

        protected UniversalDrawModeToggleBase(string label, string tooltipText, string iconName)
        {
            var iconTexture = UniversalDrawModes.LoadIcon(iconName);
            if (iconTexture != null)
                icon = iconTexture;
            else
            {
                text = label;
                Debug.LogWarning($"URP Draw Modes: built-in icon '{iconName}' not found, using text label.");
            }
            tooltip = tooltipText;

            this.RegisterValueChangedCallback(evt => OnToggled(evt.newValue));

            // Poll as well, so changes made from the Rendering Debugger window are reflected here.
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                UniversalDrawModes.changed += Refresh;
                m_Poll ??= schedule.Execute(Refresh).Every(k_PollIntervalMs);
                m_Poll.Resume();
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                UniversalDrawModes.changed -= Refresh;
                m_Poll?.Pause();
            });
        }

        protected abstract bool IsActive();
        protected abstract void OnToggled(bool on);

        void Refresh() => SetValueWithoutNotify(IsActive());
    }

    abstract class UniversalDrawModeToggle : UniversalDrawModeToggleBase
    {
        readonly UniversalDrawModes.Mode m_Mode;

        protected UniversalDrawModeToggle(UniversalDrawModes.Mode mode, string label, string tooltipText, string iconNames)
            : base(label, tooltipText, iconNames)
        {
            m_Mode = mode;
        }

        protected override bool IsActive() => UniversalDrawModes.IsActive(m_Mode, sceneView);

        // Turning an active mode off falls back to the normal view.
        protected override void OnToggled(bool on) => UniversalDrawModes.Activate(on ? m_Mode : UniversalDrawModes.Mode.Normal, sceneView);
    }

    /// <summary>
    /// Lays out its toggles as a joined button strip and forwards the owning window to them.
    /// </summary>
    abstract class UniversalDrawModeGroup : VisualElement, IAccessContainerWindow
    {
        EditorWindow m_ContainerWindow;

        public EditorWindow containerWindow
        {
            get => m_ContainerWindow;
            set
            {
                m_ContainerWindow = value;
                foreach (var child in Children())
                {
                    if (child is IAccessContainerWindow access)
                        access.containerWindow = value;
                }
            }
        }

        protected UniversalDrawModeGroup(params VisualElement[] toggles)
        {
            foreach (var toggle in toggles)
                Add(toggle);
            EditorToolbarUtility.SetupChildrenAsButtonStrip(this);
        }
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class WireframeGroup : UniversalDrawModeGroup
    {
        public const string id = "URP/DrawModes/WireframeGroup";
        public WireframeGroup() : base(
            new WireframeToggle(),
            new SolidWireframeToggle(),
            new ShadedWireframeToggle())
        { }
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class DebugViewGroup : UniversalDrawModeGroup
    {
        public const string id = "URP/DrawModes/DebugViewGroup";
        public DebugViewGroup() : base(
            new GrayAlbedoToggle(),
            new AlbedoToggle(),
            new EmissionToggle(),
            new VertexColorToggle(),
            new BakedLightmapToggle(),
            new ShadowmaskToggle(),
            new LightingComplexityToggle())
        { }
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class NormalViewToggle : UniversalDrawModeToggle
    {
        public const string id = "URP/DrawModes/Normal";
        public NormalViewToggle() : base(UniversalDrawModes.Mode.Normal, "Normal", "Normal view: disable all debug draw modes.", "Shaded@2x") { }
    }

    class WireframeToggle : UniversalDrawModeToggle
    {
        public WireframeToggle() : base(UniversalDrawModes.Mode.Wireframe, "Wire", "Wireframe (Rendering Debugger > Rendering > Additional Wireframe Modes).", "Wireframe@2x") { }
    }

    class SolidWireframeToggle : UniversalDrawModeToggle
    {
        public SolidWireframeToggle() : base(UniversalDrawModes.Mode.SolidWireframe, "Solid Wire", "Solid Wireframe (Rendering Debugger > Rendering > Additional Wireframe Modes).", "PreMatSphere@2x") { }
    }

    class ShadedWireframeToggle : UniversalDrawModeToggle
    {
        public ShadedWireframeToggle() : base(UniversalDrawModes.Mode.ShadedWireframe, "Shaded Wire", "Shaded Wireframe (Rendering Debugger > Rendering > Additional Wireframe Modes).", "ShadedWireframe@2x") { }
    }

    class GrayAlbedoToggle : UniversalDrawModeToggle
    {
        public GrayAlbedoToggle() : base(UniversalDrawModes.Mode.GrayAlbedo, "Gray", "Toggle the global shader keyword GRAY_ALBEDO.", "LightingPreviewMode@2x") { }
    }

    class AlbedoToggle : UniversalDrawModeToggle
    {
        public AlbedoToggle() : base(UniversalDrawModes.Mode.Albedo, "Albedo", "Albedo (Rendering Debugger > Material > Material Override).", "UnlitMode@2x") { }
    }

    class EmissionToggle : UniversalDrawModeToggle
    {
        public EmissionToggle() : base(UniversalDrawModes.Mode.Emission, "Emission", "Emission (Rendering Debugger > Material > Material Override).", "AISparkle@2x") { }
    }

    class VertexColorToggle : UniversalDrawModeToggle
    {
        public VertexColorToggle() : base(UniversalDrawModes.Mode.VertexColor, "Vtx Color", "Vertex Color (Rendering Debugger > Material > Vertex Attribute).", "BuildSettings.Stadia@2x") { }
    }

    class BakedLightmapToggle : UniversalDrawModeToggle
    {
        public BakedLightmapToggle() : base(UniversalDrawModes.Mode.BakedLightmap, "Lightmap", "Baked Lightmap (Scene view Debug Draw Mode).", "Lighting@2x") { }
    }

    class ShadowmaskToggle : UniversalDrawModeToggle
    {
        public ShadowmaskToggle() : base(UniversalDrawModes.Mode.Shadowmask, "Shadowmask", "Shadowmask (Scene view Debug Draw Mode).", "Profiler.Rendering@2x") { }
    }

    class LightingComplexityToggle : UniversalDrawModeToggle
    {
        public LightingComplexityToggle() : base(UniversalDrawModes.Mode.LightingComplexity, "Light Cplx", "Lighting Complexity (Rendering Debugger > Material > Material Override).", "Collab@2x") { }
    }
}
