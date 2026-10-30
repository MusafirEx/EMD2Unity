// Read-only byte-level audit for EMD weapon position-only exports.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class EmdWeaponRoundTripAuditWindow : EditorWindow
    {
        private GameObject originalAsset;
        private GameObject exportedAsset;
        private string report = "Assign the original _Wxx.emd asset and a newly imported patched .emd asset.";
        private Vector2 reportScroll;

        [MenuItem("Tools/EMD2Unity/Weapon EMD Round-Trip Audit (Experimental)")]
        public static void Open() =>
            GetWindow<EmdWeaponRoundTripAuditWindow>("Weapon EMD Audit");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Weapon EMD Binary Round-Trip Audit", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Read-only audit. Verifies both EMDs use the same byte layout and counts " +
                "every changed byte. Only changes inside known XYZ float32 vertex regions " +
                "are allowed. Bones, weights, UV, colors, triangles, materials, headers " +
                "and unknown bytes must remain identical.",
                MessageType.Info);
            originalAsset = (GameObject)EditorGUILayout.ObjectField(
                "Original Weapon .emd", originalAsset, typeof(GameObject), false);
            exportedAsset = (GameObject)EditorGUILayout.ObjectField(
                "Patched Weapon .emd", exportedAsset, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(originalAsset == null || exportedAsset == null))
                if (GUILayout.Button("Compare Binary EMD Files"))
                    RunAudit();
            if (GUILayout.Button("Copy Audit Report"))
                EditorGUIUtility.systemCopyBuffer = report;
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private sealed class Span
        {
            public string name;
            public int offset;
            public int vertices;
            public int material;
            public int triangles;
        }

        private static uint U32(byte[] b, int o)
        {
            if (o < 0 || o > b.Length - 4) throw new InvalidDataException("Out-of-bounds word at 0x" + o.ToString("X"));
            return ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) |
                   ((uint)b[o + 2] << 8) | b[o + 3];
        }
        private static ushort U16(byte[] b, int o)
        {
            if (o < 0 || o > b.Length - 2) throw new InvalidDataException("Out-of-bounds halfword at 0x" + o.ToString("X"));
            return (ushort)((b[o] << 8) | b[o + 1]);
        }
        private static string Name(byte[] b, int start, int length)
        {
            if (start < 0 || start > b.Length - length)
                throw new InvalidDataException("Name out of bounds");
            int end = start;
            while (end < start + length && b[end] != 0) end++;
            return Encoding.ASCII.GetString(b, start, end - start);
        }
        private static string AssetPath(GameObject asset, bool requireOriginalWeaponName)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".emd", StringComparison.OrdinalIgnoreCase) ||
                !(AssetImporter.GetAtPath(path) is EmdImporter))
                throw new InvalidDataException("Select an imported EMD model asset in Project (not a scene object).");
            if (requireOriginalWeaponName &&
                !Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"_W[0-9]{2,}$",
                    RegexOptions.IgnoreCase))
                throw new InvalidDataException("Original asset filename must end in _Wxx.emd.");
            return path;
        }
        private static List<Span> ExtractSpans(byte[] data)
        {
            if (data.Length < 0xB0)
                throw new InvalidDataException("EMD is too short.");
            int count = checked((int)U32(data, 0x24));
            int cursor = checked((int)U32(data, 0x4C));
            int uvOffset = checked((int)U32(data, 0x5C));
            if (count < 1 || count > 256 || cursor < 0xB0 ||
                uvOffset <= cursor || uvOffset > data.Length)
                throw new InvalidDataException("Unsupported polygon count or section offsets.");
            var spans = new List<Span>();
            for (int i = 0; i < count; ++i)
            {
                int size = checked((int)U32(data, cursor + 4));
                long end = (long)cursor + size;
                if (size < 0x1D0 || end > uvOffset)
                    throw new InvalidDataException("Invalid polygon length #" + i);
                string name = Name(data, cursor + 0x10, 32);
                int mat = U16(data, cursor + 0x130);
                int type = U16(data, cursor + 0x132);
                if (type != 4)
                    throw new InvalidDataException(name + ": unsupported polygon type 0x" + type.ToString("X"));
                int group = cursor + 0x140;
                while (group + 0x90 <= end && U32(data, group) == 0)
                    group += 4;
                if (group + 0x90 > end)
                    throw new InvalidDataException(name + ": missing mesh descriptor");
                int pointer = checked((int)U32(data, group + 8));
                int position = checked(group + pointer);
                int countVerts = U16(data, group + 0x88);
                int indices = U16(data, group + 0x8A);
                int positionBufferBytes = U16(data, group + 0x10);
                if (countVerts < 1 || indices < 3 || indices % 3 != 0 ||
                    (long)countVerts * 12 > positionBufferBytes ||
                    position < group + 0x90 || (long)position + (long)countVerts * 12 > end)
                    throw new InvalidDataException(name + ": unsupported vertex stream");
                spans.Add(new Span
                {
                    name = name + "_" + i,
                    offset = position,
                    vertices = countVerts,
                    material = mat,
                    triangles = indices / 3
                });
                cursor = checked((int)end);
            }
            if (cursor != uvOffset)
                throw new InvalidDataException("Polygon section not contiguous with UV section.");
            return spans;
        }
        private static float BeF32(byte[] b, int off)
        {
            uint word = U32(b, off);
            return BitConverter.ToSingle(BitConverter.GetBytes(word), 0);
        }
        private static string SHA(byte[] b)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }
        private static void VerifySpans(List<Span> original, List<Span> exported)
        {
            if (original.Count != exported.Count)
                throw new InvalidDataException("Mesh count differs.");
            for (int i = 0; i < original.Count; ++i)
            {
                Span a = original[i], b = exported[i];
                if (a.name != b.name || a.offset != b.offset ||
                    a.vertices != b.vertices || a.material != b.material ||
                    a.triangles != b.triangles)
                    throw new InvalidDataException("Mesh layout differs at index " + i +
                        " (" + a.name + ").");
            }
        }
        private void RunAudit()
        {
            try
            {
                string aPath = AssetPath(originalAsset, true);
                string bPath = AssetPath(exportedAsset, false);
                if (string.Equals(Path.GetFullPath(aPath), Path.GetFullPath(bPath),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Original and patched assets must be two different files.");
                byte[] a = File.ReadAllBytes(aPath);
                byte[] b = File.ReadAllBytes(bPath);
                if (a.Length != b.Length)
                    throw new InvalidDataException(
                        "File sizes differ: original=" + a.Length + ", patched=" + b.Length);
                List<Span> spans = ExtractSpans(a);
                VerifySpans(spans, ExtractSpans(b));

                var mask = new bool[a.Length];
                int totalVerts = 0;
                foreach (Span span in spans)
                {
                    totalVerts += span.vertices;
                    for (int i = 0; i < span.vertices * 12; ++i)
                    {
                        int offset = span.offset + i;
                        if (mask[offset])
                            throw new InvalidDataException("Overlapping vertex ranges at " + span.name);
                        mask[offset] = true;
                    }
                }
                int changedBytes = 0, changedNonPositionBytes = 0;
                int firstUnexpectedOffset = -1;
                for (int i = 0; i < a.Length; ++i)
                {
                    if (a[i] == b[i]) continue;
                    changedBytes++;
                    if (mask[i]) continue;
                    changedNonPositionBytes++;
                    if (firstUnexpectedOffset == -1) firstUnexpectedOffset = i;
                }
                var sb = new StringBuilder();
                sb.AppendLine("EMD2Unity WEAPON ROUND-TRIP AUDIT");
                sb.AppendLine("Original: " + aPath);
                sb.AppendLine("Patched:  " + bPath);
                sb.AppendLine("Size: " + a.Length + " bytes (identical lengths)");
                sb.AppendLine("Original SHA256: " + SHA(a));
                sb.AppendLine("Patched  SHA256: " + SHA(b));
                sb.AppendLine("Polygon mesh groups: " + spans.Count + ", vertices: " + totalVerts);
                sb.AppendLine("Changed bytes: " + changedBytes);
                sb.AppendLine("Unexpected non-position bytes changed: " + changedNonPositionBytes);
                if (firstUnexpectedOffset >= 0)
                    sb.AppendLine("First unexpected byte offset: 0x" +
                        firstUnexpectedOffset.ToString("X") + " (old 0x" +
                        a[firstUnexpectedOffset].ToString("X2") + ", new 0x" +
                        b[firstUnexpectedOffset].ToString("X2") + ")");
                sb.AppendLine();
                int changedVertices = 0;
                foreach (Span span in spans)
                {
                    int changed = 0;
                    float maxDistance = 0f;
                    for (int v = 0; v < span.vertices; ++v)
                    {
                        int o = span.offset + v * 12;
                        bool differs = false;
                        for (int k = 0; k < 12; ++k)
                            if (a[o + k] != b[o + k]) { differs = true; break; }
                        Vector3 pa = new Vector3(BeF32(a, o), BeF32(a, o + 4), BeF32(a, o + 8));
                        Vector3 pb = new Vector3(BeF32(b, o), BeF32(b, o + 4), BeF32(b, o + 8));
                        if ((float.IsNaN(pa.x) || float.IsInfinity(pa.x)) || (float.IsNaN(pa.y) || float.IsInfinity(pa.y)) || (float.IsNaN(pa.z) || float.IsInfinity(pa.z)) ||
                            (float.IsNaN(pb.x) || float.IsInfinity(pb.x)) || (float.IsNaN(pb.y) || float.IsInfinity(pb.y)) || (float.IsNaN(pb.z) || float.IsInfinity(pb.z)))
                            throw new InvalidDataException(span.name + ": nonfinite vertex at index " + v);
                        if (!differs) continue;
                        changed++;
                        float distance = Vector3.Distance(pa, pb);
                        if (distance > maxDistance) maxDistance = distance;
                    }
                    changedVertices += changed;
                    sb.AppendLine(span.name + ": " + changed + "/" + span.vertices +
                        " vertices changed; max delta " + maxDistance.ToString("G9") +
                        "; " + span.triangles + " triangles; material " + span.material);
                }
                sb.AppendLine();
                sb.AppendLine("Changed vertices overall: " + changedVertices + "/" + totalVerts);
                if (changedNonPositionBytes > 0)
                    sb.AppendLine("RESULT: FAIL — non-position data differs. Do not use this EMD in game.");
                else if (changedBytes == 0)
                    sb.AppendLine("RESULT: PASS — byte-identical no-change round-trip.");
                else
                    sb.AppendLine("RESULT: PASS — XYZ position-only changes; all other bytes identical.");
                report = sb.ToString();
                Debug.Log(report);
            }
            catch (Exception e)
            {
                report = "EMD weapon round-trip audit FAILED: " + e.Message;
                Debug.LogError(report);
            }
        }
    }
}
#endif
