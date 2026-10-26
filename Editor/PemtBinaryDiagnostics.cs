// Non-destructive binary triage for rejected PEMT samples.
// Does not alter PemtReader, motion export, or Animation Preview.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public static class PemtBinaryDiagnostics
    {
        private const string Command = "Tools/EMD2Unity/Diagnose Selected PEMT Binary";

        private static uint ReadBE(byte[] bytes, int pos)
        {
            if (pos < 0 || pos > bytes.Length - 4)
                throw new InvalidDataException("No 32-bit word at 0x" + pos.ToString("X"));
            return ((uint)bytes[pos] << 24) | ((uint)bytes[pos + 1] << 16) |
                ((uint)bytes[pos + 2] << 8) | bytes[pos + 3];
        }

        private static string H(uint value)
        {
            return "0x" + value.ToString("X8");
        }

        private static string H(int value)
        {
            return "0x" + value.ToString("X");
        }

        private static void DescribeBlock(StringBuilder b, string label, byte[] data, uint offset)
        {
            b.AppendLine(label + " at " + H(offset) + ":");
            if (offset > data.Length - 16)
            {
                b.AppendLine("  Outside physical file or truncated header; cannot inspect.");
                return;
            }
            int at = (int)offset;
            uint trackCount = ReadBE(data, at);
            uint trackTable = ReadBE(data, at + 4);
            uint unknown = ReadBE(data, at + 8);
            uint durationBits = ReadBE(data, at + 12);
            float duration = BitConverter.ToSingle(BitConverter.GetBytes(durationBits), 0);
            b.AppendLine("  trackCount=" + trackCount + ", trackTable=" + H(trackTable) +
                ", word08=" + H(unknown) + ", duration=" + duration.ToString("R") +
                " seconds (" + H(durationBits) + " raw)");
            long descriptorEnd = (long)offset + trackTable + (long)trackCount * 32L;
            b.AppendLine("  estimatedDescriptorEnd=" + H((uint)Math.Min(uint.MaxValue, descriptorEnd)) +
                ", withinPhysicalFile=" + (descriptorEnd <= data.Length &&
                descriptorEnd >= offset && trackCount <= 50000 && trackTable == 16));
        }

        internal static string Analyze(string sourcePath, byte[] data)
        {
            var b = new StringBuilder();
            b.AppendLine("EMD2Unity PEMT Binary Diagnostics v0.4.5 (read-only)");
            b.AppendLine("Asset: " + sourcePath);
            b.AppendLine("Actual bytes: " + data.Length + " (" + H(data.Length) + ")");
            if (data.Length < 16)
            {
                b.AppendLine("Error: less than 16 bytes; no PEMT header.");
                return b.ToString();
            }
            uint w0 = ReadBE(data, 0);
            uint count = ReadBE(data, 4);
            uint tableStart = ReadBE(data, 8);
            uint declared = ReadBE(data, 12);
            b.AppendLine("Header BE words: " + H(w0) + " / " + H(count) +
                " / " + H(tableStart) + " / " + H(declared));
            b.AppendLine("Word0 recognized 0x400: " + (w0 == 0x400));
            b.AppendLine("Declared - actual: " + ((long)declared - data.Length) + " bytes");
            b.AppendLine("Declared is 16-byte aligned: " + ((declared & 15) == 0));
            b.AppendLine("Actual is 16-byte aligned: " + ((data.Length & 15) == 0));

            long end = (long)tableStart + 4L * count;
            b.AppendLine("Pointer table range: " + H(tableStart) + " to " +
                H((uint)Math.Min(uint.MaxValue, end)) +
                " (withinPhysicalFile=" + (end <= data.Length) + ")");
            if (count == 0 || count > 65536 || tableStart < 16 || end > data.Length)
            {
                b.AppendLine("Cannot safely enumerate pointer entries with this layout.");
                return b.ToString();
            }

            int empty = 0, outOfBounds = 0, descending = 0, nonAligned = 0, beforeTable = 0;
            int firstId = -1, lastId = -1;
            uint firstOff = 0, lastOff = 0, greatest = 0, previous = 0;
            for (int id = 0; id < (int)count; ++id)
            {
                uint off = ReadBE(data, (int)tableStart + 4 * id);
                if (off == 0) { empty++; continue; }
                if (firstId < 0) { firstId = id; firstOff = off; }
                lastId = id;
                lastOff = off;
                if (off > greatest) greatest = off;
                if (off >= data.Length) outOfBounds++;
                if (off < end) beforeTable++;
                if ((off & 15) != 0) nonAligned++;
                if (previous != 0 && off <= previous) descending++;
                previous = off;
            }
            b.AppendLine("Nonzero entries: " + (count - empty) + "; zeros: " + empty);
            b.AppendLine("First nonzero: ID " + firstId + " offset " + H(firstOff));
            b.AppendLine("Last nonzero: ID " + lastId + " offset " + H(lastOff));
            b.AppendLine("Largest pointer: " + H(greatest));
            b.AppendLine("Pointer anomalies: beyond physical EOF=" + outOfBounds +
                ", before table end=" + beforeTable +
                ", non-16-aligned=" + nonAligned +
                ", non-monotonic=" + descending);
            b.AppendLine("Selected table entries (ID: offset):");
            for (int i = 0; i < (int)count; i++)
            {
                if (i >= 4 && i < count - 4 && i != firstId && i != lastId)
                    continue;
                uint off = ReadBE(data, (int)tableStart + i * 4);
                b.AppendLine("  " + i + ": " + H(off));
            }
            if (firstId >= 0) DescribeBlock(b, "First motion header", data, firstOff);
            if (lastId >= 0 && lastId != firstId)
                DescribeBlock(b, "Last motion header", data, lastOff);
            b.AppendLine("Note: This report cannot establish compression, truncation or motion names.");
            return b.ToString();
        }

        [MenuItem(Command)]
        private static void DiagnoseSelected()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".pemt", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("PEMT diagnostics",
                    "Select an extracted .pemt asset in the Project window first.", "OK");
                return;
            }
            try
            {
                string report = Analyze(assetPath, File.ReadAllBytes(assetPath));
                EditorGUIUtility.systemCopyBuffer = report;
                Debug.Log(report);
                EditorUtility.DisplayDialog("PEMT diagnostics",
                    "Read-only report copied to clipboard and Unity Console. Paste it into chat for analysis.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("EMD2Unity PEMT diagnostics: " + ex);
            }
        }

        [MenuItem(Command, true)]
        private static bool Validate()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) &&
                path.EndsWith(".pemt", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
