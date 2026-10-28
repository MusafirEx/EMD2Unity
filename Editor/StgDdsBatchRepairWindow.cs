// EMD2Unity: conservative, opt-in STG DDS partial-mipmap repair.
// Does not decode/recompress source pixels. Original .dds files are backed
// up outside Assets before replacement; unsupported formats remain untouched.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class StgDdsBatchRepairWindow : EditorWindow
    {
        private const string Root = "Assets/KRBWS/stg";
        private const uint DdsdMipmapCount = 0x00020000u;
        private const uint DdscapsComplex = 0x00000008u;
        private const uint DdscapsMipmap = 0x00400000u;
        private const uint DdscapsTexture = 0x00001000u;
        private const int HeaderBytes = 128;

        private sealed class Item
        {
            public string path;
            public string detail;
            public string format;
            public int width, height, declaredMips, fullMips;
            public long bytes;
            public int topLevelBytes;
            public bool canRepair;
        }

        private readonly List<Item> results = new List<Item>();
        private Vector2 scroll;
        private int fixable, unchanged, unsupported;
        private string lastAction = "Click Scan STG DDS to analyze the project's STG textures.";
        private bool scanned;

        [MenuItem("Tools/EMD2Unity/STG DDS Batch Repair")]
        public static void Open()
        {
            GetWindow<StgDdsBatchRepairWindow>("STG DDS Batch Repair").minSize =
                new Vector2(650, 400);
        }

        private static uint Word(byte[] b, int p)
        {
            return (uint)b[p] | ((uint)b[p + 1] << 8) |
                   ((uint)b[p + 2] << 16) | ((uint)b[p + 3] << 24);
        }

        private static void PutWord(byte[] b, int p, uint value)
        {
            for (int i = 0; i < 4; i++) b[p + i] = (byte)(value >> (8 * i));
        }

        private static string FourCC(byte[] b)
        {
            return Encoding.ASCII.GetString(b, 84, 4);
        }

        private static int LevelSize(int width, int height, int blockBytes)
        {
            return checked(Math.Max(1, (width + 3) / 4) *
                           Math.Max(1, (height + 3) / 4) * blockBytes);
        }

        private static Item Analyze(string assetPath)
        {
            var result = new Item { path = assetPath };
            try
            {
                using (var stream = new FileStream(assetPath, FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite))
                {
                    result.bytes = stream.Length;
                    if (stream.Length < HeaderBytes)
                    {
                        result.detail = "Truncated DDS header";
                        return result;
                    }
                    var header = new byte[HeaderBytes];
                    if (stream.Read(header, 0, header.Length) != header.Length)
                    {
                        result.detail = "Could not read DDS header";
                        return result;
                    }
                    if (header[0] != 'D' || header[1] != 'D' ||
                        header[2] != 'S' || header[3] != ' ' ||
                        Word(header, 4) != 124 || Word(header, 76) != 32)
                    {
                        result.detail = "Nonstandard/invalid DDS header";
                        return result;
                    }
                    uint rawW = Word(header, 16), rawH = Word(header, 12);
                    uint rawMip = Word(header, 28);
                    uint caps2 = Word(header, 112);
                    uint depth = Word(header, 24);
                    result.format = FourCC(header);
                    if (rawW == 0 || rawH == 0 || rawW > 16384 || rawH > 16384)
                    {
                        result.detail = "Invalid or unsupported dimensions";
                        return result;
                    }
                    result.width = (int)rawW;
                    result.height = (int)rawH;
                    result.declaredMips = rawMip == 0 ? 1 : (int)Math.Min(rawMip, 1000u);
                    if (caps2 != 0 || depth > 1)
                    {
                        result.detail = "Cube/volume/other DDS layout: manual investigation";
                        return result;
                    }
                    if ((Word(header, 80) & 0x4u) == 0u)
                    {
                        result.detail = "Not a FourCC block-compressed DDS";
                        return result;
                    }
                    int blockBytes = result.format == "DXT1" ? 8 :
                                     result.format == "DXT3" || result.format == "DXT5" ? 16 : 0;
                    if (blockBytes == 0)
                    {
                        result.detail = "Unsupported format " + result.format +
                            " (no automatic rewrite)";
                        return result;
                    }

                    int w = result.width, h = result.height, fullMips = 0;
                    while (true)
                    {
                        fullMips++;
                        if (w == 1 && h == 1) break;
                        w = Math.Max(1, w >> 1);
                        h = Math.Max(1, h >> 1);
                    }
                    result.fullMips = fullMips;
                    result.topLevelBytes = LevelSize(result.width, result.height, blockBytes);
                    if (result.declaredMips > fullMips)
                    {
                        result.detail = "Invalid mip count exceeds theoretical maximum";
                        return result;
                    }
                    long expected = HeaderBytes;
                    w = result.width; h = result.height;
                    for (int i = 0; i < result.declaredMips; i++)
                    {
                        expected += LevelSize(w, h, blockBytes);
                        w = Math.Max(1, w >> 1);
                        h = Math.Max(1, h >> 1);
                    }
                    if (result.bytes != expected)
                    {
                        result.detail = "Payload size mismatch: actual " + result.bytes +
                            ", expected " + expected + ". Manual investigation.";
                        return result;
                    }
                    if (result.declaredMips <= 1)
                    {
                        result.detail = "Already single-level DDS; no change";
                        return result;
                    }
                    if (result.declaredMips >= result.fullMips)
                    {
                        result.detail = "Complete mip chain; no change";
                        return result;
                    }
                    result.canRepair = true;
                    result.detail = "Verified partial mip chain " + result.declaredMips +
                        "/" + result.fullMips + " — can create a 1-level " +
                        result.format + " DDS without recompressing top-level blocks";
                }
            }
            catch (Exception e)
            {
                result.detail = "Read failed: " + e.GetType().Name + ": " + e.Message;
            }
            return result;
        }

        private void Scan()
        {
            results.Clear();
            fixable = unchanged = unsupported = 0;
            scanned = false;
            if (!Directory.Exists(Root))
            {
                lastAction = "Missing STG folder: " + Root;
                return;
            }
            string[] paths = Directory.GetFiles(Root, "*", SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            int index = 0;
            try
            {
                foreach (string file in paths)
                {
                    if (!file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (EditorUtility.DisplayCancelableProgressBar("EMD2Unity STG DDS Scan",
                        file, paths.Length == 0 ? 0f : (float)index / paths.Length))
                        break;
                    index++;
                    Item item = Analyze(file.Replace('\\', '/'));
                    results.Add(item);
                    if (item.canRepair) fixable++;
                    else if (item.detail.Contains("no change")) unchanged++;
                    else unsupported++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            scanned = true;
            lastAction = "Scanned " + results.Count + " DDS files: " + fixable +
                " verified partial mipmaps, " + unchanged + " already valid/complete, " +
                unsupported + " require separate investigation. No files modified.";
            Debug.Log("EMD2Unity STG DDS Scan: " + lastAction);
        }

        private static string BackupPath(Item item)
        {
            // Outside Assets, to avoid importing backups as textures.
            string project = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(project, "EMD2Unity_DDS_Backups",
                item.path.Replace('/', Path.DirectorySeparatorChar));
        }

        // Never overwrite an existing backup. A repeated Apply can encounter
        // old backups even when the original DDS was never successfully repaired.
        // Reserve a unique file using CreateNew (race-safe) and preserve every
        // existing copy, including those made by earlier EMD2Unity versions.
        private static string SaveUniqueBackup(Item item, byte[] original)
        {
            string firstPath = BackupPath(item);
            string directory = Path.GetDirectoryName(firstPath);
            Directory.CreateDirectory(directory);
            for (int number = 0; number <= 9999; number++)
            {
                string destination = number == 0 ? firstPath :
                    Path.Combine(directory, Path.GetFileNameWithoutExtension(firstPath) +
                        ".backup-" + number.ToString("D4") + Path.GetExtension(firstPath));
                FileStream stream;
                try
                {
                    stream = new FileStream(destination, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None);
                }
                catch (IOException)
                {
                    if (File.Exists(destination)) continue; // Existing backup is preserved.
                    throw;
                }
                try
                {
                    using (stream) stream.Write(original, 0, original.Length);
                    return destination;
                }
                catch
                {
                    stream.Dispose();
                    try { File.Delete(destination); }
                    catch (IOException) { /* Surface the original write failure. */ }
                    catch (UnauthorizedAccessException) { /* Preserve original error. */ }
                    throw;
                }
            }
            throw new IOException("All 10000 backup names are already occupied: " + firstPath);
        }

        private static string RepairOne(Item item)
        {
            // Revalidate the file immediately before writing; no stale scan data.
            Item fresh = Analyze(item.path);
            if (!fresh.canRepair || fresh.bytes != item.bytes ||
                fresh.format != item.format ||
                fresh.declaredMips != item.declaredMips)
                throw new InvalidDataException("Source changed after scan; scan again.");

            byte[] original = File.ReadAllBytes(item.path);
            if (original.LongLength != item.bytes)
                throw new InvalidDataException("Source size changed during repair.");
            byte[] fixedData = new byte[HeaderBytes + item.topLevelBytes];
            Buffer.BlockCopy(original, 0, fixedData, 0, fixedData.Length);
            PutWord(fixedData, 8, Word(fixedData, 8) & ~DdsdMipmapCount);
            PutWord(fixedData, 28, 1u);
            PutWord(fixedData, 108,
                (Word(fixedData, 108) & ~(DdscapsComplex | DdscapsMipmap)) |
                 DdscapsTexture);
            // Preserve DXT compression, dimensions, top mip payload and .meta.
            string backup = SaveUniqueBackup(item, original);
            string temp = item.path + ".emd2unity-tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                    stream.Write(fixedData, 0, fixedData.Length);
                File.Replace(temp, item.path, null);
                return backup;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private void Apply()
        {
            if (!scanned || fixable == 0) return;
            string project = Directory.GetParent(Application.dataPath).FullName;
            if (!EditorUtility.DisplayDialog("STG DDS Batch Repair",
                "Apply verified DXT1/DXT3/DXT5 partial-mipmap fixes to " +
                fixable + " DDS file(s) under " + Root + "?\n\n" +
                "The original files will be backed up to:\n" +
                Path.Combine(project, "EMD2Unity_DDS_Backups") +
                "\nExisting backup files remain untouched; unique numbered " +
                "backup names are created when needed." +
                "\n\nNo unknown, malformed or otherwise unsupported DDS will be changed. " +
                "Source paths and Unity .meta files remain intact.\n\n" +
                "This is opt-in. Consider a separate project backup first.",
                "Back Up Originals & Repair", "Cancel"))
                return;

            int repaired = 0, failed = 0, versionedBackups = 0;
            try
            {
                for (int i = 0; i < results.Count; i++)
                {
                    Item item = results[i];
                    if (!item.canRepair) continue;
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "EMD2Unity STG DDS Repair", item.path,
                        (float)i / results.Count)) break;
                    try
                    {
                        string backup = RepairOne(item);
                        repaired++;
                        if (!string.Equals(backup, BackupPath(item),
                            StringComparison.OrdinalIgnoreCase))
                            versionedBackups++;
                        Debug.Log("EMD2Unity STG DDS repaired: " + item.path +
                            " | Backup: " + backup);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Debug.LogWarning("EMD2Unity STG DDS skipped: " +
                            item.path + " — " + ex.Message);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (repaired > 0) AssetDatabase.Refresh();
            }
            lastAction = "Repaired " + repaired + " DDS file(s) with original backups " +
                "(" + versionedBackups + " versioned backup names). " +
                "Skipped/failed " + failed + ". Scan again to verify. " +
                "Inspect any remaining unsupported DDS separately.";
            Debug.Log("EMD2Unity STG DDS: " + lastAction);
            scanned = false; // Require fresh scan before another application.
        }

        private static string Csv(string text)
        {
            return "\"" + (text ?? "").Replace("\"", "\"\"") + "\"";
        }

        private void ExportCsv()
        {
            if (!scanned) return;
            string path = EditorUtility.SaveFilePanel("Export STG DDS Audit CSV",
                Directory.GetParent(Application.dataPath).FullName,
                "EMD2Unity_STG_DDS_Audit", "csv");
            if (string.IsNullOrEmpty(path)) return;
            var sb = new StringBuilder();
            sb.AppendLine("Path,Format,Width,Height,DeclaredMips,FullMips,Bytes,Fixable,Details");
            foreach (Item item in results)
            {
                sb.AppendLine(Csv(item.path) + "," + Csv(item.format) + "," +
                    item.width + "," + item.height + "," + item.declaredMips + "," +
                    item.fullMips + "," + item.bytes + "," +
                    item.canRepair + "," + Csv(item.detail));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            lastAction = "Exported audit: " + path;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("STG DDS Batch Audit & Repair", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Scans Assets/KRBWS/stg recursively. Identifies exact-size " +
                "DXT1/DXT3/DXT5 partial mip chains. Automatic repair keeps the top " +
                "compressed image and removes incomplete lower mip levels. " +
                "Original DDS backups are made outside Assets; existing backups " +
                "are preserved using unique numbered filenames. Unsupported formats " +
                "and non-mip errors are NOT guessed or changed.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan STG DDS (Read-only)", GUILayout.Height(27)))
                Scan();
            using (new EditorGUI.DisabledScope(!scanned))
                if (GUILayout.Button("Export Audit CSV", GUILayout.Height(27))) ExportCsv();
            using (new EditorGUI.DisabledScope(!scanned || fixable == 0))
                if (GUILayout.Button("Apply Safe Fixes (Backups)", GUILayout.Height(27)))
                    Apply();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(lastAction, MessageType.Info);
            if (!scanned) return;
            EditorGUILayout.LabelField(
                "Repairable: " + fixable + "  |  Already valid: " + unchanged +
                "  |  Unsupported/ambiguous: " + unsupported +
                "  |  Total: " + results.Count, EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Item item in results)
            {
                EditorGUILayout.LabelField(item.path, EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(
                    (item.canRepair ? "FIXABLE: " : "") +
                    item.width + "x" + item.height + " " + item.format +
                    " — " + item.detail, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
