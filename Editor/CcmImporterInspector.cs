// Read-only CCM inspector: labels use actual record offsets, not guessed names
// for gameplay functions. This panel is shared by the importer and browser.
#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal sealed class CcmInfoPanel
    {
        private int selectedIndex;
        private bool showAllRaw;
        private Vector2 recordScroll;

        private static string FormatRow(CcmComboRecord r)
        {
            return "#" + r.recordIndex.ToString("D2") + " | A=" + r.valueAt14 +
                " B=" + r.valueAt18 + " | code " + r.codeAt1CHex +
                " | link " + r.linkAt68Hex;
        }

        internal void Draw(CcmComboLibrary library)
        {
            if (library == null)
            {
                EditorGUILayout.HelpBox("Select an imported .ccm file.", MessageType.Info);
                return;
            }
            if (!string.IsNullOrEmpty(library.importError))
            {
                EditorGUILayout.HelpBox(library.importError, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("CCM Combo Records (read-only)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Confirmed: little-endian CCM\\0 signature, pointer table, 0x80-byte records, footer and raw values. " +
                "The meanings of fields +0x14/+0x18 (Timing A/B), +0x1C (code) and +0x68 (link candidate) " +
                "have NOT been proven in gameplay. These codes are not PEMT Motion IDs.",
                MessageType.Info);

            EditorGUILayout.LabelField("Source bytes", library.sourceByteCount.ToString("N0"));
            EditorGUILayout.LabelField("Version marker (LE)", library.versionHex);
            EditorGUILayout.LabelField("Record count", library.recordCount.ToString());
            EditorGUILayout.LabelField("Record size", "0x80 (128 bytes)");
            EditorGUILayout.LabelField("Pointer table", "0x10 (8 bytes per entry)");
            EditorGUILayout.LabelField("First record", "0x" + library.firstRecordOffset.ToString("X"));
            EditorGUILayout.LabelField("Footer", "0x" + library.footerOffset.ToString("X"));
            EditorGUILayout.LabelField("All A - B = 20", library.allDifferences20 ? "Yes (observed)" : "No");
            EditorGUILayout.LabelField("Links reference valid indexes",
                library.allLinksPointToRecordIds ? "Yes (low 16 bits)" : "Not all");

            if (library.records == null || library.records.Count == 0)
                return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Indexed Combo Records", EditorStyles.boldLabel);

            // Compact summary of each record; long files scroll independently.
            recordScroll = EditorGUILayout.BeginScrollView(recordScroll, GUILayout.Height(
                Mathf.Clamp(library.records.Count * 25 + 10, 70, 225)));
            for (int i = 0; i < library.records.Count; ++i)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isSelected = selectedIndex == i;
                    if (GUILayout.Toggle(isSelected, FormatRow(library.records[i]), "Button"))
                        selectedIndex = i;
                }
            }
            EditorGUILayout.EndScrollView();

            selectedIndex = Mathf.Clamp(selectedIndex, 0, library.records.Count - 1);
            CcmComboRecord record = library.records[selectedIndex];
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Record #" + record.recordIndex, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Table ID / Record ID", record.tableId + " / " + record.recordId);
            EditorGUILayout.LabelField("Byte offset", "0x" + record.recordOffset.ToString("X"));
            EditorGUILayout.LabelField("+0x14 / Timing A", record.valueAt14.ToString());
            EditorGUILayout.LabelField("+0x18 / Timing B", record.valueAt18.ToString());
            EditorGUILayout.LabelField("Observed difference A - B", record.differenceAminusB.ToString());
            EditorGUILayout.LabelField("+0x1C / Raw code", record.codeAt1CHex);
            EditorGUILayout.LabelField("Code upper/lower 16", "0x" +
                record.codeAt1CGroup.ToString("X4") + " / 0x" + record.codeAt1CIndex.ToString("X4"));
            EditorGUILayout.LabelField("+0x68 / Raw link candidate", record.linkAt68Hex);
            EditorGUILayout.LabelField("Link upper/lower 16", "0x" +
                record.linkAt68Group.ToString("X4") + " / 0x" + record.linkAt68Index.ToString("X4"));
            using (new EditorGUI.DisabledScope(!record.linkIndexIsPresent))
                if (GUILayout.Button("Inspect Record #" + record.linkAt68Index +
                    " (link low16, meaning unverified)"))
                    selectedIndex = record.linkAt68Index;

            showAllRaw = EditorGUILayout.Foldout(showAllRaw, "All 32 raw little-endian words");
            if (showAllRaw)
            {
                for (int i = 0; i < record.rawWordsHex.Length; ++i)
                    EditorGUILayout.LabelField("+0x" + (i * 4).ToString("X2"), record.rawWordsHex[i]);
            }
            if (GUILayout.Button("Copy Selected Record (offsets + raw values)"))
            {
                var txt = new StringBuilder();
                txt.AppendLine("CCM record " + record.recordIndex + " at 0x" +
                    record.recordOffset.ToString("X"));
                for (int i = 0; i < record.rawWordsHex.Length; ++i)
                    txt.AppendLine("0x" + (i * 4).ToString("X2") + "\t" + record.rawWordsHex[i]);
                EditorGUIUtility.systemCopyBuffer = txt.ToString();
            }
        }
    }

    [CustomEditor(typeof(CcmImporter))]
    public sealed class CcmImporterInspector : ScriptedImporterEditor
    {
        private readonly CcmInfoPanel panel = new CcmInfoPanel();
        protected override bool needsApplyRevert => false;

        public override void OnInspectorGUI()
        {
            var importer = target as CcmImporter;
            if (importer == null) return;
            CcmComboLibrary library = AssetDatabase.LoadAssetAtPath<CcmComboLibrary>(importer.assetPath);
            if (library == null)
                foreach (UnityEngine.Object subasset in AssetDatabase.LoadAllAssetsAtPath(importer.assetPath))
                    if ((library = subasset as CcmComboLibrary) != null) break;
            panel.Draw(library);
            EditorGUILayout.Space();
            if (GUILayout.Button("Open CCM Browser Window"))
                CcmBrowserWindow.OpenForPath(importer.assetPath);
        }
    }

    [CustomEditor(typeof(CcmComboLibrary))]
    public sealed class CcmComboLibraryInspector : Editor
    {
        private readonly CcmInfoPanel panel = new CcmInfoPanel();
        public override void OnInspectorGUI() { panel.Draw((CcmComboLibrary)target); }
    }
}
#endif
