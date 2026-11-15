// EMD2Unity: read-only Sony MSF (MultiStream File) audio decoder.
// Reference format: vgmstream src/meta/msf.c (MSF header, codec IDs).
// The source .msf is never changed.
#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal struct MsfHeader
    {
        internal byte Version;
        internal int Codec;
        internal int Channels;
        internal int SampleRate;
        internal int DataOffset;
        internal int DataSize;
        internal uint Flags;
        internal uint LoopStartBytes;
        internal uint LoopLengthBytes;

        internal bool Loops => Flags != 0xFFFFFFFFu && (Flags & 0x03u) != 0;
        internal string CodecName
        {
            get
            {
                switch (Codec)
                {
                    case 0: return "PCM16 Big Endian";
                    case 1: return "PCM16 Little Endian";
                    case 2: return "PCM32 Float (unsupported)";
                    case 3: return "PlayStation ADPCM";
                    case 4: return "ATRAC3 Low";
                    case 5: return "ATRAC3 Medium";
                    case 6: return "ATRAC3 High";
                    case 7: return "MPEG/MP3";
                    default: return "Unknown codec " + Codec;
                }
            }
        }
    }

    internal sealed class MsfAudioData
    {
        internal float[] Interleaved;
        internal int Channels;
        internal int SampleRate;
        internal int Frames => Interleaved.Length / Channels;
        internal string Decoder;
    }

    internal static class MsfAudioDecoder
    {
        private const int HeaderLength = 0x40;
        // Avoid decoding very large assets into unbounded managed float buffers.
        internal const int MaxInterleavedSamples = 30000000;
        private static readonly int[] AdpcmCoeff1 = { 0, 60, 115, 98, 122 };
        private static readonly int[] AdpcmCoeff2 = { 0, 0, -52, -55, -60 };

        private static uint Be32(byte[] bytes, int at)
        {
            return ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) |
                ((uint)bytes[at + 2] << 8) | bytes[at + 3];
        }

        private static ushort Le16(byte[] bytes, int at)
        {
            return (ushort)(bytes[at] | bytes[at + 1] << 8);
        }

        private static uint Le32(byte[] bytes, int at)
        {
            return (uint)(bytes[at] | bytes[at + 1] << 8 |
                bytes[at + 2] << 16 | bytes[at + 3] << 24);
        }

        internal static MsfHeader ParseHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderLength)
                throw new InvalidDataException("MSF is smaller than the 0x40-byte header.");
            if (bytes[0] != (byte)'M' || bytes[1] != (byte)'S' ||
                bytes[2] != (byte)'F')
                throw new InvalidDataException("Not a Sony MSF file (missing MSF magic).");

            uint codec = Be32(bytes, 0x04);
            uint channels = Be32(bytes, 0x08);
            uint declaredSize = Be32(bytes, 0x0C);
            uint rate = Be32(bytes, 0x10);
            long available = bytes.LongLength - HeaderLength;
            long dataSize = declaredSize == uint.MaxValue ? available : declaredSize;
            if (dataSize <= 0 || dataSize > available || dataSize > int.MaxValue)
                throw new InvalidDataException("MSF payload size exceeds the file or is empty.");
            if (channels < 1 || channels > 8)
                throw new InvalidDataException("Invalid MSF channel count: " + channels);
            if (codec > int.MaxValue)
                throw new InvalidDataException("Invalid MSF codec id.");
            if (rate == 0) rate = 48000; // Seen on early MSFv1 files.
            if (rate == uint.MaxValue && codec >= 4 && codec <= 6)
                rate = 44100; // Some early ATRAC3 voice files.
            if (rate < 8000 || rate > 192000)
                throw new InvalidDataException("Invalid MSF sample rate: " + rate);

            return new MsfHeader
            {
                Version = bytes[3],
                Codec = (int)codec,
                Channels = (int)channels,
                SampleRate = (int)rate,
                DataOffset = HeaderLength,
                DataSize = (int)dataSize,
                Flags = Be32(bytes, 0x14),
                LoopStartBytes = Be32(bytes, 0x18),
                LoopLengthBytes = Be32(bytes, 0x1C)
            };
        }

        private static float[] Allocate(int frames, int channels)
        {
            long samples = (long)frames * channels;
            if (frames <= 0 || samples > MaxInterleavedSamples)
                throw new InvalidDataException(
                    "Decoded MSF is empty or exceeds the " +
                    MaxInterleavedSamples + " interleaved-sample safety limit.");
            return new float[(int)samples];
        }

        internal static MsfAudioData DecodeManaged(byte[] bytes, MsfHeader header)
        {
            if (header.Codec == 0 || header.Codec == 1)
            {
                if (header.DataSize % (2 * header.Channels) != 0)
                    throw new InvalidDataException("PCM16 payload is not frame-aligned.");
                int frames = header.DataSize / (2 * header.Channels);
                float[] pcm = Allocate(frames, header.Channels);
                bool bigEndian = header.Codec == 0;
                for (int n = 0; n < pcm.Length; n++)
                {
                    int at = header.DataOffset + n * 2;
                    int raw = bigEndian ? (bytes[at] << 8 | bytes[at + 1])
                        : (bytes[at] | bytes[at + 1] << 8);
                    pcm[n] = (short)raw / 32768f;
                }
                return new MsfAudioData
                {
                    Interleaved = pcm, Channels = header.Channels,
                    SampleRate = header.SampleRate, Decoder = "Managed PCM16"
                };
            }

            if (header.Codec == 3)
            {
                int groupBytes = 16 * header.Channels;
                if (header.DataSize % groupBytes != 0)
                    throw new InvalidDataException("PS ADPCM payload is not 0x10-block aligned.");
                int groups = header.DataSize / groupBytes;
                if (groups > int.MaxValue / 28)
                    throw new InvalidDataException("Too many PS ADPCM frames.");
                float[] pcm = Allocate(groups * 28, header.Channels);
                int[] previous1 = new int[header.Channels];
                int[] previous2 = new int[header.Channels];

                for (int group = 0; group < groups; group++)
                {
                    for (int channel = 0; channel < header.Channels; channel++)
                    {
                        int block = header.DataOffset + group * groupBytes + channel * 16;
                        int predictor = (bytes[block] >> 4) & 0x0F;
                        int shift = bytes[block] & 0x0F;
                        if (predictor > 4 || shift > 12)
                            throw new InvalidDataException(
                                "Unsupported PS ADPCM predictor/shift at block " + group);
                        for (int n = 0; n < 28; n++)
                        {
                            int packed = bytes[block + 2 + (n >> 1)];
                            int nibble = (n & 1) == 0 ? (packed & 0x0F)
                                : (packed >> 4);
                            if (nibble >= 8) nibble -= 16;
                            int sample = (nibble << 12) >> shift;
                            sample += (previous1[channel] * AdpcmCoeff1[predictor] +
                                previous2[channel] * AdpcmCoeff2[predictor] + 32) >> 6;
                            sample = Math.Max(-32768, Math.Min(32767, sample));
                            previous2[channel] = previous1[channel];
                            previous1[channel] = sample;
                            pcm[(group * 28 + n) * header.Channels + channel] =
                                sample / 32768f;
                        }
                    }
                }
                return new MsfAudioData
                {
                    Interleaved = pcm, Channels = header.Channels,
                    SampleRate = header.SampleRate, Decoder = "Managed PS ADPCM"
                };
            }

            throw new NotSupportedException(
                "MSF codec " + header.CodecName +
                " requires vgmstream-cli. Configure its executable path in the MSF Inspector.");
        }

        // vgmstream-cli is a user-installed, optional decoder for ATRAC3/MPEG.
        // No executable is downloaded or run except when the user enables it.
        internal static MsfAudioData DecodeVgmstream(string sourceAssetPath, string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
                throw new InvalidOperationException("No vgmstream-cli path configured.");
            string fullInput = Path.GetFullPath(sourceAssetPath);
            if (!File.Exists(fullInput))
                throw new FileNotFoundException("MSF input file not found.", fullInput);
            string output = Path.Combine(Path.GetTempPath(),
                "emd2unity_msf_" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "-i -W 1 -o \"" + output + "\" \"" + fullInput + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                using (Process process = Process.Start(start))
                {
                    if (process == null)
                        throw new IOException("Unable to start vgmstream-cli.");
                    if (!process.WaitForExit(60000))
                    {
                        try { process.Kill(); } catch { /* already exited */ }
                        throw new TimeoutException("vgmstream-cli exceeded 60 seconds.");
                    }
                    if (process.ExitCode != 0 || !File.Exists(output))
                        throw new InvalidDataException(
                            "vgmstream-cli returned " + process.ExitCode +
                            ". Check that the executable includes required codec libraries.");
                }
                MsfAudioData result = DecodeWav(File.ReadAllBytes(output));
                result.Decoder = "vgmstream-cli";
                return result;
            }
            finally
            {
                try { if (File.Exists(output)) File.Delete(output); }
                catch (IOException) { /* temporary WAV cleanup */ }
            }
        }

        internal static MsfAudioData DecodeWav(byte[] wav)
        {
            if (wav == null || wav.Length < 44 ||
                wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F' ||
                wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
                throw new InvalidDataException("Decoder did not output a RIFF/WAVE file.");
            int pos = 12, channels = 0, sampleRate = 0, bits = 0, format = 0;
            int dataOffset = -1, dataSize = 0;
            while (pos <= wav.Length - 8)
            {
                uint chunkSize = Le32(wav, pos + 4);
                long next = (long)pos + 8 + chunkSize + (chunkSize & 1);
                if (next > wav.Length || next <= pos)
                    throw new InvalidDataException("Invalid WAV chunk length.");
                if (wav[pos] == 'f' && wav[pos + 1] == 'm' &&
                    wav[pos + 2] == 't' && wav[pos + 3] == ' ')
                {
                    if (chunkSize < 16) throw new InvalidDataException("WAV fmt chunk too short.");
                    format = Le16(wav, pos + 8);
                    channels = Le16(wav, pos + 10);
                    sampleRate = (int)Le32(wav, pos + 12);
                    bits = Le16(wav, pos + 22);
                }
                if (wav[pos] == 'd' && wav[pos + 1] == 'a' &&
                    wav[pos + 2] == 't' && wav[pos + 3] == 'a')
                {
                    dataOffset = pos + 8;
                    dataSize = (int)chunkSize;
                }
                pos = (int)next;
            }
            if (channels < 1 || channels > 8 ||
                sampleRate < 8000 || sampleRate > 192000 ||
                (format != 1 && format != 3))
                throw new InvalidDataException("Unsupported WAV channel, rate or format.");
            if (dataOffset < 0 || dataSize < 1 || dataOffset + (long)dataSize > wav.Length)
                throw new InvalidDataException("No valid WAV data chunk.");
            int bytesPerSample = bits / 8;
            if ((format == 1 && bits != 8 && bits != 16 && bits != 24 && bits != 32) ||
                (format == 3 && bits != 32))
                throw new InvalidDataException("Unsupported WAV sample format: " + bits);
            if (dataSize % (bytesPerSample * channels) != 0)
                throw new InvalidDataException("WAV samples are not frame-aligned.");
            int frames = dataSize / (bytesPerSample * channels);
            float[] pcm = Allocate(frames, channels);
            for (int n = 0; n < pcm.Length; n++)
            {
                int at = dataOffset + n * bytesPerSample;
                if (format == 3)
                {
                    float sample = BitConverter.ToSingle(wav, at);
                    pcm[n] = float.IsNaN(sample) || float.IsInfinity(sample) ? 0f
                        : Mathf.Clamp(sample, -1f, 1f);
                }
                else if (bits == 8) pcm[n] = (wav[at] - 128) / 128f;
                else if (bits == 16) pcm[n] = (short)Le16(wav, at) / 32768f;
                else if (bits == 24)
                {
                    int value = wav[at] | wav[at + 1] << 8 | wav[at + 2] << 16;
                    if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                    pcm[n] = value / 8388608f;
                }
                else pcm[n] = unchecked((int)Le32(wav, at)) / 2147483648f;
            }
            return new MsfAudioData
            {
                Interleaved = pcm, Channels = channels,
                SampleRate = sampleRate, Decoder = "WAV PCM"
            };
        }

        internal static byte[] EncodeWav16(MsfAudioData data)
        {
            long totalSamples = data.Interleaved.LongLength;
            if (totalSamples > (int.MaxValue - 44) / 2)
                throw new InvalidDataException("WAV exceeds 2 GB.");
            int dataBytes = checked((int)totalSamples * 2);
            using (var ms = new MemoryStream(dataBytes + 44))
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(dataBytes + 36);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16); // PCM fmt length
                writer.Write((ushort)1);
                writer.Write((ushort)data.Channels);
                writer.Write(data.SampleRate);
                writer.Write(data.SampleRate * data.Channels * 2);
                writer.Write((ushort)(data.Channels * 2));
                writer.Write((ushort)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(dataBytes);
                foreach (float sample in data.Interleaved)
                {
                    int value = (int)Math.Round(Math.Max(-1f, Math.Min(1f, sample)) *
                        (sample < 0 ? 32768f : 32767f));
                    writer.Write((short)value);
                }
                writer.Flush();
                return ms.ToArray();
            }
        }
    }
}
#endif
