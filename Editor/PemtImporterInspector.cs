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

        // No importer settings need Apply/Revert: these controls select motions
        // and export independent .anim assets, without modifying the PEMT.
        protected override bool needsApplyRevert => false;

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

            EditorGUILayout.LabelField("EMD2Unity - PEMT Import Settings", EditorStyles.boldLabel);
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
        }
    }
}
#endif
