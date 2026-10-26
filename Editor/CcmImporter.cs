// Sousei CCM read-only structure parser: 16-byte header, indexed 0x80-byte
// records, 16-byte footer. Layout cross-checked on CSD/GSD/SPC v1.02 samples.
// Values at +0x14,+0x18,+0x1C,+0x68 remain semantically unverified.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [ScriptedImporter(1, "ccm")]
    public sealed class CcmImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            CcmComboLibrary result;
            try
            {
                result = CcmBinaryReader.Read(File.ReadAllBytes(ctx.assetPath));
            }
            catch (Exception ex)
            {
                result = ScriptableObject.CreateInstance<CcmComboLibrary>();
                result.importError = ex.Message;
                ctx.LogImportError("EMD2Unity CCM: " + ex.Message);
            }
            result.name = Path.GetFileNameWithoutExtension(ctx.assetPath) + " [CCM Combo]";
            ctx.AddObjectToAsset("ccm", result);
            ctx.SetMainObject(result);
        }
    }

    internal static class CcmBinaryReader
    {
        private const int HeaderBytes = 0x10;
        private const int PairBytes = 0x08;
        private const int RecordBytes = 0x80;
        private const int FooterBytes = 0x10;
        private const int MaxRecords = 32768;
        private const int MaxFileBytes = 64 * 1024 * 1024;

        private static uint U32(byte[] data, int off)
        {
            if (off < 0 || off > data.Length - 4)
                throw new InvalidDataException("CCM: reading beyond file at 0x" + off.ToString("X"));
            return (uint)data[off] | ((uint)data[off + 1] << 8) |
                   ((uint)data[off + 2] << 16) | ((uint)data[off + 3] << 24);
        }

        private static bool HasMagic(byte[] data, int at)
        {
            return data[at] == 0x43 && data[at + 1] == 0x43 &&
                   data[at + 2] == 0x4D && data[at + 3] == 0;
        }

        internal static CcmComboLibrary Read(byte[] data)
        {
            if (data == null || data.Length < 0x30 || data.Length > MaxFileBytes)
                throw new InvalidDataException("CCM: invalid file size (expected 0x10 header + indexed 0x80 records + 0x10 footer).");
            if (!HasMagic(data, 0))
                throw new InvalidDataException("CCM: missing 'CCM\\0' signature.");

            uint version = U32(data, 4);
            uint countU = U32(data, 8);
            if (countU < 1 || countU > MaxRecords)
                throw new InvalidDataException("CCM: unsupported record count " + countU);
            if (U32(data, 12) != 0)
                throw new InvalidDataException("CCM: non-zero reserved header word; unknown layout.");

            int count = (int)countU;
            long expectedBytes = 0x20L + count * 0x88L;
            if (expectedBytes != data.Length)
                throw new InvalidDataException("CCM: file size mismatch. Declared " + count +
                    " records require " + expectedBytes + " bytes, actual " + data.Length + ".");

            int firstRecord = HeaderBytes + count * PairBytes;
            int footer = data.Length - FooterBytes;
            if (!HasMagic(data, footer) || U32(data, footer + 4) != version ||
                U32(data, footer + 8) != 0 || U32(data, footer + 12) != 0)
                throw new InvalidDataException("CCM: footer signature/version/empty-record marker differs.");

            var result = ScriptableObject.CreateInstance<CcmComboLibrary>();
            try
            {
                result.sourceByteCount = data.Length;
                result.versionHex = "0x" + version.ToString("X8");
                result.recordCount = count;
                result.firstRecordOffset = firstRecord;
                result.footerOffset = footer;
                result.allDifferences20 = true;
                result.allLinksPointToRecordIds = true;

                for (int i = 0; i < count; ++i)
                {
                    int pairOffset = HeaderBytes + i * PairBytes;
                    uint id = U32(data, pairOffset);
                    uint offsetU = U32(data, pairOffset + 4);
                    int expectedOffset = firstRecord + i * RecordBytes;
                    if (id != (uint)i || offsetU != (uint)expectedOffset)
                        throw new InvalidDataException(
                            "CCM: pointer table entry " + i + " differs from indexed 0x80 record layout: " +
                            "ID=" + id + ", offset=0x" + offsetU.ToString("X") +
                            ", expected=0x" + expectedOffset.ToString("X"));
                    int offset = expectedOffset;
                    if (U32(data, offset) != id)
                        throw new InvalidDataException("CCM: record ID mismatch at index " + i);

                    uint rawA = U32(data, offset + 0x14);
                    uint rawB = U32(data, offset + 0x18);
                    if (rawA > int.MaxValue || rawB > int.MaxValue)
                        throw new InvalidDataException("CCM: timing-like fields exceed supported signed range at record " + i);

                    uint code1C = U32(data, offset + 0x1C);
                    uint link68 = U32(data, offset + 0x68);
                    var record = new CcmComboRecord
                    {
                        recordIndex = i,
                        tableId = (int)id,
                        recordId = (int)U32(data, offset),
                        recordOffset = offset,
                        valueAt04Hex = "0x" + U32(data, offset + 4).ToString("X8"),
                        valueAt10Hex = "0x" + U32(data, offset + 0x10).ToString("X8"),
                        valueAt14 = (int)rawA,
                        valueAt18 = (int)rawB,
                        differenceAminusB = (int)rawA - (int)rawB,
                        codeAt1CHex = "0x" + code1C.ToString("X8"),
                        codeAt1CGroup = (int)(code1C >> 16),
                        codeAt1CIndex = (int)(code1C & 0xFFFF),
                        linkAt68Hex = "0x" + link68.ToString("X8"),
                        linkAt68Group = (int)(link68 >> 16),
                        linkAt68Index = (int)(link68 & 0xFFFF),
                        linkIndexIsPresent = (link68 & 0xFFFF) < countU
                    };
                    for (int k = 0; k < 32; ++k)
                        record.rawWordsHex[k] = "0x" + U32(data, offset + k * 4).ToString("X8");
                    result.records.Add(record);

                    if (record.differenceAminusB != 20) result.allDifferences20 = false;
                    if (!record.linkIndexIsPresent) result.allLinksPointToRecordIds = false;
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
