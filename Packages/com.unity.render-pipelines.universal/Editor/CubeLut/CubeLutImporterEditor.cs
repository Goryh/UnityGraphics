using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(CubeLutImporter))]
    sealed class CubeLutImporterEditor : ScriptedImporterEditor
    {
        SerializedProperty m_InputEncoding;
        SerializedProperty m_InputGamut;
        SerializedProperty m_OutputEncoding;
        SerializedProperty m_OutputGamut;
        SerializedProperty m_Size;
        SerializedProperty m_Extrapolation;
        SerializedProperty m_PolynomialOrder;
        SerializedProperty m_KeyPointCount;
        SerializedProperty m_FitRangeStops;
        SerializedProperty m_TonemapKnee;
        SerializedProperty m_HighlightRebuildStart;
        SerializedProperty m_ToneCurveStrength;
        SerializedProperty m_LuminanceStrength;

        public override void OnEnable()
        {
            base.OnEnable();

            m_InputEncoding = serializedObject.FindProperty(nameof(CubeLutImporter.inputEncoding));
            m_InputGamut = serializedObject.FindProperty(nameof(CubeLutImporter.inputGamut));
            m_OutputEncoding = serializedObject.FindProperty(nameof(CubeLutImporter.outputEncoding));
            m_OutputGamut = serializedObject.FindProperty(nameof(CubeLutImporter.outputGamut));
            m_Size = serializedObject.FindProperty(nameof(CubeLutImporter.size));
            m_Extrapolation = serializedObject.FindProperty(nameof(CubeLutImporter.extrapolation));
            m_PolynomialOrder = serializedObject.FindProperty(nameof(CubeLutImporter.polynomialOrder));
            m_KeyPointCount = serializedObject.FindProperty(nameof(CubeLutImporter.keyPointCount));
            m_FitRangeStops = serializedObject.FindProperty(nameof(CubeLutImporter.fitRangeStops));
            m_TonemapKnee = serializedObject.FindProperty(nameof(CubeLutImporter.tonemapKnee));
            m_HighlightRebuildStart = serializedObject.FindProperty(nameof(CubeLutImporter.highlightRebuildStart));
            m_ToneCurveStrength = serializedObject.FindProperty(nameof(CubeLutImporter.toneCurveStrength));
            m_LuminanceStrength = serializedObject.FindProperty(nameof(CubeLutImporter.luminanceStrength));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_InputEncoding, EditorGUIUtility.TrTextContent("Encoding"));
            EditorGUILayout.PropertyField(m_InputGamut, EditorGUIUtility.TrTextContent("Gamut"));
            GamutHint(m_InputEncoding, m_InputGamut);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_OutputEncoding, EditorGUIUtility.TrTextContent("Encoding"));
            EditorGUILayout.PropertyField(m_OutputGamut, EditorGUIUtility.TrTextContent("Gamut"));
            GamutHint(m_OutputEncoding, m_OutputGamut);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baking", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Size);
            EditorGUILayout.PropertyField(m_Extrapolation);

            using (new EditorGUI.IndentLevelScope())
            {
                bool mixed = m_Extrapolation.hasMultipleDifferentValues;
                bool inverseTonemap = m_Extrapolation.intValue == (int)CubeLutExtrapolation.InverseTonemap;

                if (mixed || !inverseTonemap)
                {
                    EditorGUILayout.PropertyField(m_PolynomialOrder);
                    EditorGUILayout.PropertyField(m_KeyPointCount);
                    EditorGUILayout.PropertyField(m_FitRangeStops);
                }

                if (mixed || inverseTonemap)
                {
                    EditorGUILayout.PropertyField(m_TonemapKnee, EditorGUIUtility.TrTextContent("Knee", m_TonemapKnee.tooltip));
                    EditorGUILayout.PropertyField(m_HighlightRebuildStart, EditorGUIUtility.TrTextContent("Highlight Rebuild", m_HighlightRebuildStart.tooltip));
                }
            }

            EditorGUILayout.PropertyField(m_ToneCurveStrength, EditorGUIUtility.TrTextContent("Tone Curve", m_ToneCurveStrength.tooltip));
            EditorGUILayout.PropertyField(m_LuminanceStrength, EditorGUIUtility.TrTextContent("Luminance", m_LuminanceStrength.tooltip));

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }

        // Camera log curves come with the wide gamut of their camera. A different gamut is legitimate (some LUTs
        // are authored for LogC3 in Rec709 primaries) but most often a mistake, so suggest the usual pairing.
        static void GamutHint(SerializedProperty encodingProperty, SerializedProperty gamutProperty)
        {
            if (encodingProperty.hasMultipleDifferentValues || gamutProperty.hasMultipleDifferentValues)
                return;

            var encoding = (CubeLutEncoding)encodingProperty.intValue;
            var gamut = (CubeLutGamut)gamutProperty.intValue;

            if (!TryGetUsualGamut(encoding, out var usual, out var alternative) || gamut == usual || gamut == alternative)
                return;

            string message = alternative == usual
                ? $"{encoding} is usually paired with the {usual} gamut."
                : $"{encoding} is usually paired with the {usual} or {alternative} gamut.";

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
                if (GUILayout.Button(EditorGUIUtility.TrTextContent($"Use {usual}"), GUILayout.ExpandHeight(true), GUILayout.Width(150)))
                    gamutProperty.intValue = (int)usual;
            }
        }

        static bool TryGetUsualGamut(CubeLutEncoding encoding, out CubeLutGamut usual, out CubeLutGamut alternative)
        {
            switch (encoding)
            {
                case CubeLutEncoding.ArriLogC3: usual = alternative = CubeLutGamut.ArriWideGamut3; return true;
                case CubeLutEncoding.ACEScct: usual = alternative = CubeLutGamut.ACES_AP1; return true;
                case CubeLutEncoding.SLog3: usual = CubeLutGamut.SGamut3Cine; alternative = CubeLutGamut.SGamut3; return true;
                case CubeLutEncoding.DaVinciIntermediate: usual = alternative = CubeLutGamut.DaVinciWideGamut; return true;
                case CubeLutEncoding.VLog: usual = alternative = CubeLutGamut.VGamut; return true;
                // Display encodings (sRGB, gamma 2.4) are used with several display gamuts, and Unity LogC is
                // URP's internal Rec709 space: no single usual pairing to suggest.
                default: usual = alternative = default; return false;
            }
        }
    }
}
