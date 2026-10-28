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
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("vertexColorsRgbaLast"),
                        new GUIContent("Stage Vertex Colors Are RGBA (Experimental)"));
                    EditorGUILayout.HelpBox(
                        "Stage meshes like STG31/AREA02 have packed colors such as 4C 4C 4C FF. " +
                        "RGBA reads this as grey with opaque alpha (76,76,76,255), while " +
                        "legacy ARGB gives (76,76,255,76). Enable per EMD only after " +
                        "checking the read-only UV audit. This does not change UVs, " +
                        "textures, material bindings or original model files.",
                        MessageType.Info);
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("recoverSupportedPolygons"),
                        new GUIContent("Recover Supported Meshes (skip unsupported polygons)"));
                    EditorGUILayout.HelpBox(
                        "Optional stage-EMD recovery: keeps valid polygon records if others have unsupported vertex/index layouts. Skipped polygons remain missing and detailed reasons are written to Console. Off by default to preserve strict import behavior. Click Apply to reimport.",
                        MessageType.Info);
                    EditorGUILayout.HelpBox("Normals are recalculated. If the model appears inside-out, toggle Reverse Triangle Winding.", MessageType.Info);
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("alignUvBlocksTo16"),
                        new GUIContent("Align UV Blocks to 16 Bytes (Experimental)"));
                    EditorGUILayout.HelpBox(
                        "STG30/AREA00 has 24-byte UV records with 16-byte block alignment. " +
                        "Enable this to test correcting shifted UV and vertex-color records on " +
                        "later meshes without changing materials or mesh geometry. " +
                        "Off by default: other EMD layouts are not yet confirmed. " +
                        "If enabled, this mode takes precedence over Preserve Zero UV Words. " +
                        "Clear prior UV channel overrides and click Apply to test.",
                        MessageType.Warning);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("preserveZeroUvWords"),
                        new GUIContent("Preserve Zero UV Words (Experimental)"));
                    EditorGUILayout.HelpBox(
                        "Legacy EMD parsing skips zero-valued 32-bit words after each UV block. " +
                        "These zeros can be legitimate UV coordinates. Enable only for A/B comparison: " +
                        "if the source contains real padding between blocks, this option can worsen " +
                        "the next mesh's UVs. Geometry and material selection are unchanged.",
                        MessageType.Warning);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("uvChannelOverrides"),
                        new GUIContent("Mesh Source UV Channel Overrides"), true);
                    EditorGUILayout.HelpBox(
                        "Optional overrides: enter an exact imported mesh name (for example obj_00_0) " +
                        "and source UV channel 0..6. The read-only UV audit lists available channels. " +
                        "UV0 remains unchanged when no override exists. Original source UV channels " +
                        "also appear in Unity UV1..UV6 for inspection.",
                        MessageType.Info);
                    if (GUILayout.Button("Diagnose Stage EMD Buffers (Read-only)"))
                    {
                        if (selectedEmd != null)
                            EmdStageBinaryDiagnostics.DiagnosePath(selectedEmd.assetPath);
                    }
                    if (GUILayout.Button("Diagnose EMD UV Mapping (Read-only)"))
                    {
                        if (selectedEmd != null)
                            EmdUvMappingDiagnostics.DiagnosePath(selectedEmd.assetPath);
                    }
                    break;
                case 1:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("importSkeleton"), new GUIContent("Import Skeleton & Skin Weights"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("repairDegenerateBoneAxes"),
                        new GUIContent("Repair Missing Bone Axes (Experimental)"));
                    EditorGUILayout.HelpBox(
                        "When a bone has a zero-length X, Y or Z axis, rebuild one missing axis " +
                        "from the other two using a cross product. If too few independent axes " +
                        "remain, synthesize an orthogonal reference frame. Every repaired bone " +
                        "is identified in Console by name, node ID, original axis lengths and " +
                        "recovery method. Enabled by default; turn off for strict import. " +
                        "Fake axes may not reproduce the source pose exactly.",
                        MessageType.Warning);
                    EditorGUILayout.HelpBox("Skeleton and four-influence skinning are experimental until validated against a real Battride War Genesis EMD file. Generic/Humanoid avatar mapping is not automatic.", MessageType.Info);
                    break;
                case 2:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("findNearbyTextures"), new GUIContent("Find DDS/PNG Textures Nearby"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("useEmatTextureReferences"), new GUIContent("Read Companion EMAT Texture Names"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("preferUniqueColourMap"),
                        new GUIContent("Prefer Referenced _CO for Diffuse"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("linkNormalMaps"), new GUIContent("Link Normal Textures (Opt-in)"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("linkSpecularMaps"), new GUIContent("Link Specular Textures (Opt-in)"));
                    EditorGUILayout.HelpBox(
                        "If a first EMD submaterial is an auxiliary _LI/_BL/_SP/_NO, " +
                        "use a single explicitly referenced _CO when unique; or, " +
                        "when several _CO names exist, only the exact material-stem match " +
                        "(for example ground02_BL -> ground02_CO). All candidates must " +
                        "be locally loadable Texture2D assets. Unresolved cases keep " +
                        "their original assignments. This is a single-texture fallback, " +
                        "NOT reconstructed PS3 multi-texture blending. Disable the option " +
                        "to retain the original first-submaterial mapping.",
                        MessageType.Info);
                    if (GUILayout.Button("Diagnose EMD Texture Bindings (Read-only)"))
                    {
                        if (selectedEmd != null)
                            EmdTextureBindingDiagnostics.DiagnosePath(selectedEmd.assetPath);
                    }
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
