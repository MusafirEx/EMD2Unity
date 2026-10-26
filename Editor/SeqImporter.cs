// Read-only SEQ binary structural importer based on the 12 user-supplied
// Sousei v1.02 scripts (setup, anm00, cpu, assist).
// Does NOT claim to interpret gameplay VM opcodes, event timing or Motion IDs.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [ScriptedImporter(1, "seq")]
    public sealed class SeqImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            SeqScriptLibrary result;
            try
            {
                result = SeqBinaryReader.Read(File.ReadAllBytes(ctx.assetPath));
            }
            catch (Exception ex)
            {
                result = ScriptableObject.CreateInstance<SeqScriptLibrary>();
                result.importError = ex.Message;
                ctx.LogImportError("EMD2Unity SEQ: " + ex.Message);
            }
            result.name = Path.GetFileNameWithoutExtension(ctx.assetPath) + " [SEQ Script]";
            ctx.AddObjectToAsset("seq", result);
            ctx.SetMainObject(result);
        }
    }

    internal static class SeqBinaryReader
    {
        private const int MaxFileSize = 128 * 1024 * 1024;
        private const int MaxStrings = 10000;
        private const int MaxRefsPerString = 512;
        private const int ScriptStart = 0x10;

        private static uint Be32(byte[] b, int offset)
        {
            if (offset < 0 || offset > b.Length - 4)
                throw new InvalidDataException("SEQ: invalid word at 0x" + offset.ToString("X"));
            return ((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) |
                   ((uint)b[offset + 2] << 8) | b[offset + 3];
        }

        private static bool Printable(byte b) { return b >= 0x20 && b <= 0x7E; }

        private static int CountRawMarker(byte[] b, byte a, byte c, byte d, byte e)
        {
            int count = 0;
            for (int i = 0; i <= b.Length - 4; ++i)
                if (b[i] == a && b[i + 1] == c && b[i + 2] == d && b[i + 3] == e)
                    ++count;
            return count;
        }

        private static string Category(string resource)
        {
            string s = resource.ToLowerInvariant();
            if (s.Contains("/motion/")) return "motion-container";
            if (s.Contains("/combo/")) return "combo-container";
            if (s.Contains("/seq/")) return "sequence-script";
            if (s.Contains("/model/")) return "model";
            if (s.Contains("/camera/")) return "camera";
            if (s.Contains(".efp") || s.Contains(".eff")) return "effect";
            if (s.Contains(".rssx") || s.StartsWith("drama/")) return "cutin-drama";
            if (s.StartsWith("shot/")) return "projectile";
            if (s.Contains("/")) return "other-resource";
            return "literal/part-name";
        }

        private static string PreviewHex(byte[] b)
        {
            var builder = new StringBuilder();
            int limit = Math.Min(b.Length, 128);
            for (int i = 0; i < limit; i += 16)
            {
                builder.Append(i.ToString("X6")).Append("  ");
                for (int j = i; j < Math.Min(i + 16, limit); j++)
                    builder.Append(b[j].ToString("X2")).Append(' ');
                builder.AppendLine();
            }
            return builder.ToString().TrimEnd();
        }

        internal static SeqScriptLibrary Read(byte[] b)
        {
            if (b == null || b.Length < ScriptStart || b.Length > MaxFileSize)
                throw new InvalidDataException("SEQ: file must contain a 16-byte header (max 128 MiB).");

            var result = ScriptableObject.CreateInstance<SeqScriptLibrary>();
            try
            {
                result.sourceByteCount = b.Length;
                result.word0 = "0x" + Be32(b, 0).ToString("X8");
                result.word1 = "0x" + Be32(b, 4).ToString("X8");
                result.word2 = "0x" + Be32(b, 8).ToString("X8");
                result.word3 = "0x" + Be32(b, 12).ToString("X8");
                result.marker12RawCount = CountRawMarker(b, 0x12, 0, 0, 0);
                result.marker16RawCount = CountRawMarker(b, 0x16, 0, 0, 0);
                result.marker2013RawCount = CountRawMarker(b, 0x20, 0x13, 0, 0);
                result.firstBytesHex = PreviewHex(b);

                // The source samples store printable, null-terminated strings
                // within code/data. Reproduce a greedy [ASCII 0x20-0x7E]{5,}\0
                // scan. This is a discovery heuristic, not a verified string table.
                for (int p = ScriptStart; p < b.Length; )
                {
                    if (!Printable(b[p])) { p++; continue; }
                    int start = p;
                    while (p < b.Length && Printable(b[p])) p++;
                    if (p < b.Length && b[p] == 0 && p - start >= 5)
                    {
                        if (result.strings.Count >= MaxStrings)
                            throw new InvalidDataException("SEQ: too many candidate ASCII strings.");
                        string value = Encoding.ASCII.GetString(b, start, p - start);
                        var entry = new SeqStringReference
                        {
                            byteOffset = start, value = value, category = Category(value)
                        };
                        // Candidate pointers must precede their target string.
                        // The observed 16 00 00 00 prefix is recorded separately;
                        // we do not assert instruction boundaries or VM meaning.
                        byte a = (byte)(start >> 24), c = (byte)(start >> 16);
                        byte d = (byte)(start >> 8), e = (byte)start;
                        for (int i = ScriptStart; i <= start - 4; ++i)
                        {
                            if (b[i] != a || b[i + 1] != c ||
                                b[i + 2] != d || b[i + 3] != e) continue;
                            if (entry.rawPointerOffsets.Count == MaxRefsPerString)
                            {
                                entry.referencesTruncated = true;
                                break;
                            }
                            entry.rawPointerOffsets.Add(i);
                            if (i >= 4 && b[i - 4] == 0x16 &&
                                b[i - 3] == 0 && b[i - 2] == 0 && b[i - 1] == 0)
                                entry.prefix16PointerOffsets.Add(i);
                        }
                        if (entry.rawPointerOffsets.Count > 0) result.referencedStringCount++;
                        if (entry.prefix16PointerOffsets.Count > 0) result.prefix16StringCount++;
                        result.strings.Add(entry);
                    }
                    if (p < b.Length) p++;
                }
                return result;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(result);
                throw;
            }
        }
    }
}
#endif
