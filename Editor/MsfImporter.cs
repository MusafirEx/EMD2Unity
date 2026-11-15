// Unity 2022.3 Sony MSF importer: expandable .msf with AudioClip child.
// MSF originals are never modified. External vgmstream-cli is opt-in.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [ScriptedImporter(2, "msf")]
    public sealed class MsfImporter : ScriptedImporter
    {
        [SerializeField] private bool preferVgmstream = true;
        [SerializeField] private string vgmstreamCliPath = "";
        internal static string SharedCliPath => VgmstreamIntegration.SharedExecutable;

        internal bool PreferVgmstream => preferVgmstream;
        internal string VgmstreamCliPath => vgmstreamCliPath;
        internal string EffectiveCliPath => VgmstreamIntegration.ResolveExecutable(vgmstreamCliPath);

        private MsfAudioData Decode(byte[] source, MsfHeader header,
            Action<string> warn)
        {
            if (preferVgmstream && !string.IsNullOrWhiteSpace(EffectiveCliPath))
            {
                try
                {
                    return MsfAudioDecoder.DecodeVgmstream(assetPath, EffectiveCliPath);
                }
                catch (Exception ex)
                {
                    warn("vgmstream-cli failed (" + ex.Message +
                        "); trying built-in PCM/PS ADPCM decoding.");
                }
            }
            return MsfAudioDecoder.DecodeManaged(source, header);
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            var info = new StringBuilder();
            info.AppendLine("EMD2Unity MSF Audio Import");
            info.AppendLine("File: " + ctx.assetPath);
            bool clipCreated = false;
            try
            {
                byte[] source = File.ReadAllBytes(ctx.assetPath);
                MsfHeader header = MsfAudioDecoder.ParseHeader(source);
                info.AppendLine("MSF version: 0x" + header.Version.ToString("X2"));
                info.AppendLine("Codec: " + header.CodecName +
                    " (0x" + header.Codec.ToString("X2") + ")");
                info.AppendLine("Channels: " + header.Channels);
                info.AppendLine("Sample rate: " + header.SampleRate);
                info.AppendLine("Payload bytes: " + header.DataSize);
                info.AppendLine("Loop flag: " + header.Loops);
                info.AppendLine("Loop start bytes: " + header.LoopStartBytes);
                info.AppendLine("Loop length bytes: " + header.LoopLengthBytes);

                MsfAudioData decoded = Decode(source, header, warning =>
                    ctx.LogImportWarning(name + ": " + warning));
                if (decoded.Frames <= 0)
                    throw new InvalidDataException("Decoder returned an empty AudioClip.");

                // The imported AudioClip is a child of .msf, and the metadata
                // TextAsset is the main asset so the Project window is expandable.
                AudioClip clip = AudioClip.Create(name + "_Audio",
                    decoded.Frames, decoded.Channels, decoded.SampleRate, false);
                if (clip == null || !clip.SetData(decoded.Interleaved, 0))
                {
                    if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
                    throw new IOException("Unity could not store the decoded AudioClip samples.");
                }
                clip.hideFlags = HideFlags.None;
                ctx.AddObjectToAsset("audio_clip", clip);
                clipCreated = true;
                info.AppendLine("Decoder: " + decoded.Decoder);
                info.AppendLine("Decoded frames: " + decoded.Frames);
                info.AppendLine("Duration: " +
                    ((double)decoded.Frames / decoded.SampleRate).ToString("F3") + " seconds");
                info.AppendLine("AudioClip sub-asset: " + clip.name);
                info.AppendLine("Note: Source loop markers are informational; " +
                    "set AudioSource.loop when appropriate.");
            }
            catch (Exception ex)
            {
                // Keep metadata inspectable even when codec is not available.
                // In particular ATRAC3/MPEG require an optional decoder.
                string message = "MSF audio decode unavailable for " + ctx.assetPath +
                    ": " + ex.Message;
                ctx.LogImportWarning(message);
                info.AppendLine("AudioClip not generated: " + ex.Message);
                info.AppendLine("For ATRAC3/MPEG, click Install Official vgmstream-cli " +
                    "in the MSF Inspector, then Apply.");
            }

            TextAsset metadata = new TextAsset(info.ToString())
            {
                name = name + "_MSF_Info"
            };
            ctx.AddObjectToAsset("msf_info", metadata);
            ctx.SetMainObject(metadata);
            if (clipCreated)
                Debug.Log("EMD2Unity MSF: AudioClip child generated for " + ctx.assetPath);
        }

        internal string ExportWav()
        {
            byte[] source = File.ReadAllBytes(assetPath);
            MsfHeader header = MsfAudioDecoder.ParseHeader(source);
            MsfAudioData decoded = Decode(source, header, warning =>
                Debug.LogWarning("EMD2Unity MSF export: " + warning));

            byte[] wav = MsfAudioDecoder.EncodeWav16(decoded);
            string outputPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.ChangeExtension(assetPath, ".wav").Replace('\\', '/'));
            File.WriteAllBytes(outputPath, wav);
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("EMD2Unity MSF: exported native Unity WAV AudioClip: " + outputPath);
            return outputPath;
        }
    }

    internal static class MsfToolSettings
    {
        [MenuItem("Tools/EMD2Unity/MSF/Set Default vgmstream-cli Path")]
        private static void SetSharedDecoder()
        {
            string existing = MsfImporter.SharedCliPath;
            string folder = string.IsNullOrWhiteSpace(existing) ? "" :
                (Path.GetDirectoryName(existing) ?? "");
            string selected = EditorUtility.OpenFilePanel(
                "Select vgmstream-cli executable for all MSF importers",
                folder, Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
            if (string.IsNullOrWhiteSpace(selected)) return;
            EditorPrefs.SetString("MusafirEx.EMD2Unity.MSF.DefaultVgmstreamCli", selected);
            Debug.Log("EMD2Unity MSF: shared decoder configured. " +
                "Reimport affected .msf files to generate their AudioClip children.");
        }
    }

    // A read-only smoke test for the exact source .msf selected in Unity.
    // This verifies that Unity can find the imported AudioClip child AND
    // retrieve its actual PCM data after the import has completed.
    internal static class MsfAudioValidation
    {
        [MenuItem("Tools/EMD2Unity/MSF/Validate Selected MSF AudioClip")]
        private static void ValidateSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrWhiteSpace(path) ||
                !path.EndsWith(".msf", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("EMD2Unity MSF: select an .msf or its AudioClip child first.");
                return;
            }
            AudioClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AudioClip>().FirstOrDefault();
            if (clip == null)
            {
                Debug.LogError("EMD2Unity MSF: No AudioClip child at " + path +
                    ". Check the MSF Inspector codec and configure vgmstream-cli if needed.");
                return;
            }
            int sampleFrames = Math.Min(clip.samples, 2048);
            if (sampleFrames <= 0 || clip.channels <= 0)
            {
                Debug.LogError("EMD2Unity MSF: Imported AudioClip has zero samples: " + path);
                return;
            }
            var pcm = new float[sampleFrames * clip.channels];
            if (!clip.GetData(pcm, 0))
            {
                Debug.LogError("EMD2Unity MSF: AudioClip child exists but PCM GetData failed " +
                    "after import. Use Export Native WAV for a persistent Unity AudioClip: " + path);
                return;
            }

            // A silent clip is valid, but decode a matching managed source if
            // possible to detect an AudioClip that silently lost its PCM data.
            try
            {
                byte[] source = File.ReadAllBytes(path);
                MsfHeader header = MsfAudioDecoder.ParseHeader(source);
                if (header.Codec == 0 || header.Codec == 1 || header.Codec == 3)
                {
                    MsfAudioData reference = MsfAudioDecoder.DecodeManaged(source, header);
                    int n = Math.Min(pcm.Length, reference.Interleaved.Length);
                    for (int i = 0; i < n; i++)
                    {
                        if (Math.Abs(pcm[i] - reference.Interleaved[i]) > 0.0005f)
                        {
                            Debug.LogError("EMD2Unity MSF: AudioClip child lost or changed " +
                                "its decoded PCM data at sample " + i +
                                ". Export Native WAV instead: " + path);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("EMD2Unity MSF: reference verification skipped: " +
                    ex.Message);
            }
            Debug.Log("EMD2Unity MSF PASS: selected .msf has a readable AudioClip child: " +
                path + " | " + clip.samples + " frames, " + clip.channels +
                " channels, " + clip.frequency + " Hz.");
        }

        [MenuItem("Tools/EMD2Unity/MSF/Run Decoder Self-Test")]
        private static void TestManagedDecoders()
        {
            try
            {
                byte[] pcm = MakeHeader(1, 1, 8);
                // PCM16 little-endian: 0, +16384, -16384, -32768.
                byte[] pcmBytes = { 0, 0, 0, 64, 0, 192, 0, 128 };
                Array.Copy(pcmBytes, 0, pcm, 0x40, pcmBytes.Length);
                MsfHeader header = MsfAudioDecoder.ParseHeader(pcm);
                MsfAudioData decoded = MsfAudioDecoder.DecodeManaged(pcm, header);
                Assert(decoded.Frames == 4 && decoded.Channels == 1, "PCM frame count");
                Assert(Math.Abs(decoded.Interleaved[1] - 0.5f) < 0.0001f, "PCM positive");
                Assert(Math.Abs(decoded.Interleaved[2] + 0.5f) < 0.0001f, "PCM negative");
                Assert(Math.Abs(decoded.Interleaved[3] + 1f) < 0.0001f, "PCM min value");
                MsfAudioData wav = MsfAudioDecoder.DecodeWav(
                    MsfAudioDecoder.EncodeWav16(decoded));
                Assert(wav.Frames == 4 && wav.Channels == 1, "WAV roundtrip frames");
                Assert(Math.Abs(wav.Interleaved[1] - 0.5f) < 0.0001f, "WAV samples");

                byte[] psx = MakeHeader(3, 1, 16);
                psx[0x40] = 0x0C; // predictor zero / shift 12
                for (int i = 0x42; i < 0x50; i++) psx[i] = 0x11;
                MsfAudioData adpcm = MsfAudioDecoder.DecodeManaged(
                    psx, MsfAudioDecoder.ParseHeader(psx));
                Assert(adpcm.Frames == 28, "ADPCM 28 samples per 16-byte block");
                Assert(Math.Abs(adpcm.Interleaved[0] - 1f / 32768f) < 0.000001f,
                    "ADPCM signed nibble");
                Debug.Log("EMD2Unity MSF decoder self-test PASS: PCM16, " +
                    "PS ADPCM, and WAV roundtrip.");
            }
            catch (Exception ex)
            {
                Debug.LogError("EMD2Unity MSF decoder self-test FAILED: " + ex);
            }
        }

        private static void Assert(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("MSF test failed: " + label);
        }

        private static byte[] MakeHeader(int codec, int channels, int payload)
        {
            var bytes = new byte[0x40 + payload];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'S';
            bytes[2] = (byte)'F';
            bytes[3] = 0x43;
            PutBe32(bytes, 4, codec);
            PutBe32(bytes, 8, channels);
            PutBe32(bytes, 12, payload);
            PutBe32(bytes, 16, 48000);
            return bytes;
        }

        private static void PutBe32(byte[] bytes, int at, int value)
        {
            bytes[at] = (byte)(value >> 24);
            bytes[at + 1] = (byte)(value >> 16);
            bytes[at + 2] = (byte)(value >> 8);
            bytes[at + 3] = (byte)value;
        }
    }

    [CustomEditor(typeof(MsfImporter))]
    public sealed class MsfImporterInspector : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MsfImporter importer = target as MsfImporter;
            EditorGUILayout.LabelField("Sony MSF → Unity AudioClip", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Import a Sony PS3 MSF file as an expandable asset with an AudioClip " +
                "child. Managed decoding supports PCM16 BE/LE and PS ADPCM. " +
                "ATRAC3 and MPEG/MP3 need a separately installed vgmstream-cli. " +
                "No executable is bundled or downloaded during import. " +
                "Click Install Official vgmstream-cli to download the official " +
                "vgmstream/vgmstream release (opt-in), or set a manual executable. " +
                "The original MSF is unchanged.",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Official vgmstream Integration",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Official source: github.com/vgmstream/vgmstream | " +
                "Release: " + VgmstreamIntegration.ReleaseTag + ". " +
                "The decoder is installed in the project Library folder " +
                "only when you explicitly click Install. " +
                "MSF/ATRAC3/MPEG playback uses vgmstream-cli as an external " +
                "Editor process, not a managed reimplementation.",
                MessageType.Info);
            if (GUILayout.Button("Install / Update Official vgmstream-cli (" +
                VgmstreamIntegration.ReleaseTag + ")", GUILayout.Height(26)))
            {
                serializedObject.ApplyModifiedProperties();
                try
                {
                    string installed = VgmstreamIntegration.InstallFromOfficialRelease();
                    if (!string.IsNullOrWhiteSpace(installed))
                    {
                        EditorUtility.DisplayDialog("vgmstream Installed",
                            "Decoder installed and configured:\n" + installed +
                            "\n\nThe MSF will now be reimported to create the AudioClip child.",
                            "OK");
                        AssetDatabase.ImportAsset(importer.assetPath,
                            ImportAssetOptions.ForceUpdate);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    EditorUtility.DisplayDialog("vgmstream Install Failed",
                        ex.Message, "OK");
                }
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("Validate vgmstream-cli"))
                VgmstreamIntegration.ValidateConfigured();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("preferVgmstream"),
                new GUIContent("Prefer vgmstream-cli (when configured)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("vgmstreamCliPath"),
                new GUIContent("vgmstream-cli Executable (optional override)",
                    "Leave empty to use the shared or officially installed decoder. " +
                    "Set an absolute path to override this one MSF."));
            if (importer != null)
                EditorGUILayout.LabelField("Effective Decoder Path",
                    string.IsNullOrWhiteSpace(importer.EffectiveCliPath) ?
                    "(not configured)" : importer.EffectiveCliPath);
            if (GUILayout.Button("Browse vgmstream-cli..."))
            {
                string current = serializedObject.FindProperty("vgmstreamCliPath").stringValue;
                string selected = EditorUtility.OpenFilePanel(
                    "Select vgmstream-cli executable", Path.GetDirectoryName(current) ?? "",
                    Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                if (!string.IsNullOrEmpty(selected))
                    serializedObject.FindProperty("vgmstreamCliPath").stringValue = selected;
            }

            if (importer != null)
            {
                EditorGUILayout.Space();
                string path = importer.assetPath;
                TextAsset info = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<TextAsset>().FirstOrDefault();
                if (info != null)
                {
                    EditorGUILayout.LabelField("MSF Header and Decode Status",
                        EditorStyles.boldLabel);
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.TextArea(info.text, GUILayout.MinHeight(126f));
                }

                AudioClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AudioClip>().FirstOrDefault();
                EditorGUILayout.ObjectField("Generated AudioClip Child", clip,
                    typeof(AudioClip), false);
                if (clip != null)
                {
                    EditorGUILayout.LabelField("Clip Duration", clip.length.ToString("F3") + " s");
                    EditorGUILayout.LabelField("Clip Channels", clip.channels.ToString());
                    EditorGUILayout.LabelField("Clip Sample Rate", clip.frequency.ToString());
                    if (GUILayout.Button("Select AudioClip Child"))
                    {
                        Selection.activeObject = clip;
                        EditorGUIUtility.PingObject(clip);
                    }
                }

                EditorGUILayout.HelpBox(
                    "If an AudioClip created by ScriptedImporter loses its samples " +
                    "after Unity reloads (a known issue on some Unity versions), " +
                    "click Export WAV. Unity will import that .wav through its native " +
                    "audio importer as a persistent AudioClip.",
                    MessageType.Warning);
                if (GUILayout.Button("Export Native WAV (next to MSF)"))
                {
                    serializedObject.ApplyModifiedProperties();
                    try
                    {
                        string output = importer.ExportWav();
                        UnityEngine.Object asset =
                            AssetDatabase.LoadAssetAtPath<AudioClip>(output);
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        EditorUtility.DisplayDialog("MSF Export Failed",
                            ex.Message, "OK");
                    }
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space();
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
#endif
