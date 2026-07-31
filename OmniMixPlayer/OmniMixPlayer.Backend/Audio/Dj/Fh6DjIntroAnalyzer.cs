using System;
using System.IO;
using System.Threading;
using OmniMixPlayer.SDK.Interfaces;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    public sealed class Fh6SongIntroAnalysisOptions
    {
        public int WindowMilliseconds { get; init; } = 40;
        public float AudibleThresholdDb { get; init; } = -46f;
        public float StableThresholdDb { get; init; } = -34f;
        public int MinimumStableMilliseconds { get; init; } = 320;
        public int MaximumSearchMilliseconds { get; init; } = 20_000;
        public int FallbackEntryMilliseconds { get; init; } = 750;
    }

    public sealed record Fh6SongIntroAnalysis(
        long FirstAudibleFrame,
        long StableEntryFrame,
        float Confidence,
        bool UsedFallback);

    public static class Fh6SongIntroAnalyzer
    {
        public static Fh6SongIntroAnalysis Analyze(
            ReadOnlySpan<float> interleavedPcm,
            int sampleRate,
            int channels,
            Fh6SongIntroAnalysisOptions options = null)
        {
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0)
                throw new ArgumentOutOfRangeException(nameof(channels));
            if (interleavedPcm.Length % channels != 0)
                throw new ArgumentException("PCM sample count must be divisible by the channel count.", nameof(interleavedPcm));

            options ??= new Fh6SongIntroAnalysisOptions();
            var totalFrames = interleavedPcm.Length / channels;
            var windowFrames = Math.Max(1, sampleRate * options.WindowMilliseconds / 1000);
            var maxFrames = Math.Min(totalFrames, sampleRate * options.MaximumSearchMilliseconds / 1000);
            var stableWindowsRequired = Math.Max(
                1,
                (int)Math.Ceiling((double)options.MinimumStableMilliseconds / options.WindowMilliseconds));

            long firstAudible = -1;
            long stableEntry = -1;
            var stableRun = 0;
            var stableRunStart = 0;
            var windowsAboveAudible = 0;
            var windowsExamined = 0;

            for (var startFrame = 0; startFrame < maxFrames; startFrame += windowFrames)
            {
                var frames = Math.Min(windowFrames, maxFrames - startFrame);
                var db = CalculateRmsDb(interleavedPcm, startFrame, frames, channels);
                windowsExamined++;

                if (db >= options.AudibleThresholdDb)
                {
                    windowsAboveAudible++;
                    if (firstAudible < 0)
                        firstAudible = startFrame;
                }

                if (db >= options.StableThresholdDb)
                {
                    if (stableRun == 0)
                        stableRunStart = startFrame;
                    stableRun++;
                    if (stableRun >= stableWindowsRequired)
                    {
                        stableEntry = stableRunStart;
                        break;
                    }
                }
                else
                {
                    stableRun = 0;
                }
            }

            if (firstAudible < 0)
            {
                var fallback = Math.Min(
                    totalFrames,
                    (long)sampleRate * options.FallbackEntryMilliseconds / 1000);
                return new Fh6SongIntroAnalysis(0, fallback, 0f, UsedFallback: true);
            }

            if (stableEntry < 0)
            {
                var fallback = Math.Min(
                    totalFrames,
                    firstAudible + (long)sampleRate * options.FallbackEntryMilliseconds / 1000);
                var weakConfidence = windowsExamined == 0
                    ? 0f
                    : Math.Clamp((float)windowsAboveAudible / windowsExamined, 0.1f, 0.45f);
                return new Fh6SongIntroAnalysis(firstAudible, fallback, weakConfidence, UsedFallback: true);
            }

            var leadDuration = Math.Max(1, stableEntry - firstAudible + windowFrames);
            var confidence = Math.Clamp(
                0.65f + 0.35f * Math.Min(1f, (float)(stableWindowsRequired * windowFrames) / leadDuration),
                0f,
                1f);
            return new Fh6SongIntroAnalysis(firstAudible, stableEntry, confidence, UsedFallback: false);
        }

        private static float CalculateRmsDb(
            ReadOnlySpan<float> pcm,
            int startFrame,
            int frames,
            int channels)
        {
            double sumSquares = 0;
            var samples = frames * channels;
            var startSample = startFrame * channels;
            for (var index = 0; index < samples; index++)
            {
                var sample = pcm[startSample + index];
                sumSquares += sample * sample;
            }

            if (sumSquares <= double.Epsilon)
                return -120f;

            var rms = Math.Sqrt(sumSquares / samples);
            return (float)(20.0 * Math.Log10(Math.Max(rms, 1e-6)));
        }
    }

    public static class Fh6SongIntroPreviewAnalyzer
    {
        /// <summary>
        /// Decodes a bounded opening preview and restores the reader position. Run
        /// this for the queued next track, never inside the active playback loop.
        /// </summary>
        public static Fh6SongIntroAnalysis Analyze(
            IPcmStreamReader reader,
            Fh6SongIntroAnalysisOptions options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            if (!reader.IsReady || reader.Info.SampleRate <= 0 || reader.Info.Channels <= 0)
                throw new InvalidOperationException("The song reader must be format-ready before intro analysis.");
            if (!reader.CanSeek)
                throw new InvalidOperationException("Intro analysis requires a seekable queued-track reader.");

            options ??= new Fh6SongIntroAnalysisOptions();
            var originalFrame = reader.CurrentFrame;
            var maximumFrames = checked(reader.Info.SampleRate * options.MaximumSearchMilliseconds / 1000);
            if (reader.Info.TotalFrames > 0)
                maximumFrames = (int)Math.Min((ulong)maximumFrames, reader.Info.TotalFrames);
            if (maximumFrames <= 0)
                throw new InvalidDataException("The song reader has no frames available for intro analysis.");

            var channels = reader.Info.Channels;
            var preview = new float[checked(maximumFrames * channels)];
            var chunkFrames = Math.Min(4096, maximumFrames);
            var chunk = new float[checked(chunkFrames * channels)];
            var framesRead = 0;

            try
            {
                if (!reader.Seek(0))
                    throw new InvalidOperationException("The song reader could not seek to its beginning for analysis.");

                while (framesRead < maximumFrames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var requested = Math.Min(chunkFrames, maximumFrames - framesRead);
                    var read = reader.ReadFrames(chunk, requested);
                    if (read < 0)
                        throw new InvalidDataException("The song reader failed while decoding the intro preview.");
                    if (read == 0)
                        break;

                    var samplesRead = checked((int)read * channels);
                    Array.Copy(chunk, 0, preview, framesRead * channels, samplesRead);
                    framesRead += checked((int)read);
                }
            }
            finally
            {
                if (!reader.Seek(originalFrame))
                    throw new InvalidOperationException("The song reader position could not be restored after analysis.");
            }

            if (framesRead == 0)
                throw new InvalidDataException("No decoded song frames were available for intro analysis.");

            return Fh6SongIntroAnalyzer.Analyze(
                preview.AsSpan(0, framesRead * channels),
                reader.Info.SampleRate,
                channels,
                options);
        }
    }

    public sealed class Fh6DjMixPlanningOptions
    {
        public int MusicArrivalBeforeDjEndMilliseconds { get; init; } = 900;
        public int SongFadeInMilliseconds { get; init; } = 1_200;
        public int DuckReleaseMilliseconds { get; init; } = 1_500;
        public int DjFadeOutMilliseconds { get; init; } = 80;
        public float DuckedSongGain { get; init; } = 0.24f;
        public float DjGain { get; init; } = 1f;
    }

    public sealed record Fh6DjMixPlan(
        long SongTimelineStartFrame,
        ulong SongSourceStartFrame,
        long SongFadeInFrames,
        long DjEndFrame,
        long DuckReleaseFrames,
        long DjFadeOutFrames,
        float DuckedSongGain,
        float DjGain);

    public static class Fh6DjMixPlanner
    {
        public static Fh6DjMixPlan Create(
            Fh6SongIntroAnalysis songIntro,
            int songSampleRate,
            long djSourceFrames,
            int djSampleRate,
            Fh6DjMixPlanningOptions options = null)
        {
            ArgumentNullException.ThrowIfNull(songIntro);
            if (songSampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(songSampleRate));
            if (djSourceFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(djSourceFrames));
            if (djSampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(djSampleRate));

            options ??= new Fh6DjMixPlanningOptions();
            if (options.DuckedSongGain is < 0f or > 1f)
                throw new ArgumentOutOfRangeException(nameof(options), "Ducked song gain must be between 0 and 1.");
            if (options.DjGain < 0f)
                throw new ArgumentOutOfRangeException(nameof(options), "DJ gain cannot be negative.");

            var djEnd = ConvertFrames(djSourceFrames, djSampleRate, songSampleRate);
            var arrivalLead = MillisecondsToFrames(options.MusicArrivalBeforeDjEndMilliseconds, songSampleRate);
            var arrivalTarget = Math.Max(0, djEnd - arrivalLead);
            var stableEntry = Math.Max(0, songIntro.StableEntryFrame);

            long songTimelineStart;
            ulong songSourceStart;
            if (stableEntry <= arrivalTarget)
            {
                songTimelineStart = arrivalTarget - stableEntry;
                songSourceStart = 0;
            }
            else
            {
                songTimelineStart = 0;
                songSourceStart = checked((ulong)(stableEntry - arrivalTarget));
            }

            return new Fh6DjMixPlan(
                songTimelineStart,
                songSourceStart,
                MillisecondsToFrames(options.SongFadeInMilliseconds, songSampleRate),
                djEnd,
                MillisecondsToFrames(options.DuckReleaseMilliseconds, songSampleRate),
                MillisecondsToFrames(options.DjFadeOutMilliseconds, songSampleRate),
                options.DuckedSongGain,
                options.DjGain);
        }

        private static long ConvertFrames(long frames, int sourceRate, int destinationRate)
            => checked((long)Math.Ceiling((double)frames * destinationRate / sourceRate));

        private static long MillisecondsToFrames(int milliseconds, int sampleRate)
            => Math.Max(0, checked((long)milliseconds * sampleRate / 1000));
    }
}
