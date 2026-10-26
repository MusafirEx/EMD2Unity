// Standalone browser for selecting imported CCM ScriptableObject subassets.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class CcmBrowserWindow : EditorWindow
    {
        [SerializeField] private CcmComboLibrary selectedCcm;
        private readonly CcmInfoPanel panel = new CcmInfoPanel();
        private Vector2 scroll;

        internal static void OpenForPath(string path)
        {
            var window = GetWindow<CcmBrowserWindow>("CCM Browser");
            if (!string.IsNullOrEmpty(path))
            {
                CcmComboLibrary asset = AssetDatabase.LoadAssetAtPath<CcmComboLibrary>(path);
                if (asset != null) window.selectedCcm = asset;
            }
            window.minSize = new Vector2(460f, 550f);
            window.Show();
            window.Focus();
        }

        [MenuItem("Tools/EMD2Unity/CCM Browser")]
        private static void OpenFromMenu()
        {
            OpenForPath(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("EMD2Unity - CCM Browser", EditorStyles.boldLabel);
            selectedCcm = (CcmComboLibrary)EditorGUILayout.ObjectField(
                "CCM File", selectedCcm, typeof(CcmComboLibrary), false);
            if (GUILayout.Button("Use CCM Selected in Project"))
            {
                CcmComboLibrary selected = AssetDatabase.LoadAssetAtPath<CcmComboLibrary>(
                    AssetDatabase.GetAssetPath(Selection.activeObject));
                if (selected != null) selectedCcm = selected;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            panel.Draw(selectedCcm);
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
