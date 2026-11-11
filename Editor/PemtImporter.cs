// EMD2Unity - indexed PEMT motion-container metadata importer.
// Research based on user-provided Kamen Rider Battride War Sousei update v1.02.
// Embeds Unity AnimationClip sub-assets using the same decoder as Preview/Export.
// Requires a matching EMD rig; never silently invents bone hierarchy bindings.
// Metadata asset types are declared in PemtMotionLibrary.cs (Unity MonoScript requirement).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [ScriptedImporter(6, "pemt")]
    public sealed class PemtImporter : ScriptedImporter
    {
        [Header("Embedded Animation Clips")]
        [SerializeField] private bool importAnimationClips = true;
        [Tooltip("When no EMD rig is available, import generic PEMT_jXXXX source-space AnimationClips for inspection. These clips cannot directly animate an EMD model.")]
        [SerializeField] private bool importUnboundAnimationClips = true;
        [Tooltip("Explicit imported EMD model used for bone paths and rest-pose transforms. Overrides automatic nearby-model selection.")]
        [SerializeField] private GameObject animationRig;
        [Tooltip("Find the best matching EMD skeleton in the sibling model/ folder when Animation Rig is unassigned. Do not select ambiguous ties.")]
        [SerializeField] private bool autoDetectAnimationRig = true;
        [Tooltip("Compose PEMT quaternion delta before rather than after the imported EMD rest rotation.")]
        [SerializeField] private bool deltaRotationFirst = false;
        [Tooltip("Experimental marker 0x02040022 decoded as XYZ scale multipliers, as confirmed in preview on selected motions. Inspect scale deformation visually.")]
        [SerializeField] private bool importExperimentalScale022 = true;
        [Tooltip("Off by default: skip unknown track markers and create visibly incomplete clips. Any malformed supported track still fails.")]
        [SerializeField] private bool allowPartialAnimationClips = false;

        private static HashSet<int> ReferencedBoneIds(PemtMotionLibrary library)
        {
            var ids = new HashSet<int>();
            foreach (PemtMotionMetadata motion in library.motions)
            {
                if (motion.tracks == null) continue;
                foreach (PemtTrackMetadata track in motion.tracks)
                {
                    if (track == null ||
                        (track.markerHex != "0x02040021" &&
                         track.markerHex != "0x02040028" &&
                         track.markerHex != "0x02040022")) continue;
                    if (string.IsNullOrEmpty(track.infoHex) ||
                        !track.infoHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (uint.TryParse(track.infoHex.Substring(2),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out uint info))
                        ids.Add((int)(info >> 16));
                }
            }
            return ids;
        }

        private static int CountRigMatches(GameObject model, HashSet<int> jointIds)
        {
            if (model == null) return 0;
            Transform armature = model.transform.Find("Armature");
            if (armature == null) return 0;
            int matches = 0;
            var matched = new HashSet<int>();
            foreach (Transform transform in armature.GetComponentsInChildren<Transform>(true))
            {
                Match result = Regex.Match(transform.name, @"_j(\d+)$", RegexOptions.IgnoreCase);
                if (!result.Success ||
                    !int.TryParse(result.Groups[1].Value, out int id) ||
                    !jointIds.Contains(id) ||
                    !matched.Add(id)) continue;
                matches++;
            }
            return matches;
        }

        private GameObject SelectRig(string pemtPath, PemtMotionLibrary library,
            out string explanation)
        {
            explanation = "";
            if (animationRig != null)
            {
                try
                {
                    GameObject model = PemtAnimationClipExporter.GetSourceEmdModel(animationRig);
                    string source = AssetDatabase.GetAssetPath(model);
                    if (string.IsNullOrEmpty(source) ||
                        !source.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
                    {
                        explanation = "Assigned animation rig is not an imported EMD asset.";
                        return null;
                    }
                    explanation = "Using explicitly assigned EMD rig: " + source;
                    return model;
                }
                catch (Exception ex)
                {
                    explanation = "Invalid assigned animation rig: " + ex.Message;
                    return null;
                }
            }
            if (!autoDetectAnimationRig)
            {
                explanation = "Animation Rig is unassigned and auto detection is disabled.";
                return null;
            }
            string motionFolder = Path.GetDirectoryName(pemtPath);
            string characterFolder = motionFolder == null ? null : Path.GetDirectoryName(motionFolder);
            string modelDir = characterFolder == null ? null :
                Path.Combine(characterFolder, "model");
            if (string.IsNullOrEmpty(modelDir) || !Directory.Exists(modelDir))
            {
                explanation = "No sibling model/ directory. Assign an EMD Animation Rig.";
                return null;
            }
            string[] files = Directory.GetFiles(modelDir, "*", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            HashSet<int> ids = ReferencedBoneIds(library);
            int best = 0, ties = 0;
            GameObject selected = null;
            foreach (string file in files)
            {
                if (!file.EndsWith(".emd", StringComparison.OrdinalIgnoreCase)) continue;
                string modelPath = file.Replace('\\', '/');
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                int score = CountRigMatches(model, ids);
                if (score <= 0) continue;
                if (score > best)
                {
                    best = score;
                    ties = 1;
                    selected = model;
                }
                else if (score == best)
                    ties++;
            }
            if (best <= 0)
            {
                explanation = "No nearby imported EMD skeleton matched the PEMT bone IDs. " +
                    "Assign Animation Rig in the PEMT Inspector.";
                return null;
            }
            if (ties > 1)
            {
                explanation = "Ambiguous nearby EMD skeletons: " + ties +
                    " models matched " + best + " joint IDs equally. " +
                    "Select the intended EMD in Animation Rig; no arbitrary model was chosen.";
                return null;
            }
            explanation = "Auto-detected EMD: " + AssetDatabase.GetAssetPath(selected) +
                " (" + best + "/" + ids.Count + " referenced joint IDs matched)";
            return selected;
        }

        private void ImportAnimationClips(AssetImportContext ctx,
            byte[] bytes, PemtMotionLibrary library)
        {
            if (!importAnimationClips || library.motions.Count == 0) return;
            GameObject model = SelectRig(ctx.assetPath, library, out string explanation);
            bool unbound = model == null;
            if (unbound)
            {
                library.animationRigMessage = explanation +
                    " Unbound clips use generic PEMT_jXXXX one-level paths and raw source-space transforms.";
                if (!importUnboundAnimationClips)
                {
                    ctx.LogImportWarning("EMD2Unity PEMT: " + explanation +
                        " Unbound clip import is disabled. Only metadata was imported.");
                    return;
                }
                ctx.LogImportWarning("EMD2Unity PEMT: " + explanation +
                    " Falling back to unbound source-space AnimationClip sub-assets. " +
                    "Assign the matching EMD Animation Rig and Apply for playable clips.");
            }
            else
            {
                string rigPath = AssetDatabase.GetAssetPath(model);
                ctx.DependsOnSourceAsset(rigPath);
                library.animationRigPath = rigPath;
                library.animationRigMessage = explanation;
            }
            int failed = 0, skipped = 0, partial = 0, scale = 0;
            var errors = new List<string>();
            string baseName = Path.GetFileNameWithoutExtension(ctx.assetPath);

            foreach (PemtMotionMetadata motion in library.motions)
            {
                if (motion.tracks == null ||
                    !motion.tracks.Exists(t => t != null &&
                        (t.markerHex == "0x02040021" ||
                         t.markerHex == "0x02040028" ||
                         (importExperimentalScale022 && t.markerHex == "0x02040022")) &&
                        t.dataEndRelative > 0))
                {
                    skipped++;
                    continue;
                }
                // Keep per-Motion-ID asset identifiers deterministic across
                // reimports and leave already imported clips independent.
                AnimationClip clip = null;
                try
                {
                    var skippedTracks = new List<string>();
                    var scaleTracks = new List<string>();
                    clip = unbound
                        ? PemtAnimationClipExporter.CreateUnboundFromSourceBytes(
                            ctx.assetPath, bytes, motion, allowPartialAnimationClips,
                            skippedTracks, importExperimentalScale022, scaleTracks)
                        : PemtAnimationClipExporter.CreateFromSourceBytes(
                            ctx.assetPath, bytes, motion, model, deltaRotationFirst,
                            allowPartialAnimationClips, skippedTracks,
                            importExperimentalScale022, scaleTracks);
                    clip.name = baseName + "_Motion_" + motion.motionId.ToString("X3");
                    ctx.AddObjectToAsset("motion_" + motion.motionId.ToString("X4"), clip);
                    library.embeddedClipCount++;
                    if (unbound) library.embeddedUnboundCount++;
                    if (skippedTracks.Count > 0) partial++;
                    if (scaleTracks.Count > 0) scale++;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (clip != null && !AssetDatabase.Contains(clip))
                        UnityEngine.Object.DestroyImmediate(clip);
                    if (errors.Count < 8)
                        errors.Add(motion.motionIdHex + ": " + ex.Message);
                }
            }
            library.embeddedFailedCount = failed;
            library.embeddedSkippedCount = skipped;
            library.embeddedPartialCount = partial;
            library.embeddedScaleCount = scale;
            string summary = "EMD2Unity PEMT embedded Motion IDs: " +
                library.embeddedClipCount + " clips imported (" +
                library.embeddedUnboundCount + " unbound), " + skipped +
                " without supported transform tracks, " + failed + " decode failures. " +
                explanation + (scale > 0 ? ". " + scale +
                    " clips use EXPERIMENTAL 0x02040022 scale semantics." : "") +
                (partial > 0 ? " WARNING: " + partial +
                    " partial clip(s) omit unsupported track channels." : "");
            if (failed > 0 || partial > 0 || scale > 0)
                ctx.LogImportWarning(summary);
            else
                Debug.Log(summary);
            if (errors.Count > 0)
                ctx.LogImportWarning("EMD2Unity PEMT: first clip errors:\n" +
                    string.Join("\n", errors.ToArray()));
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            PemtMotionLibrary library;
            byte[] bytes = null;
            try
            {
                bytes = File.ReadAllBytes(ctx.assetPath);
                library = PemtReader.Read(bytes);
            }
            catch (Exception ex)
            {
                library = ScriptableObject.CreateInstance<PemtMotionLibrary>();
                library.importError = ex.Message;
                ctx.LogImportError("EMD2Unity PEMT: " + ex.Message);
            }
            if (library.headerSizeMismatch && string.IsNullOrEmpty(library.importError))
            {
                ctx.LogImportWarning(
                    "EMD2Unity PEMT: header declares " + library.headerDeclaredByteCount +
                    " bytes, physical file has " + library.sourceByteCount +
                    " bytes. Accepted as an aligned size-mismatch variant after " +
                    "validating indexed motion/track bounds. Animation keyframes " +
                    "still require visual or export verification.");
            }
            library.name = Path.GetFileNameWithoutExtension(ctx.assetPath) + " [PEMT Motions]";
            ctx.AddObjectToAsset("motions", library);
            ctx.SetMainObject(library);
            if (string.IsNullOrEmpty(library.importError) && bytes != null)
            {
                try { ImportAnimationClips(ctx, bytes, library); }
                catch (Exception ex)
                {
                    // Container metadata is still useful even when a rig
                    // dependency or unexpected sub-asset decode fails.
                    library.animationRigMessage = "Embedded animation import failed: " + ex.Message;
                    ctx.LogImportWarning("EMD2Unity PEMT: " + library.animationRigMessage +
                        ". Motion metadata remains importable. " +
                        "See Console and retry with a matching EMD Animation Rig.");
                }
            }
        }
    }

    internal static class PemtReader
    {
        private const int MaxFileSize = 128 * 1024 * 1024;
        private const int MaxPointerEntries = 65536;
        private const int MaxTracksPerBlock = 50000;
        private const int DescriptorSize = 32;

        private static uint U32(byte[] data, int pos)
        {
            if (pos < 0 || pos > data.Length - 4)
                throw new InvalidDataException("PEMT: unexpected end of file at 0x" + pos.ToString("X"));
            return ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16)
                   | ((uint)data[pos + 2] << 8) | data[pos + 3];
        }

        private static float F32(byte[] data, int pos)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(U32(data, pos)), 0);
        }

        private static int SafeInt(uint n, string field)
        {
            if (n > int.MaxValue)
                throw new InvalidDataException("PEMT: invalid " + field + ": " + n);
            return (int)n;
        }

        public static PemtMotionLibrary Read(byte[] data)
        {
            if (data == null || data.Length < 0x10 || data.Length > MaxFileSize)
                throw new InvalidDataException("PEMT: invalid file length.");
            uint word0 = U32(data, 0);
            if (word0 != 0x400u)
                throw new InvalidDataException("PEMT: unexpected initial word 0x" + word0.ToString("X8"));
            int pointerCount = SafeInt(U32(data, 4), "pointer count");
            int tableStart = SafeInt(U32(data, 8), "pointer table offset");
            int declaredBytes = SafeInt(U32(data, 12), "declared file size");
            string details = " (header: pointerCount=" + pointerCount +
                ", tableOffset=0x" + tableStart.ToString("X") +
                ", declaredSize=" + declaredBytes +
                ", actualSize=" + data.Length + ").";
            if (pointerCount < 1 || pointerCount > MaxPointerEntries)
                throw new InvalidDataException("PEMT: invalid pointer count." + details);
            if (tableStart != 0x10)
                throw new InvalidDataException("PEMT: unexpected pointer table offset; expected 0x10." + details);
            bool sizeMismatch = declaredBytes != data.Length;
            // Some observed ACL PEMTs have a larger header size than their
            // physical file, even though all indexed pointers are in range.
            // Accept this ONLY for aligned, larger declared sizes, and ONLY
            // if the entire pointer/track structure below still validates.
            // Never use declaredBytes for buffer access.
            if (sizeMismatch &&
                (declaredBytes <= data.Length || declaredBytes > MaxFileSize ||
                 (declaredBytes & 0xF) != 0 || (data.Length & 0xF) != 0))
                throw new InvalidDataException(
                    "PEMT: unsupported declared/physical file-size mismatch." + details);
            if ((long)tableStart + pointerCount * 4L > data.Length)
                throw new InvalidDataException("PEMT: pointer table extends past end of file." + details);

            long tableEnd = (long)tableStart + pointerCount * 4L;
            var ids = new List<int>();
            var offsets = new List<int>();
            int lastOffset = 0;
            for (int id = 0; id < pointerCount; id++)
            {
                int off = SafeInt(U32(data, tableStart + id * 4), "motion pointer");
                if (off == 0) continue;
                if ((long)off < tableEnd || (off & 0xF) != 0 || off <= lastOffset || off >= data.Length)
                    throw new InvalidDataException("PEMT: invalid or non-monotonic pointer at motion ID 0x" +
                        id.ToString("X3"));
                ids.Add(id);
                offsets.Add(off);
                lastOffset = off;
            }
            if (ids.Count == 0 || ids[0] != 0)
                throw new InvalidDataException("PEMT: default motion ID 0 is missing.");

            PemtMotionLibrary library = ScriptableObject.CreateInstance<PemtMotionLibrary>();
            library.sourceByteCount = data.Length;
            library.headerDeclaredByteCount = declaredBytes;
            library.headerSizeMismatch = sizeMismatch;
            library.headerSizeDifference = declaredBytes - data.Length;
            library.pointerTableEntries = pointerCount;
            library.occupiedEntries = ids.Count;

            for (int i = 0; i < ids.Count; i++)
            {
                int start = offsets[i];
                int end = (i + 1 < ids.Count) ? offsets[i + 1] : data.Length;
                int blockBytes = end - start;
                if (blockBytes < 16)
                    throw new InvalidDataException("PEMT: truncated motion 0x" + ids[i].ToString("X3"));
                int trackCount = SafeInt(U32(data, start), "track count");
                int trackOffset = SafeInt(U32(data, start + 4), "track table offset");
                float duration = F32(data, start + 12);
                long trackTableEnd = 16L + trackCount * (long)DescriptorSize;
                if (trackCount < 0 || trackCount > MaxTracksPerBlock ||
                    trackOffset != 0x10 || trackTableEnd > blockBytes ||
                    float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
                    throw new InvalidDataException("PEMT: invalid track table or duration at motion 0x" +
                        ids[i].ToString("X3"));

                var motion = new PemtMotionMetadata
                {
                    motionId = ids[i],
                    motionIdHex = "0x" + ids[i].ToString("X3"),
                    blockOffset = start,
                    blockByteCount = blockBytes,
                    apparentDurationSeconds = duration,
                    nominalFramesAt60Hz = Mathf.RoundToInt(duration * 60f)
                };

                for (int t = 0; t < trackCount; t++)
                {
                    int pos = start + trackOffset + t * DescriptorSize;
                    uint marker = U32(data, pos);
                    uint info = U32(data, pos + 4);
                    float trackDuration = F32(data, pos + 8);
                    uint unknown = U32(data, pos + 12);
                    int payloadStart = SafeInt(U32(data, pos + 16), "track payload offset");
                    int payloadEnd = SafeInt(U32(data, pos + 20), "track payload end");
                    uint reserved1 = U32(data, pos + 24);
                    uint reserved2 = U32(data, pos + 28);
                    if (payloadStart < trackTableEnd || payloadStart >= blockBytes ||
                        (payloadEnd != 0 && (payloadEnd < payloadStart || payloadEnd > blockBytes)) ||
                        float.IsNaN(trackDuration) || float.IsInfinity(trackDuration))
                        throw new InvalidDataException("PEMT: invalid track " + t +
                            " in motion 0x" + ids[i].ToString("X3"));
                    motion.tracks.Add(new PemtTrackMetadata
                    {
                        index = t,
                        markerHex = "0x" + marker.ToString("X8"),
                        infoHex = "0x" + info.ToString("X8"),
                        apparentDurationSeconds = trackDuration,
                        dataStartRelative = payloadStart,
                        dataEndRelative = payloadEnd,
                        unknownWordHex = "0x" + unknown.ToString("X8"),
                        reserved1Hex = "0x" + reserved1.ToString("X8"),
                        reserved2Hex = "0x" + reserved2.ToString("X8")
                    });
                }
                library.trackCount += trackCount;
                library.motions.Add(motion);
            }
            return library;
        }
    }
}
#endif
