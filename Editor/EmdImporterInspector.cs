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
            // Stage EMDs can import as an empty root without any renderable
            // polygon groups. Surface this clearly rather than presenting a
            // misleading apparently successful model import.
            var selectedEmd = target as EmdImporter;
            if (selectedEmd != null && !string.IsNullOrEmpty(selectedEmd.assetPath))
            {
                GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(selectedEmd.assetPath);
                if (imported != null)
                {
                    bool hasGeometry = false;
                    foreach (MeshFilter filter in imported.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0)
                        {
                            hasGeometry = true;
                            break;
                        }
                    }
                    if (!hasGeometry)
                    {
                        foreach (SkinnedMeshRenderer render in imported.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            if (render.sharedMesh != null && render.sharedMesh.vertexCount > 0)
                            {
                                hasGeometry = true;
                                break;
                            }
                        }
                    }
                    if (!hasGeometry)
                        EditorGUILayout.HelpBox(
                            "This EMD imported without renderable meshes (empty GameObject). " +
                            "See Console for polygon/vertex counts. If this is an environment AREA model, " +
                            "it may be a reference/empty file or a format variant; an imported root alone " +
                            "does not mean its geometry was decoded.", MessageType.Warning);
                }
            }
            switch (tab)
            {
                case 0:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("modelScale"), new GUIContent("Scale"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("swapYAndZ"), new GUIContent("Convert PS3 Z-Up to Unity Y-Up"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("reverseWinding"), new GUIContent("Reverse Triangle Winding"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("useVertexColors"), new GUIContent("Preserve Vertex Colors"));
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("recoverSupportedPolygons"),
                        new GUIContent("Recover Supported Meshes (skip unsupported polygons)"));
                    EditorGUILayout.HelpBox(
                        "Optional stage-EMD recovery: keeps valid polygon records if others have unsupported vertex/index layouts. Skipped polygons remain missing and detailed reasons are written to Console. Off by default to preserve strict import behavior. Click Apply to reimport.",
                        MessageType.Info);
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
                    EditorGUILayout.HelpBox("EMAT references _CO, _NO and _SP maps. Diffuse prefers EMD. To use _NO normals, click the button below: it configures nearby *_NO textures as Unity Normal maps and enables normal linking. Specular matching remains experimental.", MessageType.Info);
                    if (GUILayout.Button("Prepare Nearby _NO Textures as Normal Maps"))
                    {
                        var selectedImporter = target as EmdImporter;
                        if (selectedImporter != null)
                        {
                            int found;
                            int updated = EmdImporter.PrepareNearbyNormalTextures(selectedImporter.assetPath, out found);
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
                                Debug.Log("EMD2Unity: prepared " + updated + " normal textures; " +
                                    found + " matching _NO files found for " + selectedImporter.assetPath +
                                    ". Normal linking enabled. Reimporting EMD...");
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
