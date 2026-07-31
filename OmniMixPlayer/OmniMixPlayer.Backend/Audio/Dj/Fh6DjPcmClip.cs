using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    public sealed class Fh6DjPcmClip
    {
        private readonly float[] _samples;

        public int SampleRate { get; }
        public int Channels { get; }
        public long TotalFrames => _samples.LongLength / Channels;

        public Fh6DjPcmClip(float[] interleavedSamples, int sampleRate, int channels)
        {
            ArgumentNullException.ThrowIfNull(interleavedSamples);
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0)
                throw new ArgumentOutOfRangeException(nameof(channels));
            if (interleavedSamples.Length == 0 || interleavedSamples.Length % channels != 0)
                throw new ArgumentException("DJ PCM must contain complete interleaved frames.", nameof(interleavedSamples));

            _samples = interleavedSamples;
            SampleRate = sampleRate;
            Channels = channels;
        }

        public static Fh6DjPcmClip LoadWave(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            if (ReadFourCc(reader) != "RIFF")
                throw new InvalidDataException("DJ clip is not a RIFF file.");
            _ = reader.ReadUInt32();
            if (ReadFourCc(reader) != "WAVE")
                throw new InvalidDataException("DJ clip is not a WAVE file.");

            WaveFormat format = null;
            byte[] audioData = null;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadFourCc(reader);
                var chunkLength = reader.ReadUInt32();
                var chunkEnd = checked(stream.Position + chunkLength);
                if (chunkEnd > stream.Length)
                    throw new InvalidDataException($"DJ wave chunk {chunkId} is truncated.");

                if (chunkId == "fmt ")
                    format = ReadFormat(reader, chunkLength);
                else if (chunkId == "data")
                {
                    if (chunkLength > int.MaxValue)
                        throw new InvalidDataException("DJ wave data is too large to premix in memory.");
                    audioData = reader.ReadBytes((int)chunkLength);
                    if (audioData.Length != chunkLength)
                        throw new EndOfStreamException("DJ wave data is truncated.");
                }

                stream.Position = chunkEnd + (chunkLength & 1);
            }

            if (format == null || audioData == null)
                throw new InvalidDataException("DJ wave file is missing fmt or data chunks.");

            return new Fh6DjPcmClip(
                DecodeSamples(audioData, format),
                format.SampleRate,
                format.Channels);
        }

        internal float SampleAt(double framePosition, int outputChannel, int outputChannels)
        {
            if (framePosition < 0 || framePosition >= TotalFrames)
                return 0f;

            var firstFrame = (long)framePosition;
            var secondFrame = Math.Min(firstFrame + 1, TotalFrames - 1);
            var fraction = (float)(framePosition - firstFrame);
            var first = ReadMappedSample(firstFrame, outputChannel, outputChannels);
            var second = ReadMappedSample(secondFrame, outputChannel, outputChannels);
            return first + (second - first) * fraction;
        }

        private float ReadMappedSample(long frame, int outputChannel, int outputChannels)
        {
            var sampleBase = checked((int)(frame * Channels));
            if (Channels == 1)
                return _samples[sampleBase];
            if (outputChannels == 1)
            {
                double sum = 0;
                for (var channel = 0; channel < Channels; channel++)
                    sum += _samples[sampleBase + channel];
                return (float)(sum / Channels);
            }

            return _samples[sampleBase + Math.Min(outputChannel, Channels - 1)];
        }

        private static WaveFormat ReadFormat(BinaryReader reader, uint chunkLength)
        {
            if (chunkLength < 16)
                throw new InvalidDataException("DJ wave fmt chunk is too short.");

            var formatTag = reader.ReadUInt16();
            var channels = reader.ReadUInt16();
            var sampleRate = reader.ReadInt32();
            _ = reader.ReadUInt32();
            var blockAlign = reader.ReadUInt16();
            var bitsPerSample = reader.ReadUInt16();

            if (formatTag is not 1 and not 3)
                throw new InvalidDataException($"DJ wave format {formatTag} is not PCM or IEEE float.");
            if (channels == 0 || sampleRate <= 0)
                throw new InvalidDataException("DJ wave format has invalid channels or sample rate.");

            var bytesPerSample = bitsPerSample / 8;
            if (bytesPerSample == 0 || blockAlign != channels * bytesPerSample)
                throw new InvalidDataException("DJ wave block alignment is invalid.");

            return new WaveFormat(formatTag, channels, sampleRate, bitsPerSample, blockAlign);
        }

        private static float[] DecodeSamples(byte[] data, WaveFormat format)
        {
            if (data.Length % format.BlockAlign != 0)
                throw new InvalidDataException("DJ wave data does not contain complete frames.");

            var bytesPerSample = format.BitsPerSample / 8;
            var result = new float[data.Length / bytesPerSample];
            for (var index = 0; index < result.Length; index++)
            {
                var offset = index * bytesPerSample;
                result[index] = (format.FormatTag, format.BitsPerSample) switch
                {
                    (1, 8) => (data[offset] - 128) / 128f,
                    (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2)) / 32768f,
                    (1, 24) => ReadInt24(data, offset) / 8388608f,
                    (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)) / 2147483648f,
                    (3, 32) => BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4))),
                    _ => throw new InvalidDataException(
                        $"DJ wave sample format {format.FormatTag}/{format.BitsPerSample} is unsupported.")
                };
            }

            return result;
        }

        private static int ReadInt24(byte[] data, int offset)
        {
            var value = data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16;
            return (value & 0x800000) != 0 ? value | unchecked((int)0xff000000) : value;
        }

        private static string ReadFourCc(BinaryReader reader)
            => Encoding.ASCII.GetString(reader.ReadBytes(4));

        private sealed record WaveFormat(
            ushort FormatTag,
            int Channels,
            int SampleRate,
            int BitsPerSample,
            int BlockAlign);
    }
}
