// Read-only SEQ metadata UI shared by the source ScriptedImporter inspector
// and a standalone browser. Filters are interactive; binary is never modified.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal sealed class SeqInfoPanel
    {
        private string filter = "";
        private string category = "All";
        private bool referencedOnly;
        private int page;
        private const int PageSize = 25;

        private static string Offsets(List<int> values)
        {
            if (values == null || values.Count == 0) return "(none)";
            return string.Join(", ", values.Take(12).Select(x => "0x" + x.ToString("X")).ToArray()) +
                (values.Count > 12 ? " ... (" + values.Count + " total)" : "");
        }

        internal void Draw(SeqScriptLibrary seq)
        {
            if (seq == null)
            {
                EditorGUILayout.HelpBox("No imported SEQ metadata was found.", MessageType.Warning);
                return;
            }
            if (!string.IsNullOrEmpty(seq.importError))
            {
                EditorGUILayout.HelpBox(seq.importError, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("SEQ Binary Script (read-only)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Header words are big-endian observations, NOT documented opcode names. " +
                "Resource strings and literal pointer matches do not identify motion IDs, " +
                "frame timing, combo transitions, or instructions without VM analysis.",
                MessageType.Info);

            EditorGUILayout.LabelField("File bytes", seq.sourceByteCount.ToString("N0"));
            EditorGUILayout.LabelField("Header BE +0x00", seq.word0);
            EditorGUILayout.LabelField("Header BE +0x04", seq.word1);
            EditorGUILayout.LabelField("Header BE +0x08", seq.word2);
            EditorGUILayout.LabelField("Header BE +0x0C", seq.word3);
            EditorGUILayout.LabelField("Candidate ASCII strings", seq.strings.Count.ToString());
            EditorGUILayout.LabelField("With raw pointer candidate", seq.referencedStringCount.ToString());
            EditorGUILayout.LabelField("With 16 00 00 00 prefix", seq.prefix16StringCount.ToString());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Raw Byte Marker Frequencies", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("12 00 00 00", seq.marker12RawCount.ToString());
            EditorGUILayout.LabelField("16 00 00 00", seq.marker16RawCount.ToString());
            EditorGUILayout.LabelField("20 13 00 00", seq.marker2013RawCount.ToString());
            EditorGUILayout.HelpBox(
                "These are unaligned byte-pattern counts across the WHOLE file, " +
                "not decoded instruction counts.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resource / String Reference Index", EditorStyles.boldLabel);
            string changedFilter = EditorGUILayout.TextField("Search path / offset", filter);
            if (changedFilter != filter) { filter = changedFilter; page = 0; }

            var kinds = new List<string> { "All" };
            foreach (var item in seq.strings)
                if (!kinds.Contains(item.category)) kinds.Add(item.category);
            kinds.Sort(StringComparer.OrdinalIgnoreCase);
            kinds.Remove("All");
            kinds.Insert(0, "All");
            int categoryIndex = Math.Max(0, kinds.IndexOf(category));
            int nextCategoryIndex = EditorGUILayout.Popup("Resource type", categoryIndex, kinds.ToArray());
            if (nextCategoryIndex != categoryIndex)
            {
                category = kinds[nextCategoryIndex];
                page = 0;
            }
            bool newReferencedOnly = EditorGUILayout.Toggle("Pointer candidates only", referencedOnly);
            if (newReferencedOnly != referencedOnly) { referencedOnly = newReferencedOnly; page = 0; }

            var matches = seq.strings.Where(s =>
                (category == "All" || s.category == category) &&
                (!referencedOnly || s.rawPointerOffsets.Count > 0) &&
                (string.IsNullOrEmpty(filter) ||
                    s.value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ("0x" + s.byteOffset.ToString("X")).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            ).ToList();

            int totalPages = Math.Max(1, (matches.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, totalPages - 1);
            EditorGUILayout.LabelField("Matches", matches.Count.ToString());
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(page == 0))
                if (GUILayout.Button("Previous")) page--;
            EditorGUILayout.LabelField("Page " + (page + 1) + "/" + totalPages,
                GUILayout.Width(90));
            using (new EditorGUI.DisabledScope(page + 1 >= totalPages))
                if (GUILayout.Button("Next")) page++;
            EditorGUILayout.EndHorizontal();

            if (matches.Count == 0)
                EditorGUILayout.HelpBox("No matching strings. cpu.seq and assist.seq may have zero printable resource paths; raw header/token data is still available.", MessageType.Info);
            int first = page * PageSize;
            for (int i = first; i < Math.Min(first + PageSize, matches.Count); i++)
            {
                SeqStringReference entry = matches[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("0x" + entry.byteOffset.ToString("X") +
                    "  [" + entry.category + "]", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Copy path", GUILayout.Width(82)))
                    EditorGUIUtility.systemCopyBuffer = entry.value;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.SelectableLabel(entry.value, EditorStyles.wordWrappedLabel,
                    GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField("Raw BE offset matches", Offsets(entry.rawPointerOffsets),
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Preceded by 16 00 00 00",
                    Offsets(entry.prefix16PointerOffsets), EditorStyles.wordWrappedMiniLabel);
                if (entry.referencesTruncated)
                    EditorGUILayout.HelpBox("Pointer candidates truncated at 512 matches.", MessageType.Warning);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("First 128 Bytes (hex)", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(seq.firstBytesHex ?? "", GUILayout.MinHeight(125));
        }
    }

    [CustomEditor(typeof(SeqImporter))]
    public sealed class SeqImporterInspector : ScriptedImporterEditor
    {
        private readonly SeqInfoPanel panel = new SeqInfoPanel();
        protected override bool needsApplyRevert => false;

        public override void OnInspectorGUI()
        {
            SeqImporter importer = target as SeqImporter;
            if (importer == null) return;
            SeqScriptLibrary library = AssetDatabase.LoadAssetAtPath<SeqScriptLibrary>(importer.assetPath);
            if (library == null)
            {
                foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(importer.assetPath))
                    if ((library = obj as SeqScriptLibrary) != null) break;
            }
            panel.Draw(library);
            EditorGUILayout.Space();
            if (GUILayout.Button("Open SEQ Browser Window"))
                SeqBrowserWindow.OpenForPath(importer.assetPath);
        }
    }

    [CustomEditor(typeof(SeqScriptLibrary))]
    public sealed class SeqScriptLibraryInspector : Editor
    {
        private readonly SeqInfoPanel panel = new SeqInfoPanel();
        public override void OnInspectorGUI() { panel.Draw((SeqScriptLibrary)target); }
    }
}
#endif
