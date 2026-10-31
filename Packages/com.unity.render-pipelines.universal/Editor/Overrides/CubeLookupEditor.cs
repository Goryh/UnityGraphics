using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(CubeLookup))]
    sealed class CubeLookupEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Texture;
        SerializedDataParameter m_Contribution;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<CubeLookup>(serializedObject);

            m_Texture = Unpack(o.Find(x => x.texture));
            m_Contribution = Unpack(o.Find(x => x.contribution));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Texture, EditorGUIUtility.TrTextContent("Lookup Texture", "A 3D texture imported from a .cube file."));

            var lut = m_Texture.value.objectReferenceValue;
            if (lut != null && !((CubeLookup)target).ValidateLUT())
                EditorGUILayout.HelpBox("Invalid lookup texture. It must be a cubic 3D texture imported from a .cube file.", MessageType.Warning);
            else if (lut != null && !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(lut)) is CubeLutImporter))
                EditorGUILayout.HelpBox("The lookup texture was not imported from a .cube file. It must be laid out in the internal LUT space: LogC coordinates, LogC encoded colors.", MessageType.Info);

            PropertyField(m_Contribution, EditorGUIUtility.TrTextContent("Contribution"));

            var asset = UniversalRenderPipeline.asset;
            if (asset != null)
            {
                if (asset.supportsHDR && asset.colorGradingMode == ColorGradingMode.HighDynamicRange)
                    EditorGUILayout.HelpBox("This Lookup Table (LUT) is baked into the internal HDR color grading LUT, before the YRGB curves and tonemapping.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("Cube Lookup is only supported with the High Dynamic Range (HDR) Grading Mode. Enable HDR and set the Grading Mode in the current Universal Render Pipeline Asset to High Dynamic Range.", MessageType.Warning);
            }
        }
    }
}
