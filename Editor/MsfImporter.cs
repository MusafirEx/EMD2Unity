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
    [ScriptedImporter(1, "msf")]
    public sealed class MsfImporter : ScriptedImporter
    {
        [SerializeField] private bool preferVgmstream = true;
        [SerializeField] private string vgmstreamCliPath = "";

        internal bool PreferVgmstream => preferVgmstream;
        internal string VgmstreamCliPath => vgmstreamCliPath;

        private MsfAudioData Decode(byte[] source, MsfHeader header,
            Action<string> warn)
        {
            if (preferVgmstream && !string.IsNullOrWhiteSpace(vgmstreamCliPath))
            {
                try
                {
                    return MsfAudioDecoder.DecodeVgmstream(assetPath, vgmstreamCliPath.Trim());
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
                info.AppendLine("For ATRAC3/MPEG, configure a local vgmstream-cli executable " +
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
                "No executable is bundled or downloaded. The original MSF is unchanged.",
                MessageType.Info);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("preferVgmstream"),
                new GUIContent("Prefer vgmstream-cli (when configured)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("vgmstreamCliPath"),
                new GUIContent("vgmstream-cli Executable",
                    "Absolute path to vgmstream-cli.exe, or a command on PATH. " +
                    "Set this per asset to keep imports reproducible."));
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
