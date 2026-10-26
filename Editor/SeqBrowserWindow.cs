// Standalone read-only SEQ browser, useful if Unity renders imported subassets
// with disabled Inspector fields.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class SeqBrowserWindow : EditorWindow
    {
        [SerializeField] private SeqScriptLibrary selectedSeq;
        private readonly SeqInfoPanel panel = new SeqInfoPanel();
        private Vector2 scroll;

        internal static void OpenForPath(string path)
        {
            var window = GetWindow<SeqBrowserWindow>("SEQ Browser");
            if (!string.IsNullOrEmpty(path))
            {
                SeqScriptLibrary asset = AssetDatabase.LoadAssetAtPath<SeqScriptLibrary>(path);
                if (asset != null) window.selectedSeq = asset;
            }
            window.minSize = new Vector2(410, 420);
            window.Show();
            window.Focus();
        }

        [MenuItem("Tools/EMD2Unity/SEQ Browser")]
        private static void Open()
        {
            OpenForPath(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("EMD2Unity - SEQ Browser", EditorStyles.boldLabel);
            selectedSeq = (SeqScriptLibrary)EditorGUILayout.ObjectField(
                "SEQ Script", selectedSeq, typeof(SeqScriptLibrary), false);
            if (GUILayout.Button("Use SEQ Selected in Project"))
            {
                string path = AssetDatabase.GetAssetPath(Selection.activeObject);
                var asset = AssetDatabase.LoadAssetAtPath<SeqScriptLibrary>(path);
                if (asset != null) selectedSeq = asset;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            panel.Draw(selectedSeq);
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
