// Read-only PEMT metadata Inspector. No animation decoding is implied by the UI.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [CustomEditor(typeof(PemtMotionLibrary))]
    public sealed class PemtMotionLibraryInspector : Editor
    {
        private string motionFilter = "";
        private int selectedMotionId = -1;
        private int trackPage = 0;
        private const int TrackPageSize = 24;
        private Vector2 listScroll;

        public override void OnInspectorGUI()
        {
            var library = (PemtMotionLibrary)target;
            EditorGUILayout.LabelField("PEMT Motion Container", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(library.importError))
            {
                EditorGUILayout.HelpBox(library.importError, MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox(
                "Read-only structural inventory. PEMT keyframe encoding, bone mapping and blend rules are not yet decoded. No AnimationClip has been created.",
                MessageType.Info);
            EditorGUILayout.LabelField("File bytes", library.sourceByteCount.ToString("N0"));
            EditorGUILayout.LabelField("Pointer slots", library.pointerTableEntries.ToString());
            EditorGUILayout.LabelField("Occupied Motion IDs", library.occupiedEntries.ToString());
            EditorGUILayout.LabelField("Track descriptors", library.trackCount.ToString("N0"));
            EditorGUILayout.LabelField("FPS", "60 Hz timing inferred; not confirmed playback FPS");
            EditorGUILayout.Space();

            motionFilter = EditorGUILayout.TextField("Find Motion ID", motionFilter);
            EditorGUILayout.LabelField("Click a motion to view its track descriptors", EditorStyles.miniLabel);
            var matches = new List<PemtMotionMetadata>();
            foreach (var motion in library.motions)
            {
                if (string.IsNullOrEmpty(motionFilter) ||
                    motion.motionIdHex.IndexOf(motionFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    motion.motionId.ToString().IndexOf(motionFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    matches.Add(motion);
            }
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.MaxHeight(240));
            foreach (var motion in matches)
            {
                string label = motion.motionIdHex + "  |  " +
                    motion.apparentDurationSeconds.ToString("F3") + "s  |  " +
                    motion.tracks.Count + " tracks";
                if (GUILayout.Toggle(selectedMotionId == motion.motionId, label, "Button"))
                {
                    if (selectedMotionId != motion.motionId)
                    {
                        selectedMotionId = motion.motionId;
                        trackPage = 0;
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            PemtMotionMetadata selected = library.motions.Find(m => m.motionId == selectedMotionId);
            if (selected == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Motion " + selected.motionIdHex, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Block offset", "0x" + selected.blockOffset.ToString("X"));
            EditorGUILayout.LabelField("Block bytes", selected.blockByteCount.ToString("N0"));
            EditorGUILayout.LabelField("Apparent duration", selected.apparentDurationSeconds.ToString("F6") + "s");
            EditorGUILayout.LabelField("Nominal frames at 60 Hz", selected.nominalFramesAt60Hz.ToString());
            EditorGUILayout.LabelField("Track count", selected.tracks.Count.ToString());
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
                EditorGUILayout.LabelField("Payload offset (block-relative)", "0x" + track.dataStartRelative.ToString("X"));
                EditorGUILayout.LabelField("Payload end (block-relative)", track.dataEndRelative == 0 ?
                    "0 (unspecified)" : "0x" + track.dataEndRelative.ToString("X"));
                EditorGUI.indentLevel--;
            }
        }
    }
}
#endif
