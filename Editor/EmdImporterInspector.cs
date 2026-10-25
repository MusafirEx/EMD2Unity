#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    // Keeps the familiar Model / Rig / Materials / Animation layout.
    [CustomEditor(typeof(EmdImporter))]
    public sealed class EmdImporterInspector : ScriptedImporterEditor
    {
        private int tab;
        private static readonly string[] Tabs = { "Model", "Rig", "Materials", "Animation" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            tab = GUILayout.Toolbar(tab, Tabs);
            EditorGUILayout.Space(7f);
            switch (tab)
            {
                case 0:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("modelScale"), new GUIContent("Scale"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("swapYAndZ"), new GUIContent("Convert PS3 Z-Up to Unity Y-Up"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("reverseWinding"), new GUIContent("Reverse Triangle Winding"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("useVertexColors"), new GUIContent("Preserve Vertex Colors"));
                    EditorGUILayout.HelpBox("Normals are recalculated. If the model appears inside-out, toggle Reverse Triangle Winding.", MessageType.Info);
                    break;
                case 1:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("importSkeleton"), new GUIContent("Import Skeleton & Skin Weights"));
                    EditorGUILayout.HelpBox("Skeleton and four-influence skinning are experimental until validated against a real Battride War Genesis EMD file. Generic/Humanoid avatar mapping is not automatic.", MessageType.Info);
                    break;
                case 2:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("findNearbyTextures"), new GUIContent("Find DDS/PNG Textures Nearby"));
                    EditorGUILayout.HelpBox("Place extracted textures in a 'textures' folder next to the EMD or in the same directory. The importer uses the first referenced submaterial texture.", MessageType.Info);
                    break;
                default:
                    EditorGUILayout.HelpBox("Animation clips are not embedded/imported by this EMD parser. Separate animation format reverse-engineering is required.", MessageType.Warning);
                    break;
            }
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
#endif
