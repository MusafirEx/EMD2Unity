// EMD2Unity - indexed PEMT motion-container metadata importer.
// Research based on user-provided Kamen Rider Battride War Sousei update v1.02.
// This importer does not decode keyframes or generate AnimationClips.
// Metadata asset types are declared in PemtMotionLibrary.cs (Unity MonoScript requirement).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [ScriptedImporter(4, "pemt")]
    public sealed class PemtImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            PemtMotionLibrary library;
            try
            {
                library = PemtReader.Read(File.ReadAllBytes(ctx.assetPath));
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
