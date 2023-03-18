using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;


namespace UnityEditor.Rendering.HighDefinition
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(WaterRendering))]
    class WaterRenderingEditor : VolumeComponentEditor
    {
        // General
        SerializedDataParameter m_Enable;

        // LOD
        SerializedDataParameter m_TriangleSize;

        // Lighting
        SerializedDataParameter m_AmbientProbeDimmer;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<WaterRendering>(serializedObject);
            // General
            m_Enable = Unpack(o.Find(x => x.enable));

            // LOD
            m_TriangleSize = Unpack(o.Find(x => x.triangleSize));

            // Lighting
            m_AmbientProbeDimmer = Unpack(o.Find(x => x.ambientProbeDimmer));
        }

        public override void OnInspectorGUI()
        {
            HDRenderPipelineAsset currentAsset = HDRenderPipeline.currentAsset;
            if (!currentAsset?.currentPlatformRenderPipelineSettings.supportWater ?? false)
            {
                EditorGUILayout.Space();
                HDEditorUtils.QualitySettingsHelpBox("The current HDRP Asset does not support Water Surfaces.", MessageType.Error,
                    HDRenderPipelineUI.ExpandableGroup.Rendering,
                    HDRenderPipelineUI.ExpandableRendering.Water, "m_RenderPipelineSettings.supportWater");
                return;
            }

            EditorGUILayout.LabelField("General", EditorStyles.miniLabel);
            PropertyField(m_Enable, EditorGUIUtility.TrTextContent("State"));

            EditorGUILayout.LabelField("Level of Detail", EditorStyles.miniLabel);
            PropertyField(m_TriangleSize);

            EditorGUILayout.LabelField("Lighting", EditorStyles.miniLabel);
            PropertyField(m_AmbientProbeDimmer);
        }
    }
}
