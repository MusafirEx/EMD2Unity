// Standalone editable PEMT tool window. This is useful even when Unity's
// imported-asset Inspector chooses to display a disabled/read-only data view.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class PemtMotionBrowserWindow : EditorWindow
    {
        [SerializeField] private PemtMotionLibrary selectedPemt;
        private readonly PemtMotionPanel panel = new PemtMotionPanel();
        private Vector2 scroll;

        internal static void OpenForPath(string path)
        {
            var window = GetWindow<PemtMotionBrowserWindow>("PEMT Motion Browser");
            if (!string.IsNullOrEmpty(path))
            {
                var library = AssetDatabase.LoadAssetAtPath<PemtMotionLibrary>(path);
                if (library != null) window.selectedPemt = library;
            }
            window.minSize = new Vector2(360f, 400f);
            window.Show();
            window.Focus();
        }

        [MenuItem("Tools/EMD2Unity/PEMT Motion Browser")]
        private static void OpenFromMenu()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            OpenForPath(path);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("EMD2Unity - PEMT Motion Browser", EditorStyles.boldLabel);
            selectedPemt = (PemtMotionLibrary)EditorGUILayout.ObjectField(
                "PEMT Container", selectedPemt, typeof(PemtMotionLibrary), false);
            if (GUILayout.Button("Use PEMT Selected in Project"))
            {
                string path = AssetDatabase.GetAssetPath(Selection.activeObject);
                var library = AssetDatabase.LoadAssetAtPath<PemtMotionLibrary>(path);
                if (library != null) selectedPemt = library;
            }

            if (selectedPemt == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a .pemt file in Project, click 'Use PEMT Selected in Project', or choose a PemtMotionLibrary above.",
                    MessageType.Info);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            panel.Draw(selectedPemt);
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
