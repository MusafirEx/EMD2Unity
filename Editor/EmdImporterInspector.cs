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
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("useEmatTextureReferences"), new GUIContent("Read Companion EMAT Texture Names"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("linkNormalMaps"), new GUIContent("Link Normal Textures (Opt-in)"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("linkSpecularMaps"), new GUIContent("Link Specular Textures (Opt-in)"));
                    EditorGUILayout.HelpBox("Unity DDSImporter has no Texture Type setting. The button converts nearby *_NO.dds to new *_NO_EMD2Unity.png normal maps (original DDS files remain unchanged), configures normal-map import settings and enables linking. Existing generated PNG files are preserved. Specular linking remains experimental.", MessageType.Info);
                    if (GUILayout.Button("Prepare Nearby Normal Maps (DDS to PNG)"))
                    {
                        var selectedImporter = target as EmdImporter;
                        if (selectedImporter != null)
                        {
                            int found, generated, failed;
                            int updated = EmdImporter.PrepareNearbyNormalTextures(
                                selectedImporter.assetPath, out found, out generated, out failed);
                            if (found == 0)
                            {
                                EditorUtility.DisplayDialog("EMD2Unity Normal Maps",
                                    "No *_NO textures with supported formats were found next to this model (including its textures/ folder).",
                                    "OK");
                            }
                            else
                            {
                                serializedObject.FindProperty("linkNormalMaps").boolValue = true;
                                serializedObject.ApplyModifiedProperties();
                                Debug.Log("EMD2Unity: found " + found + " *_NO sources, generated " +
                                    generated + " DDS-to-PNG derivatives, configured " + updated +
                                    " Normal-map importers, failed " + failed + " for " +
                                    selectedImporter.assetPath + ". Reimporting EMD...");
                                selectedImporter.SaveAndReimport();
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
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
