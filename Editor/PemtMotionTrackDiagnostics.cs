// PEMT Motion ID / track investigation. Read-only: marker semantics are not inferred.
// An unsupported marker is displayed with exact source offsets and bounded raw bytes.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal static class PemtMotionTrackDiagnostics
    {
        internal static bool IsKnownTransform(string marker)
        {
            return string.Equals(marker, "0x02040021", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(marker, "0x02040028", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsKnownTimeline(string marker)
        {
            return string.Equals(marker, "0x00000040", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsUnsupported(string marker)
        {
            return !IsKnownTransform(marker) && !IsKnownTimeline(marker);
        }

        internal static string TrackSummary(PemtMotionMetadata motion, PemtTrackMetadata track)
        {
            if (track == null) return "null track";
            uint info = 0;
            if (!string.IsNullOrEmpty(track.infoHex))
            {
                string hex = track.infoHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ?
                    track.infoHex.Substring(2) : track.infoHex;
                uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out info);
            }
            return "Motion " + (motion == null ? "unknown" : motion.motionIdHex) +
                " (ID " + (motion == null ? -1 : motion.motionId) + ")" +
                " / track #" + track.index +
                " / marker " + track.markerHex +
                " / info " + track.infoHex +
                " / info.high16(joint candidate) " + (info >> 16) +
                " / info.low16(count candidate) " + (info & 0xFFFF) +
                " / offsetA(block-relative) 0x" + track.dataStartRelative.ToString("X") +
                " / offsetB(block-relative) 0x" + track.dataEndRelative.ToString("X");
        }

        private static string SampleHex(byte[] data, long absolute, long blockStart,
            long blockEnd, int maxBytes)
        {
            if (absolute < blockStart || absolute >= blockEnd ||
                absolute < 0 || absolute >= data.LongLength)
                return "(outside motion block / no data)";
            int count = (int)Math.Min(maxBytes,
                Math.Min(blockEnd - absolute, data.LongLength - absolute));
            StringBuilder hex = new StringBuilder();
            for (int j = 0; j < count; j++)
            {
                if (j > 0) hex.Append(' ');
                hex.Append(data[(int)absolute + j].ToString("X2"));
            }
            return hex.ToString();
        }


        // Evidence-only candidate interpretation for an unknown channel.
        // Do not use this to classify 0x02040022 as scale by default.
        private static uint ReadBigEndian32(byte[] data, long at)
        {
            int i = checked((int)at);
            return ((uint)data[i] << 24) | ((uint)data[i + 1] << 16) |
                   ((uint)data[i + 2] << 8) | data[i + 3];
        }

        private static float CandidateHalf(byte[] data, long at)
        {
            int i = checked((int)at);
            uint value = (uint)((data[i] << 8) | data[i + 1]);
            uint sign = (value & 0x8000u) << 16;
            uint fraction = value & 0x3FFu;
            int exponent = (int)((value >> 10) & 31u);
            uint bits;
            if (exponent == 0)
            {
                if (fraction == 0u) bits = sign;
                else
                {
                    int power = -14;
                    while ((fraction & 0x400u) == 0u)
                    {
                        fraction <<= 1;
                        power--;
                    }
                    bits = sign | ((uint)(power + 127) << 23) |
                           ((fraction & 0x3FFu) << 13);
                }
            }
            else if (exponent == 31)
                bits = sign | 0x7F800000u | (fraction << 13);
            else bits = sign | ((uint)(exponent - 15 + 127) << 23) |
                        (fraction << 13);
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        private static void AppendScaleCandidate(StringBuilder report, byte[] data,
            PemtTrackMetadata track, long blockStart, long blockEnd)
        {
            if (!string.Equals(track.markerHex, "0x02040022",
                StringComparison.OrdinalIgnoreCase)) return;
            report.AppendLine("  SCALE CANDIDATE ONLY (0x02040022; NOT VERIFIED):");
            if (string.IsNullOrEmpty(track.infoHex) || track.infoHex.Length < 3 ||
                !uint.TryParse(track.infoHex.Substring(2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out uint info))
            {
                report.AppendLine("    Cannot interpret key count from info.");
                return;
            }
            int count = (int)(info & 0xFFFFu);
            long timeStart = blockStart + track.dataStartRelative;
            long valueStart = blockStart + track.dataEndRelative;
            if (count < 1 || track.dataEndRelative <= 0 ||
                timeStart < blockStart || valueStart < blockStart ||
                timeStart + count * 4L > blockEnd ||
                valueStart + count * 8L > blockEnd)
            {
                report.AppendLine("    Offsets/count do not fit a 4-byte time + 8-byte value/key layout.");
                return;
            }
            for (int k = 0; k < Math.Min(count, 4); k++)
            {
                float time = BitConverter.ToSingle(
                    BitConverter.GetBytes(ReadBigEndian32(data, timeStart + k * 4L)), 0);
                long p = valueStart + k * 8L;
                float x = CandidateHalf(data, p);
                float y = CandidateHalf(data, p + 2);
                float z = CandidateHalf(data, p + 4);
                float padding = CandidateHalf(data, p + 6);
                report.AppendLine("    Key " + k + ": timeBEFloat=" + time +
                    " | candidate half XYZ=" + x + ", " + y + ", " + z +
                    " | fourthHalf=" + padding +
                    " | zeroPadding=" + (padding == 0f));
            }
            report.AppendLine("    Preview may TRY XYZ as local-scale multipliers by opt-in." +
                " This does not establish the true track semantics.");
        }

        internal static string BuildReport(PemtMotionLibrary library, PemtMotionMetadata motion)
        {
            if (library == null || motion == null)
                throw new ArgumentException("Select an imported PEMT and Motion ID.");
            string path = AssetDatabase.GetAssetPath(library);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".pemt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Selected library is not backed by a .pemt source.");
            byte[] data = File.ReadAllBytes(path);
            long start = motion.blockOffset;
            long end = start + (long)motion.blockByteCount;
            if (start < 0 || end <= start || end > data.LongLength)
                throw new InvalidDataException("Motion block outside physical PEMT file.");

            var report = new StringBuilder();
            report.AppendLine("EMD2Unity PEMT Motion Track Diagnostics v0.5.3 (read-only)");
            report.AppendLine("Asset: " + path);
            report.AppendLine("Motion ID: " + motion.motionIdHex + " (" + motion.motionId + ")");
            report.AppendLine("Motion block: [0x" + start.ToString("X") + ", 0x" +
                end.ToString("X") + ") | " + motion.blockByteCount + " bytes");
            report.AppendLine("Physical PEMT bytes: " + data.Length);
            report.AppendLine("Track count: " + (motion.tracks == null ? 0 : motion.tracks.Count));
            report.AppendLine("IMPORTANT: info high/low words and offset A/B are RAW fields." +
                " Unknown markers are NOT assigned a transform meaning.");
            int supported = 0, timeline = 0, unsupported = 0;
            if (motion.tracks != null)
            {
                const int maxTracks = 512;
                for (int i = 0; i < motion.tracks.Count && i < maxTracks; i++)
                {
                    PemtTrackMetadata track = motion.tracks[i];
                    if (track == null)
                    {
                        report.AppendLine("Track entry " + i + ": null metadata");
                        continue;
                    }
                    bool known = IsKnownTransform(track.markerHex);
                    bool special = IsKnownTimeline(track.markerHex);
                    if (known) supported++;
                    else if (special) timeline++;
                    else unsupported++;

                    report.AppendLine("");
                    report.AppendLine("Track entry " + i + ": " + TrackSummary(motion, track));
                    report.AppendLine("  Decoder: " + (known ? "known transform" :
                        special ? "known timeline marker (currently ignored)" :
                        "UNSUPPORTED / format not yet determined"));
                    report.AppendLine("  Unknown/reserved: " + track.unknownWordHex +
                        " | " + track.reserved1Hex + " | " + track.reserved2Hex);
                    long offsetA = start + (long)track.dataStartRelative;
                    long offsetB = start + (long)track.dataEndRelative;
                    report.AppendLine("  Offset A absolute: 0x" + offsetA.ToString("X") +
                        " | first32: " + SampleHex(data, offsetA, start, end, 32));
                    report.AppendLine("  Offset B absolute: 0x" + offsetB.ToString("X") +
                        " | first32: " + (track.dataEndRelative <= 0 ? "(not provided)" :
                        SampleHex(data, offsetB, start, end, 32)));
                    AppendScaleCandidate(report, data, track, start, end);
                }
                if (motion.tracks.Count > maxTracks)
                    report.AppendLine("Truncated report after " + maxTracks + " tracks.");
            }
            report.AppendLine("");
            report.AppendLine("SUMMARY: supported transform=" + supported +
                ", timeline=" + timeline + ", unsupported=" + unsupported + ".");
            report.AppendLine("Do not interpret an unknown marker as position or rotation" +
                " without confirming its binary payload and keyframe structure.");
            return report.ToString();
        }

        internal static void CopyAndLog(PemtMotionLibrary library, PemtMotionMetadata motion)
        {
            try
            {
                string report = BuildReport(library, motion);
                EditorGUIUtility.systemCopyBuffer = report;
                Debug.Log(report);
                EditorUtility.DisplayDialog("PEMT Motion Diagnostics",
                    "Motion track report copied to clipboard and written to Console." +
                    "\nPaste the report for binary analysis.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("EMD2Unity PEMT diagnostic failed: " + ex);
                EditorUtility.DisplayDialog("PEMT Motion Diagnostics", ex.Message, "OK");
            }
        }
    }
}
#endif
