// Optional official vgmstream-cli integration for Sony MSF AudioClip import.
// Source: https://github.com/vgmstream/vgmstream (ISC-style COPYING).
// Does NOT ship native binaries or download anything during asset import.
// Explicit Editor install action downloads the official tagged GitHub release
// into the project's Library directory (not Assets / UPM package).
#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal static class VgmstreamIntegration
    {
        internal const string ReleaseTag = "r2168";
        internal const string GlobalCliPathKey =
            "MusafirEx.EMD2Unity.MSF.DefaultVgmstreamCli";
        private const long MaxZipBytes = 90L * 1024 * 1024;
        private const long MaxExpandedBytes = 300L * 1024 * 1024;
        private const int MaxFiles = 1024;

        internal static string SharedExecutable => EditorPrefs.GetString(GlobalCliPathKey, "");
        internal static string InstallationRoot => Path.Combine(
            Directory.GetCurrentDirectory(), "Library", "EMD2Unity", "vgmstream");
        internal static string ReleaseFolder => Path.Combine(InstallationRoot, ReleaseTag);

        internal static string ResolveExecutable(string perAssetPath)
        {
            // Explicit per-file override wins. Otherwise prefer the CURRENT
            // project's installed decoder to avoid stale EditorPrefs references
            // pointing into another Unity project's Library directory.
            if (!string.IsNullOrWhiteSpace(perAssetPath))
                return perAssetPath.Trim();
            string installed = FindCli(ReleaseFolder);
            if (!string.IsNullOrWhiteSpace(installed))
                return installed;
            return SharedExecutable.Trim();
        }

        internal static string FindCli(string directory)
        {
            if (!Directory.Exists(directory)) return "";
            string name = Application.platform == RuntimePlatform.WindowsEditor
                ? "vgmstream-cli.exe" : "vgmstream-cli";
            string[] paths = Directory.GetFiles(directory, name,
                SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            return paths.Length > 0 ? paths[0] : "";
        }

        internal static string ReleaseArchiveName
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        return "vgmstream-win64.zip";
                    case RuntimePlatform.LinuxEditor:
                        return "vgmstream-linux.zip";
                    case RuntimePlatform.OSXEditor:
                        return "vgmstream-mac.zip";
                    default:
                        throw new PlatformNotSupportedException(
                            "No official vgmstream CLI archive for this Editor platform.");
                }
            }
        }

        [MenuItem("Tools/EMD2Unity/MSF/Install Official vgmstream-cli (r2168)")]
        internal static void InstallFromOfficialReleaseMenu()
        {
            try
            {
                string exe = InstallFromOfficialRelease();
                EditorUtility.DisplayDialog("EMD2Unity - vgmstream Installed",
                    "Official " + ReleaseTag + " CLI installed at:\n" + exe +
                    "\n\nReimport .msf files to generate AudioClip children.",
                    "OK");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    "EMD2Unity: official vgmstream install failed: " + ex);
                EditorUtility.DisplayDialog("vgmstream Install Failed",
                    ex.Message + "\n\nYou can still select an existing " +
                    "vgmstream-cli manually in the MSF Inspector.", "OK");
            }
        }

        internal static string InstallFromOfficialRelease()
        {
            string archiveName = ReleaseArchiveName;
            string url = "https://github.com/vgmstream/vgmstream/releases/download/" +
                ReleaseTag + "/" + archiveName;
            if (!EditorUtility.DisplayDialog("Install official vgmstream-cli",
                "Download vgmstream " + ReleaseTag + " directly from:\n" + url +
                "\n\nThe third-party decoder will run as an external process " +
                "for MSF files you import. The executable and dependencies " +
                "will be stored in this project's Library/EMD2Unity folder; " +
                "NOT in Assets or the Git package.\n\nvgmstream copyright " +
                "and license: github.com/vgmstream/vgmstream/blob/master/COPYING",
                "Download and Install", "Cancel"))
                return "";

            Directory.CreateDirectory(InstallationRoot);
            string temporary = Path.Combine(InstallationRoot,
                "_install_" + Guid.NewGuid().ToString("N"));
            string zipPath = temporary + ".zip";
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.UserAgent,
                        "EMD2Unity-Unity-Editor");
                    EditorUtility.DisplayProgressBar("Installing vgmstream",
                        "Downloading the official " + ReleaseTag + " archive...", 0.1f);
                    client.DownloadFile(url, zipPath);
                }

                long bytes = new FileInfo(zipPath).Length;
                if (bytes < 1024 || bytes > MaxZipBytes)
                    throw new InvalidDataException(
                        "Downloaded vgmstream archive has an unexpected size: " + bytes);
                Directory.CreateDirectory(temporary);
                ExtractValidated(zipPath, temporary);
                string executable = FindCli(temporary);
                if (string.IsNullOrEmpty(executable))
                    throw new InvalidDataException(
                        "The official archive did not contain vgmstream-cli.");

                // UNIX ZIP metadata may not preserve executable permissions in .NET.
                if (Application.platform != RuntimePlatform.WindowsEditor)
                {
                    using (var chmod = Process.Start(new ProcessStartInfo
                    {
                        FileName = "/bin/chmod",
                        Arguments = "+x \"" + executable.Replace("\"", "") + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }))
                    {
                        if (chmod == null || !chmod.WaitForExit(10000) ||
                            chmod.ExitCode != 0)
                            throw new IOException("Could not enable vgmstream-cli executable permission.");
                    }
                }
                EnsureCliWorks(executable);

                // Only install AFTER the downloaded CLI passes the sanity check.
                if (Directory.Exists(ReleaseFolder))
                    Directory.Delete(ReleaseFolder, true);
                Directory.Move(temporary, ReleaseFolder);
                executable = FindCli(ReleaseFolder);
                if (string.IsNullOrWhiteSpace(executable))
                    throw new IOException("vgmstream executable lost during installation.");

                // Set the shared path so existing MSF settings work automatically.
                EditorPrefs.SetString(GlobalCliPathKey, executable);
                UnityEngine.Debug.Log("EMD2Unity: installed official vgmstream " +
                    ReleaseTag + ": " + executable + " | source: " + url);
                return executable;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                try { if (File.Exists(zipPath)) File.Delete(zipPath); }
                catch (IOException) { /* report already logged */ }
                try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
                catch (IOException) { /* report already logged */ }
            }
        }

        private static void ExtractValidated(string archive, string output)
        {
            string root = Path.GetFullPath(output);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;
            long extractedSize = 0;
            int count = 0;
            using (ZipArchive zip = ZipFile.OpenRead(archive))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (++count > MaxFiles)
                        throw new InvalidDataException("Too many entries in vgmstream archive.");
                    if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                        throw new InvalidDataException("Symlinks are not allowed in decoder archive.");
                    extractedSize += entry.Length;
                    if (extractedSize > MaxExpandedBytes)
                        throw new InvalidDataException("Uncompressed decoder files exceed size limit.");

                    string filename = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string destination = Path.GetFullPath(Path.Combine(output, filename));
                    if (!destination.StartsWith(root,
                        Application.platform == RuntimePlatform.WindowsEditor ?
                        StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidDataException("Unsafe ZIP archive entry: " +
                            entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name))
                        Directory.CreateDirectory(destination);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, false);
                    }
                }
            }
        }

        internal static void EnsureCliWorks(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
                throw new FileNotFoundException(
                    "Cannot find vgmstream-cli executable.", path);

            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-V",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            }))
            {
                if (process == null)
                    throw new IOException("Could not launch vgmstream-cli.");
                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(10000) || process.ExitCode != 0 ||
                    string.IsNullOrWhiteSpace(output))
                    throw new IOException(
                        "vgmstream-cli -V did not return valid version information.");
            }
        }

        // Read JSON metadata using the same upstream decoder that will
        // extract the audio. This can show codec, subsong count and loop
        // sample points before generating .wav output.
        internal static string InspectMsfWithCli(string inputPath, string overridePath)
        {
            string cli = ResolveExecutable(overridePath);
            EnsureCliWorks(cli);
            string input = Path.GetFullPath(inputPath);
            if (!File.Exists(input))
                throw new FileNotFoundException("Selected MSF could not be read.", input);
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = cli,
                Arguments = "-I \"" + input.Replace("\"", "") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            }))
            {
                if (process == null)
                    throw new IOException("Failed to start vgmstream-cli.");
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    try { process.Kill(); } catch { /* process has exited */ }
                    throw new TimeoutException("vgmstream-cli -I metadata request timed out.");
                }
                if (process.ExitCode != 0)
                    throw new InvalidDataException(
                        "vgmstream-cli did not recognize the selected MSF (exit " +
                        process.ExitCode + ").");
                string json = output.GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidDataException("vgmstream-cli returned empty metadata.");
                return json.Length > 100000 ? json.Substring(0, 100000) : json;
            }
        }

        [MenuItem("Tools/EMD2Unity/MSF/Validate Configured vgmstream-cli")]
        internal static void ValidateConfigured()
        {
            string exe = ResolveExecutable("");
            try
            {
                EnsureCliWorks(exe);
                UnityEngine.Debug.Log("EMD2Unity: vgmstream-cli verified: " + exe);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("EMD2Unity: decoder validation failed: " +
                    ex.Message);
            }
        }
    }
}
#endif
