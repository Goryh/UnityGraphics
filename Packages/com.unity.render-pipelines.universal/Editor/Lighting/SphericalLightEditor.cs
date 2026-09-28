using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(SphericalLight))]
    [CanEditMultipleObjects]
    class SphericalLightEditor : Editor
    {
        static class Styles
        {
            public static readonly GUIContent Color = EditorGUIUtility.TrTextContent("Color", "The color of the light.");
            public static readonly GUIContent Intensity = EditorGUIUtility.TrTextContent("Intensity", "The intensity of the light.");
            public static readonly GUIContent Range = EditorGUIUtility.TrTextContent("Range", "External radius at which the light's contribution fades to zero. Scaled by the transform's global X scale.");
            public static readonly GUIContent AreaRadius = EditorGUIUtility.TrTextContent("Area Radius", "Internal radius of the light's emitting sphere. Cannot exceed the range. Scaled by the transform's global X scale.");
            public static readonly GUIContent ExclusionMask = EditorGUIUtility.TrTextContent("Exclusion Mask", "Bit mask passed to shaders, read with GetAdditionalLightExclusionMask().");
        }

        SerializedProperty m_Color;
        SerializedProperty m_Intensity;
        SerializedProperty m_Range;
        SerializedProperty m_AreaRadius;
        SerializedProperty m_ExclusionMask;

        void OnEnable()
        {
            m_Color = serializedObject.FindProperty("m_Color");
            m_Intensity = serializedObject.FindProperty("m_Intensity");
            m_Range = serializedObject.FindProperty("m_Range");
            m_AreaRadius = serializedObject.FindProperty("m_AreaRadius");
            m_ExclusionMask = serializedObject.FindProperty("m_ExclusionMask");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_Color, Styles.Color);
            EditorGUILayout.PropertyField(m_Intensity, Styles.Intensity);
            EditorGUILayout.PropertyField(m_Range, Styles.Range);
            EditorGUILayout.PropertyField(m_AreaRadius, Styles.AreaRadius);
            EditorGUILayout.PropertyField(m_ExclusionMask, Styles.ExclusionMask);

            // Applying calls SphericalLight.OnValidate(), which clamps the area radius and updates the light.
            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI()
        {
            var light = (SphericalLight)target;
            var position = light.transform.position;
            var color = light.color;

            // Handles show and edit the radii in world units, i.e. scaled by the transform's global x scale.
            float scale = light.radiusScale;
            if (scale <= 0.0f)
                return;

            EditorGUI.BeginChangeCheck();

            using (new Handles.DrawingScope(new Color(color.r, color.g, color.b, 0.75f)))
            {
                float worldRange = Handles.RadiusHandle(Quaternion.identity, position, light.worldRange);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(light, "Adjust Spherical Light Range");
                    light.range = worldRange / scale;
                    light.areaRadius = Mathf.Min(light.areaRadius, light.range);
                    light.UpdateLight();
                }
            }

            EditorGUI.BeginChangeCheck();

            using (new Handles.DrawingScope(Color.Lerp(color, Color.white, 0.5f)))
            {
                float worldAreaRadius = Handles.RadiusHandle(Quaternion.identity, position, light.worldAreaRadius);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(light, "Adjust Spherical Light Area Radius");
                    light.areaRadius = Mathf.Min(worldAreaRadius / scale, light.range);
                    light.UpdateLight();
                }
            }
        }

        [MenuItem("GameObject/Light/Spherical Light", priority = 10)]
        static void CreateSphericalLight(MenuCommand menuCommand)
        {
            var parent = menuCommand.context as GameObject;
            var gameObject = ObjectFactory.CreateGameObject("Spherical Light", typeof(SphericalLight));
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            if (parent == null && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.MoveToView(gameObject.transform);

            gameObject.GetComponent<SphericalLight>().UpdateLight();
            Selection.activeGameObject = gameObject;
        }
    }
}
