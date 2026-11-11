// PEMT source-file Inspector. ScriptedImporterEditor is essential here:
// embedding controls in a custom Editor for the generated ScriptableObject
// causes Unity to render them read-only when selecting the source .pemt asset.
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [CustomEditor(typeof(PemtImporter))]
    public sealed class PemtImporterInspector : ScriptedImporterEditor
    {
        private readonly PemtMotionPanel panel = new PemtMotionPanel();

        // The importer now persists model binding and embedded clip settings.
        // Use ScriptedImporterEditor Apply/Revert so changes rebuild the sub-assets.
        protected override bool needsApplyRevert => true;

        public override void OnInspectorGUI()
        {
            PemtImporter importer = target as PemtImporter;
            if (importer == null)
            {
                EditorGUILayout.HelpBox("No PEMT importer is selected.", MessageType.Warning);
                return;
            }

            string path = importer.assetPath;
            PemtMotionLibrary library = AssetDatabase.LoadAssetAtPath<PemtMotionLibrary>(path);
            if (library == null)
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    library = asset as PemtMotionLibrary;
                    if (library != null) break;
                }
            }

            serializedObject.Update();
            EditorGUILayout.LabelField("EMD2Unity - PEMT Animation Container", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importAnimationClips"),
                new GUIContent("Import Animation Clips"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animationRig"),
                new GUIContent("Animation Rig (EMD)", "An imported EMD model with the matching bone hierarchy."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoDetectAnimationRig"),
                new GUIContent("Auto-detect Nearby EMD"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("deltaRotationFirst"),
                new GUIContent("Delta Rotation First"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importExperimentalScale022"),
                new GUIContent("Experimental Scale 0x22"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("allowPartialAnimationClips"),
                new GUIContent("Allow Partial Clips"));
            EditorGUILayout.HelpBox(
                "Each decoded Motion ID becomes an AnimationClip sub-asset under this .pemt " +
                "(expand the asset in Project). A matching EMD rig is required for " +
                "correct hierarchical bone paths. Experimental scale 0x02040022 " +
                "and partial clips require visual verification; partial import " +
                "skips unsupported tracks only. Apply to reimport. Original .pemt " +
                "bytes and separately exported .anim assets are not modified.",
                MessageType.Info);
            if (library == null)
            {
                EditorGUILayout.HelpBox(
                    "No imported PemtMotionLibrary was found. Check Console for PEMT parsing errors, then reimport this file.",
                    MessageType.Error);
            }
            else
            {
                panel.Draw(library);
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("Open PEMT Motion Browser Window"))
                PemtMotionBrowserWindow.OpenForPath(path);
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
#endif
