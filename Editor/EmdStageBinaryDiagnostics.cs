// Stage EMD binary diagnostics. Read-only and intentionally separate from
// EmdParser, so verified Character EMD geometry and PEMT remain unchanged.
// Inspects polygon/group-0 buffer bounds and index ranges; does not invent
// vertices or infer an unsupported topology.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public static class EmdStageBinaryDiagnostics
    {
        private const string MenuPath = "Tools/EMD2Unity/Diagnose Selected Stage EMD";
        private const long MaxBytes = 512L * 1024 * 1024;

        private static ushort U16(byte[] b, long pos)
        {
            if (pos < 0 || pos > b.LongLength - 2)
                throw new InvalidDataException("U16 outside EMD at 0x" + pos.ToString("X"));
            int p = (int)pos;
            return (ushort)((b[p] << 8) | b[p + 1]);
        }

        private static uint U32(byte[] b, long pos)
        {
            if (pos < 0 || pos > b.LongLength - 4)
                throw new InvalidDataException("U32 outside EMD at 0x" + pos.ToString("X"));
            int p = (int)pos;
            return ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) |
                   ((uint)b[p + 2] << 8) | b[p + 3];
        }

        private static int I32(byte[] b, long pos) { return unchecked((int)U32(b, pos)); }

        private static string Hex(long x) { return "0x" + x.ToString("X"); }

        private static string Name(byte[] bytes, long offset)
        {
            var s = new StringBuilder();
            for (int i = 0; i < 32 && offset + i < bytes.LongLength; i++)
            {
                byte v = bytes[(int)(offset + i)];
                if (v == 0) break;
                s.Append(v >= 32 && v < 127 ? (char)v : '?');
            }
            return s.ToString();
        }

        private static string HexBytes(byte[] bytes, long offset, int count)
        {
            if (offset < 0 || offset >= bytes.LongLength) return "outside file";
            int available = (int)Math.Min(count, bytes.LongLength - offset);
            var s = new StringBuilder();
            for (int i = 0; i < available; i++)
            {
                if (i > 0) s.Append(' ');
                s.Append(bytes[(int)(offset + i)].ToString("X2"));
            }
            return s.ToString();
        }

        private static string FloatTriplet(byte[] b, long at)
        {
            if (at < 0 || at > b.LongLength - 12) return "outside file";
            var s = new StringBuilder();
            for (int k = 0; k < 3; k++)
            {
                uint bits = U32(b, at + k * 4);
                float value = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                if (k > 0) s.Append(", ");
                s.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                s.Append(" [0x").Append(bits.ToString("X8")).Append(']');
            }
            return s.ToString();
        }

        private static void IndexRange(byte[] data, long start, int count, int vertexCap,
            int pointCount, out int maxIndex, out int beyondChunk, out int beyondPoint)
        {
            maxIndex = -1; beyondChunk = 0; beyondPoint = 0;
            if (start < 0 || start + (long)count * 2L > data.Length) return;
            for (int i = 0; i < count; i++)
            {
                int value = U16(data, start + i * 2L);
                if (value > maxIndex) maxIndex = value;
                if (value >= vertexCap) beyondChunk++;
                if (value >= pointCount) beyondPoint++;
            }
        }

        internal static string Analyze(byte[] data, string asset)
        {
            var report = new StringBuilder();
            report.AppendLine("EMD2Unity Stage EMD Buffer Diagnostics v0.4.7 (read-only)");
            report.AppendLine("Asset: " + asset);
            report.AppendLine("Physical bytes: " + data.Length + " (" + Hex(data.Length) + ")");
            report.AppendLine("Note: group 0 only is decoded structurally; other groups need full chaining analysis.");
            if (data.Length < 0x68)
            {
                report.AppendLine("EMD header truncated.");
                return report.ToString();
            }
            int polygonCount = I32(data, 0x24);
            int polygonStart = I32(data, 0x4C);
            int uvStart = I32(data, 0x5C);
            report.AppendLine("Polygons=" + polygonCount + " polygonTable=" + Hex(polygonStart) +
                " firstUVHeader=" + Hex(uvStart));
            if (polygonCount < 0 || polygonCount > 10000 ||
                polygonStart < 0 || polygonStart >= data.Length)
            {
                report.AppendLine("Invalid polygon count/table; stopped.");
                return report.ToString();
            }

            long current = polygonStart;
            int mismatches = 0, overIndex = 0, zeroGroups = 0, parseErrors = 0;
            int detailed = 0;
            const int MaxDetailed = 80;
            for (int i = 0; i < polygonCount; i++)
            {
                long record = current;
                if (record < 0 || record > data.LongLength - 8)
                {
                    report.AppendLine("Polygon table ends prematurely at polygon #" + i);
                    parseErrors++;
                    break;
                }
                int recordBytes = I32(data, record + 4);
                if (recordBytes < 0x140 || record + recordBytes > data.LongLength)
                {
                    report.AppendLine("Polygon #" + i + ": invalid block size " + recordBytes +
                        " at " + Hex(record));
                    parseErrors++;
                    break;
                }
                current = record + recordBytes;
                try
                {
                    string name = Name(data, record + 0x10);
                    long header = record + 0x130;
                    ushort material = U16(data, header);
                    ushort format = U16(data, header + 2);
                    int groups = format == 0x204 ? U16(data, header + 0x10) : 1;
                    long groupStart = record + (format == 0x204 ? 0x160 : 0x140);
                    long basePos = groupStart;
                    int zeroWords = 0;
                    while (basePos <= current - 4 && U32(data, basePos) == 0 && zeroWords++ < 65536)
                        basePos += 4;
                    if (basePos + 0x90 > current)
                        throw new InvalidDataException("group-0 buffer header outside polygon record");
                    long indexStart = basePos + I32(data, basePos);
                    int indexBytes = U16(data, basePos + 4);
                    long vertexStart = basePos + I32(data, basePos + 8);
                    int vertexBytes = U16(data, basePos + 0x10);
                    int uvBytes = I32(data, basePos + 0x20);
                    int points = U16(data, basePos + 0x88);
                    int indexField = U16(data, basePos + 0x8A);
                    int capacity = vertexBytes / 12;
                    bool mismatch = points > capacity;
                    if (mismatch) mismatches++;
                    if (points == 0) zeroGroups++;

                    int n = Math.Min(indexField, indexBytes / 2);
                    int max, beyondChunk, beyondPoints;
                    IndexRange(data, indexStart, n, capacity, points,
                        out max, out beyondChunk, out beyondPoints);
                    if (beyondChunk > 0) overIndex++;

                    int faceCandidate = indexField <= 21845 ? indexField * 3 : 0;
                    bool faceCandidateFits = faceCandidate > 0 &&
                        faceCandidate <= indexBytes / 2;
                    int fMax = -1, fBeyond = 0, fOverPoint = 0;
                    if (faceCandidateFits)
                        IndexRange(data, indexStart, faceCandidate, capacity, points,
                            out fMax, out fBeyond, out fOverPoint);
                    bool wholePositionRangeInFile = vertexStart >= 0 &&
                        vertexStart + (long)points * 12 <= data.LongLength;

                    // Print all suspicious records, plus a few ordinary records.
                    bool print = mismatch || beyondChunk > 0 || i < 4;
                    if (print && detailed++ < MaxDetailed)
                    {
                        report.AppendLine("");
                        report.AppendLine("Polygon #" + i + " " + name + " @ " + Hex(record) +
                            " material=" + material + " headerType=" + Hex(format) +
                            " groups=" + groups);
                        report.AppendLine("  pointCount=" + points + " vertexBytes=" + vertexBytes +
                            " (capacity=" + capacity + " xyz float3)" +
                            " vertexStart=" + Hex(vertexStart));
                        report.AppendLine("  indexField=" + indexField + " indexBytes=" + indexBytes +
                            " indexStart=" + Hex(indexStart) + " UVBytes=" + uvBytes +
                            " UVBytes/point=" + (points > 0 ? (uvBytes / (float)points).ToString("F3") : "N/A"));
                        report.AppendLine("  Index probe count=" + n + " max=" + max +
                            " beyond16KCapacity=" + beyondChunk + " beyondPointCount=" + beyondPoints);
                        if (faceCandidateFits)
                            report.AppendLine("  Alternate faceCount*3 probe: count=" + faceCandidate +
                                " max=" + fMax + " beyondCapacity=" + fBeyond +
                                " beyondPointCount=" + fOverPoint);
                        report.AppendLine("  Full contiguous xyz bytes=" + (points * 12L) +
                            " in physical file=" + wholePositionRangeInFile);
                        if (vertexStart >= 0 && vertexStart < data.LongLength)
                        {
                            report.AppendLine("  XYZ first vertex: " + FloatTriplet(data, vertexStart));
                            report.AppendLine("  Bytes at reported vertex chunk end: " +
                                HexBytes(data, vertexStart + vertexBytes, 24));
                            report.AppendLine("  XYZ following reported chunk: " +
                                FloatTriplet(data, vertexStart + vertexBytes));
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is OverflowException)
                {
                    parseErrors++;
                    if (detailed++ < MaxDetailed)
                        report.AppendLine("Polygon #" + i + " @" + Hex(record) +
                            " cannot inspect first group: " + ex.Message);
                }
            }
            report.AppendLine("");
            report.AppendLine("SUMMARY: polygons=" + polygonCount +
                ", insufficient declared position chunks=" + mismatches +
                ", records with indices beyond position chunk=" + overIndex +
                ", zero-vertex groups=" + zeroGroups +
                ", inspect errors=" + parseErrors + ".");
            report.AppendLine("This tool does not reconstruct mesh geometry. " +
                "Full stage-vertex layout remains unverified; inspect raw index values " +
                "and bytes after the 0x4000 boundary before changing the importer.");
            return report.ToString();
        }

        internal static void DiagnosePath(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("EMD Stage Diagnostics",
                    "Select an extracted .emd source file in the Project window.", "OK");
                return;
            }
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxBytes)
                    throw new InvalidDataException("EMD source missing or larger than diagnostic limit (512 MiB).");
                string report = Analyze(File.ReadAllBytes(path), path);
                EditorGUIUtility.systemCopyBuffer = report;
                Debug.Log(report);
                EditorUtility.DisplayDialog("EMD Stage Diagnostics",
                    "The read-only buffer report was copied to clipboard and Console. " +
                    "Paste it in the chat to investigate the actual stage layout.", "OK");
            }
            catch (Exception ex) { Debug.LogError("EMD Stage Diagnostics: " + ex); }
        }

        [MenuItem(MenuPath)]
        private static void Selected()
        {
            DiagnosePath(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) &&
                path.EndsWith(".emd", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
