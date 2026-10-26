// PEMT Motion GUI drawn directly from the ScriptedImporter Inspector or EditorWindow.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    // UI state is not serialized to the imported library: the source .pemt is read-only,
    // but the motion selection, model assignment and export are editor-side controls.
    internal sealed class PemtMotionPanel
    {
        private string motionFilter = "";
        private int selectedMotionId = -1;
        private int trackPage = 0;
        private const int TrackPageSize = 24;
        private GameObject targetEmd;
        private bool rotateDeltaFirst;

        // PEMT files typically live in <character>/motion/, with EMD assets in
        // <character>/model/. This fallback works even when drag-and-drop is awkward.
        internal static List<GameObject> FindNearbyEmdModels(PemtMotionLibrary library)
        {
            var result = new List<GameObject>();
            string path = AssetDatabase.GetAssetPath(library);
            if (string.IsNullOrEmpty(path)) return result;
            string motionDirectory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(motionDirectory)) return result;
            string characterDirectory = Path.GetDirectoryName(motionDirectory);
            if (string.IsNullOrEmpty(characterDirectory)) return result;
            string modelDirectory = Path.Combine(characterDirectory, "model").Replace('\\', '/');
            if (!Directory.Exists(modelDirectory)) return result;
            foreach (string file in Directory.GetFiles(modelDirectory))
            {
                if (!file.EndsWith(".emd", StringComparison.OrdinalIgnoreCase)) continue;
                var imported = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
                if (imported != null) result.Add(imported);
            }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public void Draw(PemtMotionLibrary library)
        {
            if (library == null)
            {
                EditorGUILayout.HelpBox("PEMT motion library is not yet imported.", MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField("PEMT Motion Container", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(library.importError))
            {
                EditorGUILayout.HelpBox(library.importError, MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox(
                "Motion IDs and tracks can be inspected here. Experimental .anim export decodes float16 position/quaternion keyframes; pose composition remains under validation.",
                MessageType.Info);
            EditorGUILayout.LabelField("File bytes", library.sourceByteCount.ToString("N0"));
            EditorGUILayout.LabelField("Pointer slots", library.pointerTableEntries.ToString());
            EditorGUILayout.LabelField("Occupied Motion IDs", library.occupiedEntries.ToString());
            EditorGUILayout.LabelField("Track descriptors", library.trackCount.ToString("N0"));
            EditorGUILayout.LabelField("FPS", "60 Hz timing inferred; not confirmed playback FPS");
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("1. Select a PEMT Motion", EditorStyles.boldLabel);
            motionFilter = EditorGUILayout.TextField("Filter Motion ID", motionFilter);
            var matches = new List<PemtMotionMetadata>();
            foreach (var motion in library.motions)
            {
                if (string.IsNullOrEmpty(motionFilter) ||
                    motion.motionIdHex.IndexOf(motionFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    motion.motionId.ToString().IndexOf(motionFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    matches.Add(motion);
            }
            PemtMotionMetadata selected = null;
            if (matches.Count > 0)
            {
                string[] motionOptions = new string[matches.Count];
                for (int i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    motionOptions[i] = m.motionIdHex + "  |  " +
                        m.apparentDurationSeconds.ToString("F3") + "s  |  " +
                        m.tracks.Count + " tracks";
                }
                int current = matches.FindIndex(m => m.motionId == selectedMotionId);
                if (current < 0)
                {
                    current = matches.FindIndex(m => m.motionId != 0 && m.tracks.Count > 0);
                    if (current < 0) current = 0;
                }
                int next = EditorGUILayout.Popup("Select Motion ID", current, motionOptions);
                if (next != current) trackPage = 0;
                if (next >= 0 && next < matches.Count)
                {
                    selected = matches[next];
                    selectedMotionId = selected.motionId;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No Motion ID matches this filter. Clear the filter to select a motion.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Select an EMD Model", EditorStyles.boldLabel);
            targetEmd = (GameObject)EditorGUILayout.ObjectField(
                "EMD Model (Project/Hierarchy)", targetEmd, typeof(GameObject), true);
            var nearby = FindNearbyEmdModels(library);
            if (nearby.Count > 0)
            {
                string[] modelOptions = new string[nearby.Count + 1];
                modelOptions[0] = "(Choose model from sibling model folder)";
                int modelIndex = 0;
                for (int i = 0; i < nearby.Count; i++)
                {
                    modelOptions[i + 1] = nearby[i].name + " (" +
                        Path.GetFileName(AssetDatabase.GetAssetPath(nearby[i])) + ")";
                    if (targetEmd == nearby[i]) modelIndex = i + 1;
                }
                int picked = EditorGUILayout.Popup("Nearby EMD Models", modelIndex, modelOptions);
                if (picked != modelIndex && picked > 0)
                    targetEmd = nearby[picked - 1];
            }
            else
            {
                EditorGUILayout.HelpBox("No imported EMD model found in the sibling model/ directory. Drag an EMD asset from Project or an EMD prefab instance from Hierarchy.", MessageType.Info);
            }
            if (targetEmd == null)
                EditorGUILayout.HelpBox("Select a model above. The AnimationClip export button will become enabled.", MessageType.Info);

            if (selected == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Motion " + selected.motionIdHex, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Block offset", "0x" + selected.blockOffset.ToString("X"));
            EditorGUILayout.LabelField("Block bytes", selected.blockByteCount.ToString("N0"));
            EditorGUILayout.LabelField("Apparent duration", selected.apparentDurationSeconds.ToString("F6") + "s");
            EditorGUILayout.LabelField("Nominal frames at 60 Hz", selected.nominalFramesAt60Hz.ToString());
            EditorGUILayout.LabelField("Track count", selected.tracks.Count.ToString());
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3. Export Selected AnimationClip", EditorStyles.boldLabel);
            rotateDeltaFirst = EditorGUILayout.Toggle("Delta Rotation First", rotateDeltaFirst);
            EditorGUILayout.HelpBox("Assign the imported EMD model matching this PEMT character. The clip is generated from real float16 keyframes. When joints look twisted, compare both Delta Rotation First modes. Test on a duplicate model.",MessageType.Warning);
            using(new EditorGUI.DisabledScope(targetEmd==null))
            {
                if(GUILayout.Button("Export Selected Motion to .anim"))
                {
                    try{PemtAnimationClipExporter.Export(library,selected,targetEmd,rotateDeltaFirst);}
                    catch(Exception ex){Debug.LogException(ex);EditorUtility.DisplayDialog("PEMT Export Failed",ex.Message,"OK");}
                }
                if(GUILayout.Button("Export ALL Animations in This PEMT"))
                {
                    try{PemtAnimationBatchExporter.ExportAll(library,targetEmd,rotateDeltaFirst);}
                    catch(Exception ex){Debug.LogException(ex);EditorUtility.DisplayDialog("PEMT Batch Export Failed",ex.Message,"OK");}
                }
                if (GUILayout.Button("Preview Selected Motion (Play / Pause / Timeline)"))
                    PemtAnimationPreviewWindow.OpenForSource(library, targetEmd, selected.motionId, rotateDeltaFirst);
            }
            EditorGUILayout.HelpBox(
                "Export All creates a <PEMT name>_Animations folder beside the source PEMT, skips existing .anim files, and shows one summary. You can cancel while retaining exported files.",
                MessageType.Info);
            int maxPage = Math.Max(0, (selected.tracks.Count - 1) / TrackPageSize);
            if (trackPage > maxPage) trackPage = maxPage;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(trackPage == 0))
                if (GUILayout.Button("Previous Tracks")) trackPage--;
            GUILayout.Label("Page " + (trackPage + 1) + "/" + (maxPage + 1), GUILayout.Width(86));
            using (new EditorGUI.DisabledScope(trackPage >= maxPage))
                if (GUILayout.Button("Next Tracks")) trackPage++;
            EditorGUILayout.EndHorizontal();
            int start = trackPage * TrackPageSize;
            for (int i = start; i < Math.Min(start + TrackPageSize, selected.tracks.Count); i++)
            {
                var track = selected.tracks[i];
                EditorGUILayout.LabelField(
                    "#" + track.index + "  " + track.markerHex + "  info " + track.infoHex,
                    EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Track duration", track.apparentDurationSeconds.ToString("F6") + "s");
                EditorGUILayout.LabelField("Key times offset (block-relative)", "0x" + track.dataStartRelative.ToString("X"));
                EditorGUILayout.LabelField("Key values offset (block-relative)", track.dataEndRelative == 0 ?
                    "None (timeline track)" : "0x" + track.dataEndRelative.ToString("X"));
                EditorGUI.indentLevel--;
            }
        }
    }

    // Retained for explicit subasset selection; clicking a .pemt file uses
    // PemtImporterInspector instead, bypassing Unity's read-only asset wrapper.
    [CustomEditor(typeof(PemtMotionLibrary))]
    public sealed class PemtMotionLibraryInspector : Editor
    {
        private readonly PemtMotionPanel panel = new PemtMotionPanel();

        public override void OnInspectorGUI()
        {
            panel.Draw((PemtMotionLibrary)target);
        }
    }
}
#endif
