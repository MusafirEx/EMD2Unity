// EMD2Unity: batch Sony MSF => WAV, preserving each source folder.
// Opt-in Editor action only. Does not overwrite original .msf or existing .wav.
// Uses official vgmstream-cli when configured, managed PCM/PS ADPCM fallback.
// Recursive scan uses the Unity AssetDatabase, not meshes or scene objects.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal sealed class MsfBulkWavReport
    {
        internal string Folder;
        internal int Found;
        internal int Exported;
        internal int SkippedExisting;
        internal int Failed;
        internal bool Cancelled;
        internal readonly List<string> Errors = new List<string>();

        internal string Summary =>
            "Folder: " + Folder + "\n" +
            "MSF files found: " + Found + "\n" +
            "WAV exported: " + Exported + "\n" +
            "Existing WAV skipped: " + SkippedExisting + "\n" +
            "Failed: " + Failed + "\n" +
            "Cancelled: " + (Cancelled ? "Yes" : "No");
    }

    internal static class MsfBulkWavExporter
    {
        internal static string[] FindMsfAssets(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) ||
                !AssetDatabase.IsValidFolder(folder) ||
                !(folder.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                  folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(
                    "Select a valid folder under this Unity project's Assets directory.");

            // FindAssets includes descendants. MSF assets can have a TextAsset
            // main object, so a t:DefaultAsset search would miss them.
            string[] guids = AssetDatabase.FindAssets("", new[] { folder });
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string prefix = folder.TrimEnd('/') + "/";
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.EndsWith(".msf", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!folder.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(path);
            }
            string[] paths = result.ToArray();
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        internal static int CountExistingWavs(string[] files)
        {
            return files.Count(path => File.Exists(Path.ChangeExtension(path, ".wav")));
        }

        internal static MsfBulkWavReport Export(string folder)
        {
            string[] sources = FindMsfAssets(folder);
            var report = new MsfBulkWavReport
            {
                Folder = folder,
                Found = sources.Length
            };
            if (sources.Length == 0) return report;

            try
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    string path = sources[i];
                    // Progress is cancellable between individual sounds;
                    // in-flight native decoding has its own 60s timeout.
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "EMD2Unity - Bulk MSF → WAV",
                        (i + 1) + " / " + sources.Length + ": " + path,
                        (float)i / sources.Length))
                    {
                        report.Cancelled = true;
                        break;
                    }

                    string wav = Path.ChangeExtension(path, ".wav").Replace('\\', '/');
                    if (File.Exists(wav))
                    {
                        report.SkippedExisting++;
                        continue;
                    }

                    string temporary = Path.Combine(
                        Path.GetDirectoryName(wav) ?? "",
                        ".emd2unity_msf_" + Guid.NewGuid().ToString("N") + ".wav")
                        .Replace('\\', '/');
                    try
                    {
                        // Always create a staging file first. File.Move cannot
                        // overwrite a newly created destination in the meantime.
                        DecodeToStagingWav(path, temporary);
                        ValidateWav(temporary);
                        File.Move(temporary, wav);
                        report.Exported++;
                    }
                    catch (Exception ex)
                    {
                        report.Failed++;
                        string message = path + " — " + ex.GetType().Name + ": " +
                            ex.Message;
                        report.Errors.Add(message);
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(temporary)) File.Delete(temporary);
                        }
                        catch (IOException ex)
                        {
                            UnityEngine.Debug.LogWarning(
                                "EMD2Unity MSF: could not remove temporary WAV " +
                                temporary + ": " + ex.Message);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                // Import all newly created WAVs once, via Unity's native audio
                // importer. Do not write assets during OnImportAsset callbacks.
                if (report.Exported > 0)
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            var summary = "EMD2Unity MSF bulk export:\n" + report.Summary;
            if (report.Errors.Count > 0)
                summary += "\n\nErrors (" + report.Errors.Count + "):\n" +
                    string.Join("\n", report.Errors.ToArray());
            if (report.Failed > 0) UnityEngine.Debug.LogWarning(summary);
            else UnityEngine.Debug.Log(summary);
            return report;
        }

        private static void DecodeToStagingWav(string sourcePath, string temporary)
        {
            var importer = AssetImporter.GetAtPath(sourcePath) as MsfImporter;
            bool preferVgmstream = importer == null || importer.PreferVgmstream;
            string overridePath = importer == null ? "" : importer.VgmstreamCliPath;
            string cli = VgmstreamIntegration.ResolveExecutable(overridePath);
            bool cliAvailable = !string.IsNullOrWhiteSpace(cli) && File.Exists(cli);
            Exception managedFailure = null;
            Exception cliFailure = null;

            if (!preferVgmstream)
            {
                try
                {
                    DecodeManagedToWav(sourcePath, temporary);
                    return;
                }
                catch (Exception ex)
                {
                    managedFailure = ex;
                }
            }

            if (cliAvailable)
            {
                try
                {
                    DecodeWithVgmstreamToWav(sourcePath, temporary, cli);
                    return;
                }
                catch (Exception ex)
                {
                    cliFailure = ex;
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (IOException) { /* cleaned after this file */ }
                }
            }

            if (preferVgmstream)
            {
                try
                {
                    DecodeManagedToWav(sourcePath, temporary);
                    return;
                }
                catch (Exception ex)
                {
                    managedFailure = ex;
                }
            }

            string detail = managedFailure == null ? "Built-in decoder not attempted."
                : managedFailure.Message;
            if (cliFailure != null)
                detail += " | vgmstream-cli error: " + cliFailure.Message;
            else if (!cliAvailable)
                detail += " | vgmstream-cli is not installed/configured.";
            throw new InvalidDataException(
                "No available decoder could export this MSF. " + detail);
        }

        private static void DecodeManagedToWav(string sourcePath, string temporary)
        {
            byte[] source = File.ReadAllBytes(sourcePath);
            MsfHeader header = MsfAudioDecoder.ParseHeader(source);
            MsfAudioData decoded = MsfAudioDecoder.DecodeManaged(source, header);
            byte[] output = MsfAudioDecoder.EncodeWav16(decoded);
            File.WriteAllBytes(temporary, output);
        }

        private static void DecodeWithVgmstreamToWav(
            string sourcePath, string temporary, string executable)
        {
            string input = Path.GetFullPath(sourcePath);
            string destination = Path.GetFullPath(temporary);
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "-i -W 1 -o \"" +
                    destination.Replace("\"", "") + "\" \"" +
                    input.Replace("\"", "") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            using (Process process = Process.Start(psi))
            {
                if (process == null)
                    throw new IOException("Unable to start vgmstream-cli.");
                if (!process.WaitForExit(60000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException(
                        "vgmstream-cli exceeded 60 seconds for " + sourcePath);
                }
                if (process.ExitCode != 0)
                    throw new InvalidDataException(
                        "vgmstream-cli failed with exit code " + process.ExitCode +
                        " for " + sourcePath);
            }
        }

        private static void ValidateWav(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < 44)
                throw new InvalidDataException("Decoder did not produce a usable WAV.");
            using (var stream = File.OpenRead(path))
            {
                var header = new byte[12];
                if (stream.Read(header, 0, header.Length) != header.Length ||
                    header[0] != 'R' || header[1] != 'I' ||
                    header[2] != 'F' || header[3] != 'F' ||
                    header[8] != 'W' || header[9] != 'A' ||
                    header[10] != 'V' || header[11] != 'E')
                    throw new InvalidDataException(
                        "Decoder output is not a RIFF/WAVE file.");
            }
        }
    }

    public sealed class MsfBulkWavExporterWindow : EditorWindow
    {
        private DefaultAsset folderAsset;
        private string folderPath = "";
        private string[] discovered = new string[0];
        private int existing;
        private MsfBulkWavReport lastReport;
        private Vector2 scroll;

        [MenuItem("Tools/EMD2Unity/MSF/Bulk Export MSF to WAV...")]
        private static void OpenWindow()
        {
            MsfBulkWavExporterWindow window =
                GetWindow<MsfBulkWavExporterWindow>("Bulk MSF → WAV");
            window.minSize = new Vector2(480f, 300f);
            window.AdoptSelection();
            window.Show();
        }

        [MenuItem("Assets/EMD2Unity/MSF/Bulk Export MSF to WAV", false, 2001)]
        private static void OpenFromProject()
        {
            OpenWindow();
        }

        [MenuItem("Assets/EMD2Unity/MSF/Bulk Export MSF to WAV", true)]
        private static bool ValidateProjectMenu()
        {
            return AssetDatabase.IsValidFolder(
                AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(folderPath))
                AdoptSelection();
        }

        private void AdoptSelection()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!AssetDatabase.IsValidFolder(path) &&
                !string.IsNullOrEmpty(path) &&
                path.EndsWith(".msf", StringComparison.OrdinalIgnoreCase))
                path = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path) && path.StartsWith(
                    "Assets", StringComparison.OrdinalIgnoreCase))
                SetFolder(path);
        }

        private void SetFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path) ||
                !(path.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                  path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
            {
                EditorUtility.DisplayDialog("Bulk MSF → WAV",
                    "Choose a folder inside this Unity project's Assets directory.",
                    "OK");
                return;
            }
            folderPath = path;
            folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            RefreshListing();
        }

        private void RefreshListing()
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                discovered = new string[0];
                existing = 0;
                return;
            }
            try
            {
                discovered = MsfBulkWavExporter.FindMsfAssets(folderPath);
                existing = MsfBulkWavExporter.CountExistingWavs(discovered);
            }
            catch (Exception ex)
            {
                discovered = new string[0];
                existing = 0;
                UnityEngine.Debug.LogWarning("EMD2Unity MSF scan: " + ex.Message);
            }
        }

        private void BrowseFolder()
        {
            string initial = string.IsNullOrEmpty(folderPath) ?
                Application.dataPath : Path.GetFullPath(folderPath);
            string selected = EditorUtility.OpenFolderPanel(
                "Select folder containing MSF sounds", initial, "");
            if (string.IsNullOrEmpty(selected)) return;

            string assets = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string absolute = Path.GetFullPath(selected)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringComparison comparison =
                Application.platform == RuntimePlatform.WindowsEditor ?
                StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (absolute.Equals(assets, comparison))
            {
                SetFolder("Assets");
                return;
            }
            string prefix = assets + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(prefix, comparison))
            {
                EditorUtility.DisplayDialog("Bulk MSF → WAV",
                    "The selected folder must be inside this Unity project's Assets/ folder.",
                    "OK");
                return;
            }
            string relative = "Assets/" + absolute.Substring(prefix.Length)
                .Replace('\\', '/');
            SetFolder(relative);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Bulk MSF → WAV", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Search the selected Assets folder and ALL subfolders for .msf. " +
                "Export each to a WAV next to the original MSF, keeping the " +
                "directory hierarchy unchanged. Existing WAVs are skipped " +
                "(never overwritten). Original MSF files are untouched.",
                MessageType.Info);
            EditorGUI.BeginChangeCheck();
            DefaultAsset newFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Source Folder", folderAsset, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                string path = AssetDatabase.GetAssetPath(newFolder);
                SetFolder(path);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Selected Project Folder"))
                AdoptSelection();
            if (GUILayout.Button("Browse Assets Folder..."))
                BrowseFolder();
            if (GUILayout.Button("Rescan"))
                RefreshListing();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Folder", string.IsNullOrEmpty(folderPath) ?
                "(none selected)" : folderPath);
            EditorGUILayout.LabelField("Search", "Recursive (including subfolders)");
            EditorGUILayout.LabelField("MSF Files Found", discovered.Length.ToString());
            EditorGUILayout.LabelField("WAV Files Already Present", existing.ToString());
            EditorGUILayout.LabelField("New WAV Candidates",
                Math.Max(0, discovered.Length - existing).ToString());
            string decoder = VgmstreamIntegration.ResolveExecutable("");
            EditorGUILayout.LabelField("vgmstream-cli", string.IsNullOrWhiteSpace(decoder) ?
                "(not configured; managed PCM/ADPCM only)" : decoder);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(discovered.Length == 0))
            {
                if (GUILayout.Button("Export All MSF to WAV", GUILayout.Height(30f)))
                {
                    if (EditorUtility.DisplayDialog("Confirm Bulk MSF → WAV",
                        "Source folder: " + folderPath + "\n" +
                        "Found MSF files: " + discovered.Length + "\n" +
                        "Existing WAV to skip: " + existing + "\n" +
                        "New WAV candidates: " + (discovered.Length - existing) +
                        "\n\nOutput will be saved beside each original MSF. " +
                        "Original MSFs and existing WAVs will not be modified. " +
                        "You can cancel between files.",
                        "Export WAV", "Cancel"))
                    {
                        lastReport = MsfBulkWavExporter.Export(folderPath);
                        RefreshListing();
                        EditorUtility.DisplayDialog("Bulk MSF → WAV Result",
                            lastReport.Summary +
                            (lastReport.Failed > 0 ?
                                "\n\nSee Unity Console for each failed file." : ""),
                            "OK");
                    }
                }
            }

            if (lastReport != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last Export", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(lastReport.Summary,
                    lastReport.Failed > 0 ? MessageType.Warning : MessageType.Info);
                if (lastReport.Errors.Count > 0)
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll,
                        GUILayout.MinHeight(40f), GUILayout.MaxHeight(180f));
                    foreach (string message in lastReport.Errors.Take(30))
                        EditorGUILayout.SelectableLabel(message,
                            GUILayout.Height(34f));
                    EditorGUILayout.EndScrollView();
                }
            }
        }
    }
}
#endif
