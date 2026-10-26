// Batch export uses the same verified PEMT keyframe decoder as single-motion export.
// Each Motion ID receives a separate .anim asset; no source EMD/PEMT is modified.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal static class PemtAnimationBatchExporter
    {
        internal static void ExportAll(PemtMotionLibrary library, GameObject model, bool rotateFirst)
        {
            if (library == null || model == null)
                throw new ArgumentException("Select a PEMT and matching EMD model first.");
            string sourcePath = AssetDatabase.GetAssetPath(library);
            if (string.IsNullOrEmpty(sourcePath) ||
                !sourcePath.EndsWith(".pemt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Selected asset is not a PEMT source.");
            if (library.motions == null || library.motions.Count == 0)
                throw new InvalidDataException("This PEMT contains no indexed motions.");

            // Both Project EMD assets and linked prefab instances are accepted;
            // the single-motion exporter resolves their EMD source and rest pose.
            string folder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new IOException("Source PEMT folder must exist inside Unity Assets.");
            string basename = Path.GetFileNameWithoutExtension(sourcePath);
            string outputFolderName = basename + "_Animations";
            string outputFolder = folder + "/" + outputFolderName;

            if (!EditorUtility.DisplayDialog("Export All PEMT Animations",
                "Source: " + sourcePath +
                "\nMotions: " + library.motions.Count +
                "\nOutput: " + outputFolder +
                "\n\nExport every motion that contains position/rotation keys?" +
                "\nPreviously exported .anim files will NOT be overwritten.", "Export All", "Cancel"))
                return;

            if (!AssetDatabase.IsValidFolder(outputFolder))
            {
                string newGuid = AssetDatabase.CreateFolder(folder, outputFolderName);
                if (string.IsNullOrEmpty(newGuid) || !AssetDatabase.IsValidFolder(outputFolder))
                    throw new IOException("Unable to create animation folder " + outputFolder);
            }

            int created = 0, skippedExisting = 0, skippedWithoutKeys = 0, failed = 0;
            bool cancelled = false;
            var sampleErrors = new List<string>();
            try
            {
                for (int index = 0; index < library.motions.Count; index++)
                {
                    PemtMotionMetadata motion = library.motions[index];
                    if (EditorUtility.DisplayCancelableProgressBar("PEMT Export All",
                        basename + " - " + motion.motionIdHex + " (" + (index + 1) +
                        "/" + library.motions.Count + ")", (float)index / library.motions.Count))
                    {
                        cancelled = true;
                        break;
                    }

                    bool hasTransform = false;
                    if (motion.tracks != null)
                    {
                        foreach (PemtTrackMetadata track in motion.tracks)
                        {
                            if ((track.markerHex == "0x02040021" ||
                                 track.markerHex == "0x02040028") &&
                                track.dataEndRelative > 0)
                            {
                                hasTransform = true;
                                break;
                            }
                        }
                    }
                    if (!hasTransform)
                    {
                        skippedWithoutKeys++;
                        continue;
                    }

                    // Stable names prevent collisions between Motion IDs.
                    // Different PEMT files have different output directories.
                    string assetPath = outputFolder + "/" + basename + "_Motion_" +
                        motion.motionId.ToString("X3") + ".anim";
                    if (File.Exists(assetPath) ||
                        AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                    {
                        skippedExisting++;
                        continue;
                    }

                    try
                    {
                        if (PemtAnimationClipExporter.Export(
                            library, motion, model, rotateFirst, assetPath, false))
                            created++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        string message = motion.motionIdHex + ": " + ex.Message;
                        if (sampleErrors.Count < 5) sampleErrors.Add(message);
                        Debug.LogWarning("EMD2Unity PEMT batch export failed for " + message);
                    }
                }
            }
            finally
            {
                // Save already-exported clips even when the user cancels.
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }

            string summary = "Source: " + sourcePath +
                "\nDestination: " + outputFolder +
                "\n\nExported: " + created +
                "\nAlready existed (unchanged): " + skippedExisting +
                "\nNo transform keys: " + skippedWithoutKeys +
                "\nFailed / unmatched bones: " + failed +
                "\nCancelled: " + (cancelled ? "Yes" : "No");
            if (sampleErrors.Count > 0)
                summary += "\n\nFirst errors:\n" + string.Join("\n", sampleErrors.ToArray());
            Debug.Log("EMD2Unity PEMT batch export summary:\n" + summary);
            EditorUtility.DisplayDialog("Export All PEMT Animations - Summary", summary, "OK");

            if (created > 0)
            {
                UnityEngine.Object folderAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputFolder);
                if (folderAsset != null) EditorGUIUtility.PingObject(folderAsset);
            }
        }
    }
}
#endif
