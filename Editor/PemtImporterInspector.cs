// PEMT source-file Inspector. ScriptedImporterEditor is essential here:
// embedding controls in a custom Editor for the generated ScriptableObject
// causes Unity to render them read-only when selecting the source .pemt asset.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [CustomEditor(typeof(PemtImporter))]
    public sealed class PemtImporterInspector : ScriptedImporterEditor
    {
        private readonly PemtMotionPanel panel = new PemtMotionPanel();
        private string loopFilter = "";
        private int loopPage;
        private const int LoopPageSize = 16;
        private static readonly string[] LoopModes = { "Default", "Once", "Loop" };

        private static int FindLoopOverride(SerializedProperty array, int motionId)
        {
            for (int index = 0; index < array.arraySize; index++)
            {
                SerializedProperty item = array.GetArrayElementAtIndex(index);
                if (item.FindPropertyRelative("motionId").intValue == motionId)
                    return index;
            }
            return -1;
        }

        private static void SetLoopOverride(SerializedProperty array, int index,
            int motionId, int mode, bool pose)
        {
            if (mode == 0)
            {
                if (index >= 0) array.DeleteArrayElementAtIndex(index);
                return;
            }
            if (index < 0)
            {
                index = array.arraySize;
                array.InsertArrayElementAtIndex(index);
            }
            SerializedProperty item = array.GetArrayElementAtIndex(index);
            item.FindPropertyRelative("motionId").intValue = motionId;
            item.FindPropertyRelative("loopTime").boolValue = mode == 2;
            item.FindPropertyRelative("loopPose").boolValue = mode == 2 && pose;
        }

        private void DrawMotionLoopSettings(PemtMotionLibrary library)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Per-Motion Loop Settings", EditorStyles.boldLabel);
            SerializedProperty defaultLoop = serializedObject.FindProperty("defaultLoopTime");
            EditorGUILayout.PropertyField(defaultLoop, new GUIContent("Default Loop Time",
                "Unchecked means Once for any Motion ID without an override."));
            EditorGUILayout.HelpBox(
                "Loop settings are saved on this PEMT importer, by numeric Motion ID. " +
                "Default = inherit the global setting; Once = play once; Loop = repeat. " +
                "Loop Pose is optional and may need visual testing. Click Apply below " +
                "to update all embedded AnimationClip sub-assets. Standalone .anim files " +
                "already exported are not overwritten.", MessageType.Info);

            if (library == null || library.motions == null || library.motions.Count == 0)
                return;
            string nextFilter = EditorGUILayout.TextField("Find Motion ID", loopFilter);
            if (nextFilter != loopFilter)
            {
                loopFilter = nextFilter;
                loopPage = 0;
            }

            var filtered = new List<PemtMotionMetadata>();
            foreach (PemtMotionMetadata motion in library.motions)
            {
                if (string.IsNullOrEmpty(loopFilter) ||
                    motion.motionIdHex.IndexOf(loopFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    motion.motionId.ToString().IndexOf(loopFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add(motion);
            }
            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("No Motion ID matches the filter.", MessageType.Info);
                return;
            }
            int maxPage = (filtered.Count - 1) / LoopPageSize;
            loopPage = Mathf.Clamp(loopPage, 0, maxPage);
            SerializedProperty overrides = serializedObject.FindProperty("motionLoopOverrides");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Motion ID / Duration", EditorStyles.miniBoldLabel);
            GUILayout.Label("Mode", EditorStyles.miniBoldLabel, GUILayout.Width(85));
            GUILayout.Label("Pose", EditorStyles.miniBoldLabel, GUILayout.Width(45));
            EditorGUILayout.EndHorizontal();

            int first = loopPage * LoopPageSize;
            for (int i = first; i < Mathf.Min(first + LoopPageSize, filtered.Count); i++)
            {
                PemtMotionMetadata motion = filtered[i];
                int index = FindLoopOverride(overrides, motion.motionId);
                bool currentLoop = index >= 0 &&
                    overrides.GetArrayElementAtIndex(index).FindPropertyRelative("loopTime").boolValue;
                bool currentPose = index >= 0 &&
                    overrides.GetArrayElementAtIndex(index).FindPropertyRelative("loopPose").boolValue;
                int mode = index < 0 ? 0 : currentLoop ? 2 : 1;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(motion.motionIdHex + "  (" +
                    motion.apparentDurationSeconds.ToString("F2") + "s)");
                int nextMode = EditorGUILayout.Popup(mode, LoopModes, GUILayout.Width(85));
                bool nextPose = currentPose;
                using (new EditorGUI.DisabledScope(nextMode != 2))
                    nextPose = EditorGUILayout.Toggle(currentPose, GUILayout.Width(45));
                EditorGUILayout.EndHorizontal();
                if (nextMode != mode || (nextMode == 2 && nextPose != currentPose))
                    SetLoopOverride(overrides, index, motion.motionId, nextMode, nextPose);
            }
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(loopPage <= 0))
                if (GUILayout.Button("Previous")) loopPage--;
            GUILayout.Label("Page " + (loopPage + 1) + "/" + (maxPage + 1),
                GUILayout.Width(95));
            using (new EditorGUI.DisabledScope(loopPage >= maxPage))
                if (GUILayout.Button("Next")) loopPage++;
            EditorGUILayout.EndHorizontal();
        }

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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importUnboundAnimationClips"),
                new GUIContent("Import Unbound Clips Without EMD"));
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
            DrawMotionLoopSettings(library);
            EditorGUILayout.HelpBox(
                "Each decoded Motion ID becomes an AnimationClip sub-asset under this .pemt " +
                "(expand the asset in Project). Matching EMD rigs provide hierarchical " +
                "bone paths and rest pose. Without one, optional unbound clips use " +
                "generic PEMT_jXXXX paths and raw source-space curves and cannot " +
                "directly animate an EMD. Experimental scale 0x02040022 and partial " +
                "clips require visual verification. Apply to reimport; source PEMT " +
                "bytes and separately exported .anim files remain unchanged.",
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
