// Read-only EMD UV audit. Does not modify the model or any texture files.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal static class EmdUvMappingDiagnostics
    {
        [MenuItem("Tools/EMD2Unity/Diagnose Selected EMD UV Mapping")]
        private static void DiagnoseSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
            {
                GameObject obj = Selection.activeGameObject;
                if (obj != null)
                {
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(obj);
                    if (source == null) source = PrefabUtility.GetCorrespondingObjectFromSource(obj);
                    if (source != null) path = AssetDatabase.GetAssetPath(source);
                }
            }
            DiagnosePath(path);
        }

        internal static void DiagnosePath(string path)
        {
            try
            {
                string report = BuildReport(path);
                Debug.Log(report);
                EditorGUIUtility.systemCopyBuffer = report;
                EditorUtility.DisplayDialog("EMD2Unity UV Audit",
                    "Read-only UV report copied to Console and clipboard. " +
                    "No imported meshes, original EMD or textures were changed.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("EMD2Unity UV audit failed: " + ex);
                EditorUtility.DisplayDialog("EMD2Unity UV Audit", ex.Message, "OK");
            }
        }

        private static string FormatVector(Vector2 v)
        {
            return "(" + v.x.ToString("G5", System.Globalization.CultureInfo.InvariantCulture) +
                ", " + v.y.ToString("G5", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        private static string Statistics(Vector2[] values)
        {
            if (values == null || values.Length == 0) return "(no UVs)";
            int nonFinite = 0, extreme = 0, origin = 0;
            float minU = float.PositiveInfinity, minV = float.PositiveInfinity;
            float maxU = float.NegativeInfinity, maxV = float.NegativeInfinity;
            foreach (Vector2 v in values)
            {
                if (float.IsNaN(v.x) || float.IsInfinity(v.x) ||
                    float.IsNaN(v.y) || float.IsInfinity(v.y))
                {
                    nonFinite++;
                    continue;
                }
                minU = Mathf.Min(minU, v.x); maxU = Mathf.Max(maxU, v.x);
                minV = Mathf.Min(minV, v.y); maxV = Mathf.Max(maxV, v.y);
                // Large UVs can mean deliberate texture tiling; this is only a check.
                if (Mathf.Abs(v.x) > 64f || Mathf.Abs(v.y) > 64f) extreme++;
                if (Mathf.Abs(v.x) < 1e-6f && Mathf.Abs(v.y) < 1e-6f) origin++;
            }
            string extent = nonFinite == values.Length ? "(no finite coordinates)" :
                "U " + minU.ToString("G5") + ".." + maxU.ToString("G5") +
                ", V " + minV.ToString("G5") + ".." + maxV.ToString("G5");
            return extent + " | nonfinite=" + nonFinite + "/" + values.Length +
                " large(|UV|>64)=" + extreme + " near-origin=" + origin +
                " | first=" + FormatVector(values[0]) +
                (values.Length > 1 ? " second=" + FormatVector(values[1]) : "");
        }

        // Only flags channels worth testing. Finite, bounded coordinates and
        // nonzero variation do NOT prove which PS3 sampler uses a channel.
        private static string CandidateChannels(EmdGroup group)
        {
            if (group.SourceUvChannels == null) return "(none)";
            var candidates = new List<string>();
            for (int channel = 0; channel < group.SourceUvChannels.Length; channel++)
            {
                Vector2[] values = group.SourceUvChannels[channel];
                if (values == null || values.Length < 2) continue;
                float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
                float minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
                int invalid = 0, extreme = 0;
                foreach (Vector2 uv in values)
                {
                    if (float.IsNaN(uv.x) || float.IsInfinity(uv.x) ||
                        float.IsNaN(uv.y) || float.IsInfinity(uv.y))
                    {
                        invalid++;
                        continue;
                    }
                    if (Mathf.Abs(uv.x) > 128f || Mathf.Abs(uv.y) > 128f)
                        extreme++;
                    minU = Mathf.Min(minU, uv.x);
                    maxU = Mathf.Max(maxU, uv.x);
                    minV = Mathf.Min(minV, uv.y);
                    maxV = Mathf.Max(maxV, uv.y);
                }
                if (invalid == 0 && extreme == 0 &&
                    (maxU - minU > 1e-4f || maxV - minV > 1e-4f))
                    candidates.Add(channel.ToString());
            }
            return candidates.Count == 0 ? "(none with finite, bounded and variable UVs)" :
                string.Join(", ", candidates.ToArray());
        }

        private static string PointerCandidate(long candidate, EmdGroup g, byte[] data)
        {
            if (candidate < 0 || candidate > data.LongLength - (long)g.UvStride * g.Vertices.Length)
                return "outside file/mesh-span (" + candidate + ")";
            long delta = candidate - g.UvStartOffset;
            string label = "0x" + candidate.ToString("X") + " (delta=" + delta + ")";
            return label;
        }

        private static string VertexColorByteOrder(byte[] data, EmdGroup group)
        {
            // Existing EMD layouts place colors before UVs for 8-16-byte
            // records, or after three packed pairs for 20-28-byte records.
            int colorOffset = group.UvStartOffset + (group.UvStride > 16 ? 12 : 0);
            if (colorOffset < 0 || colorOffset > data.Length - 4)
                return "color bytes outside file";
            byte a = data[colorOffset], b = data[colorOffset + 1];
            byte c = data[colorOffset + 2], d = data[colorOffset + 3];
            return "raw=" + a.ToString("X2") + " " + b.ToString("X2") +
                " " + c.ToString("X2") + " " + d.ToString("X2") +
                " | RGBA=(" + a + "," + b + "," + c + "," + d + ")" +
                " | legacy ARGB=(" + b + "," + c + "," + d + "," + a + ")";
        }

        private static string HexAt(byte[] data, int offset, int maxBytes)
        {
            if (offset < 0 || offset >= data.Length) return "(out of bounds)";
            int count = Math.Min(maxBytes, data.Length - offset);
            var builder = new StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append(data[offset + i].ToString("X2"));
            }
            return builder.ToString();
        }

        private static bool HasUnusableDefaultUv(EmdGroup group)
        {
            if (group.UV == null || group.UV.Length == 0) return true;
            foreach (Vector2 value in group.UV)
                if (float.IsNaN(value.x) || float.IsNaN(value.y) ||
                    float.IsInfinity(value.x) || float.IsInfinity(value.y) ||
                    Mathf.Abs(value.x) > 1024f || Mathf.Abs(value.y) > 1024f)
                    return true;
            return false;
        }

        private static Dictionary<string, Mesh> ImportedMeshes(GameObject root)
        {
            var result = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            if (root == null) return result;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && !result.ContainsKey(filter.name))
                    result.Add(filter.name, filter.sharedMesh);
            foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.sharedMesh != null && !result.ContainsKey(skin.name))
                    result.Add(skin.name, skin.sharedMesh);
            return result;
        }

        private static EmdDocument AttemptParse(byte[] bytes, bool keepZeroWords,
            bool align16, bool rgbaLast, StringBuilder report, string name)
        {
            try
            {
                EmdDocument doc = EmdParser.Parse(bytes, true, keepZeroWords, align16, rgbaLast);
                report.AppendLine(name + ": " + doc.Groups.Count +
                    " mesh groups; skipped polygon records=" + doc.SkippedPolygonCount);
                return doc;
            }
            catch (Exception e)
            {
                report.AppendLine(name + ": PARSE FAILED " + e.GetType().Name +
                    ": " + e.Message);
                return null;
            }
        }

        internal static string BuildReport(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".emd", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
                throw new InvalidDataException("Select an EMD (.emd) Project asset or its prefab instance.");

            var report = new StringBuilder();
            report.AppendLine("EMD2Unity UV Mapping Audit (read-only)");
            report.AppendLine("EMD: " + path);
            var importer = AssetImporter.GetAtPath(path) as EmdImporter;
            bool preserveZero = false;
            bool align16 = false;
            bool rgbaLast = false;
            if (importer != null)
            {
                SerializedObject so = new SerializedObject(importer);
                SerializedProperty preserve = so.FindProperty("preserveZeroUvWords");
                if (preserve != null) preserveZero = preserve.boolValue;
                SerializedProperty alignment = so.FindProperty("alignUvBlocksTo16");
                if (alignment != null) align16 = alignment.boolValue;
                SerializedProperty vertexColorMode = so.FindProperty("vertexColorsRgbaLast");
                if (vertexColorMode != null) rgbaLast = vertexColorMode.boolValue;
            }
            report.AppendLine("Current Preserve Zero UV Words: " + preserveZero);
            report.AppendLine("Current Align UV Blocks to 16 Bytes: " + align16);
            report.AppendLine("Current Stage Vertex Colors Are RGBA: " + rgbaLast);
            report.AppendLine("UV source pairs are decoded as big-endian half-floats. " +
                "Default UV0 uses source channel 0 (strides 8..16) or " +
                "source channel 3 (strides 20..28).");
            byte[] bytes = File.ReadAllBytes(path);
            EmdDocument legacy = AttemptParse(bytes, false, false, rgbaLast, report, "Legacy skip-zero alignment");
            EmdDocument contiguous = AttemptParse(bytes, true, false, rgbaLast, report, "Preserve-zero alignment");
            EmdDocument aligned = AttemptParse(bytes, false, true, rgbaLast, report, "16-byte aligned UV blocks");
            EmdDocument chosen = align16 ? aligned : (preserveZero ? contiguous : legacy);
            EmdDocument alternate = preserveZero ? legacy : contiguous;
            if (chosen == null)
            {
                report.AppendLine("CURRENT PARSE FAILED. No UV conclusions possible.");
                return report.ToString();
            }
            var meshes = ImportedMeshes(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            report.AppendLine("Imported mesh objects found: " + meshes.Count);
            report.AppendLine("Original EMD file UV base offset: 0x" +
                chosen.FileUvOffset.ToString("X"));
            report.AppendLine("UV group metadata +0x34 is labelled ActualUVStart " +
                "in the reference MaxScript, but its pointer base/meaning is " +
                "not established. The candidates below must be verified before use.");
            int groups = Math.Min(chosen.Groups.Count, 300);
            for (int i = 0; i < groups; i++)
            {
                EmdGroup g = chosen.Groups[i];
                string meshName = g.Name + "_" + i;
                int primary = g.UvStride <= 16 ? 0 : 3;
                report.AppendLine("");
                report.AppendLine("MESH " + i + ": " + meshName +
                    " | MaterialIndex=" + g.MaterialIndex +
                    " | vertices=" + (g.Vertices == null ? 0 : g.Vertices.Length));
                report.AppendLine("  UV stride=" + g.UvStride + " bytes | source UV sets=" +
                    (g.SourceUvChannels == null ? 0 : g.SourceUvChannels.Length) +
                    " | default source channel=" + primary);
                report.AppendLine("  UV block start=0x" + g.UvStartOffset.ToString("X") +
                    (align16 ? " | 16-byte padding AFTER this block=" :
                        " | zero bytes skipped AFTER this block=") +
                    g.UvSkippedZeroBytes);
                report.AppendLine("  Group descriptor start: 0x" +
                    g.UvGroupHeaderOffset.ToString("X") +
                    " | raw UV pointer [+0x34]: 0x" +
                    unchecked((uint)g.UvHeaderPointerRaw).ToString("X8") +
                    " | secondary [+0x38]: 0x" +
                    unchecked((uint)g.UvHeaderSecondaryRaw).ToString("X8"));
                long pointerAbsolute = (long)g.UvHeaderPointerRaw;
                long pointerGroupRelative = (long)g.UvGroupHeaderOffset + g.UvHeaderPointerRaw;
                long pointerUvBaseRelative = (long)chosen.FileUvOffset + g.UvHeaderPointerRaw;
                report.AppendLine("  Raw pointer hypotheses: absolute=" +
                    PointerCandidate(pointerAbsolute, g, bytes) +
                    ", group-relative=" + PointerCandidate(pointerGroupRelative, g, bytes) +
                    ", file-UV-relative=" + PointerCandidate(pointerUvBaseRelative, g, bytes));
                report.AppendLine("  Sequential UV block first 24 bytes: " +
                    HexAt(bytes, g.UvStartOffset, 24));
                report.AppendLine("  Vertex color byte orders: " +
                    VertexColorByteOrder(bytes, g));
                if (g.UvSkippedZeroBytes > 0 && !align16)
                    report.AppendLine("  CHECK: zero words skipped between UV blocks; " +
                        "could be genuine padding OR valid leading coordinates.");
                if (aligned != null && i < aligned.Groups.Count)
                {
                    EmdGroup fixedGroup = aligned.Groups[i];
                    if (fixedGroup.Name == g.Name)
                    {
                        report.AppendLine("  Align-to-16 candidate: UV start=0x" +
                            fixedGroup.UvStartOffset.ToString("X") +
                            " | delta=" + (fixedGroup.UvStartOffset - g.UvStartOffset) +
                            " bytes | next-block padding=" +
                            fixedGroup.UvSkippedZeroBytes + " bytes");
                        report.AppendLine("  Align-to-16 candidate UV0: " +
                            Statistics(fixedGroup.UV));
                        report.AppendLine("  Align-to-16 first vertex colour: " +
                            (fixedGroup.Colors != null && fixedGroup.Colors.Length > 0 ?
                                fixedGroup.Colors[0].ToString() : "(none)"));
                    }
                    else
                        report.AppendLine("  Align-to-16 candidate: mesh order differs; comparison unavailable.");
                }
                if (alternate != null && i < alternate.Groups.Count)
                {
                    EmdGroup other = alternate.Groups[i];
                    if (g.Name == other.Name)
                        report.AppendLine("  Other alignment: UV start=0x" +
                            other.UvStartOffset.ToString("X") +
                            " | delta=" + (other.UvStartOffset - g.UvStartOffset) +
                            " bytes | stride=" + other.UvStride);
                    else
                        report.AppendLine("  Other alignment: group names differ; no comparison.");
                }
                if (g.SourceUvChannels != null)
                    for (int j = 0; j < g.SourceUvChannels.Length; j++)
                        report.AppendLine("  Source UV " + j +
                            (j == primary ? " [legacy default]" : "") +
                            ": " + Statistics(g.SourceUvChannels[j]));
                if (HasUnusableDefaultUv(g))
                {
                    report.AppendLine("  UV0 DATA WARNING: default source channel contains " +
                        "nonfinite or extreme (>1024) coordinates. This is not a " +
                        "texture-binding or shader proof.");
                    report.AppendLine("  Channels worth visual A/B testing: " + CandidateChannels(g));
                    report.AppendLine("  First source UV record (hex, bounded): " +
                        HexAt(bytes, g.UvStartOffset, Math.Min(48, g.UvStride * 2)));
                    report.AppendLine("  An invalid UV0 may also mean a bad stream offset/vertex " +
                        "record layout; do NOT permanently remap solely from this report.");
                }
                Mesh unityMesh;
                if (meshes.TryGetValue(meshName, out unityMesh))
                    report.AppendLine("  Imported Unity UV0: " + Statistics(unityMesh.uv));
                else
                    report.AppendLine("  Imported Unity mesh not found; no renderer UV comparison.");
            }
            if (chosen.Groups.Count > groups)
                report.AppendLine("Limited report to first " + groups +
                    " groups of " + chosen.Groups.Count + ".");
            report.AppendLine("");
            report.AppendLine("IMPORTANT: 16-byte alignment is a candidate layout " +
                "supported by stage EMD hex evidence, not a verified universal EMD rule. " +
                "Check visual UVs and vertex colours for affected meshes. The RGBA " +
                "color mode changes vertex colors only and is opt-in.");
            report.AppendLine("When all sampled vertex color bytes end with FF and " +
                "the first byte varies, RGBA ordering may be correct. Check " +
                "the material shader and original PS3 rendering before " +
                "enabling it globally.");
            report.AppendLine("If legacy and preserve-zero modes have the same UV starts " +
                "for every mesh, changing Preserve Zero UV Words cannot fix those meshes. " +
                "Skipped zeros after the final group do not affect any reported group.");
            report.AppendLine("Interpretation: source UV channels and offset differences are " +
                "diagnostic evidence only. Tiled UVs outside 0..1 are normal in games. " +
                "Compare a visibly good mesh with a distorted mesh and the original game. " +
                "Do not globally switch UV alignment or source channels without testing.");
            return report.ToString();
        }
    }
}
#endif
