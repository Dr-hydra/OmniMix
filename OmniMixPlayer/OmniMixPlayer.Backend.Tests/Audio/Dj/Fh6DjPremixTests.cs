using System;
using OmniMixPlayer.Backend.Audio.Dj;
using OmniMixPlayer.SDK.Interfaces;
using Xunit;

namespace OmniMixPlayer.Backend.Tests.Audio.Dj
{
    public sealed class Fh6DjPremixTests
    {
        [Fact]
        public void IntroAnalyzer_FindsStableMusicAfterSilence()
        {
            const int sampleRate = 1_000;
            var pcm = new float[3_000];
            Array.Fill(pcm, 0.2f, 1_000, 2_000);

            var result = Fh6SongIntroAnalyzer.Analyze(pcm, sampleRate, channels: 1);

            Assert.InRange(result.FirstAudibleFrame, 960, 1_040);
            Assert.InRange(result.StableEntryFrame, 960, 1_040);
            Assert.False(result.UsedFallback);
            Assert.True(result.Confidence >= 0.65f);
        }

        [Fact]
        public void MixPlanner_AlignsStableEntryBeforeDjEnd()
        {
            var intro = new Fh6SongIntroAnalysis(24_000, 48_000, 1f, false);

            var plan = Fh6DjMixPlanner.Create(
                intro,
                songSampleRate: 48_000,
                djSourceFrames: 480_000,
                djSampleRate: 48_000,
                new Fh6DjMixPlanningOptions { MusicArrivalBeforeDjEndMilliseconds = 1_000 });

            Assert.Equal(384_000, plan.SongTimelineStartFrame);
            Assert.Equal((ulong)0, plan.SongSourceStartFrame);
            Assert.Equal(432_000, plan.SongTimelineStartFrame + intro.StableEntryFrame);
        }

        [Fact]
        public void PreviewAnalyzer_RestoresQueuedReaderPosition()
        {
            var reader = new ArrayPcmReader(sampleRate: 1_000, channels: 1, frames: 3_000, value: 0.2f);
            Assert.True(reader.Seek(321));

            var result = Fh6SongIntroPreviewAnalyzer.Analyze(reader);

            Assert.Equal((ulong)321, reader.CurrentFrame);
            Assert.Equal(0, result.FirstAudibleFrame);
            Assert.False(result.UsedFallback);
        }

        [Fact]
        public void PremixReader_MixesDucksAndSeeksOnCompositeTimeline()
        {
            var song = new ArrayPcmReader(sampleRate: 10, channels: 1, frames: 100, value: 1f);
            var dj = new Fh6DjPcmClip(CreateSamples(20, 0.5f), sampleRate: 10, channels: 1);
            var plan = new Fh6DjMixPlan(
                SongTimelineStartFrame: 10,
                SongSourceStartFrame: 0,
                SongFadeInFrames: 0,
                DjEndFrame: 20,
                DuckReleaseFrames: 10,
                DjFadeOutFrames: 0,
                DuckedSongGain: 0.2f,
                DjGain: 1f);
            using var reader = new Fh6DjPremixPcmStreamReader(song, dj, plan);
            var buffer = new float[20];

            Assert.Equal(10d, reader.SongDurationSeconds);
            Assert.Equal(-1d, reader.MapTimelineFrameToSongSeconds(0));
            Assert.Equal(0d, reader.MapTimelineFrameToSongSeconds(10));
            Assert.Equal(25UL, reader.MapSongSecondsToTimelineFrame(1.5d));

            var read = reader.ReadFrames(buffer, 20);

            Assert.Equal(20, read);
            Assert.All(buffer[..10], value => Assert.Equal(0.5f, value, precision: 4));
            Assert.All(buffer[10..], value => Assert.Equal(0.7f, value, precision: 4));
            Assert.Equal((ulong)20, reader.CurrentFrame);
            Assert.Equal(1d, reader.MapTimelineFrameToSongSeconds(reader.CurrentFrame));

            Assert.True(reader.Seek(25));
            Assert.Equal((ulong)15, song.CurrentFrame);
            Assert.Equal((ulong)25, reader.CurrentFrame);

            var oneFrame = new float[1];
            Assert.Equal(1, reader.ReadFrames(oneFrame, 1));
            Assert.Equal(0.6f, oneFrame[0], precision: 4);
        }

        [Fact]
        public void PremixReader_MapsSkippedSongIntroToOriginalTimeline()
        {
            var song = new ArrayPcmReader(sampleRate: 10, channels: 1, frames: 100, value: 1f);
            var dj = new Fh6DjPcmClip(CreateSamples(20, 0.5f), sampleRate: 10, channels: 1);
            var plan = new Fh6DjMixPlan(
                SongTimelineStartFrame: 0,
                SongSourceStartFrame: 5,
                SongFadeInFrames: 0,
                DjEndFrame: 20,
                DuckReleaseFrames: 10,
                DjFadeOutFrames: 0,
                DuckedSongGain: 0.2f,
                DjGain: 1f);
            using var reader = new Fh6DjPremixPcmStreamReader(song, dj, plan);

            Assert.Equal(0.5d, reader.MapTimelineFrameToSongSeconds(0));
            Assert.Equal(0UL, reader.MapSongSecondsToTimelineFrame(0.5d));
            Assert.Equal(10UL, reader.MapSongSecondsToTimelineFrame(1.5d));
        }

        private static float[] CreateSamples(int count, float value)
        {
            var result = new float[count];
            Array.Fill(result, value);
            return result;
        }

        private sealed class ArrayPcmReader : IPcmStreamReader
        {
            private readonly float[] _samples;
            private bool _disposed;

            public PcmStreamInfo Info { get; }
            public ulong CurrentFrame { get; private set; }
            public bool IsEndOfStream => CurrentFrame >= Info.TotalFrames;
            public bool IsReady => !_disposed;
            public bool CanSeek => !_disposed;
            public bool HasPendingSeek => false;
            public long PendingSeekFrame => -1;
            public double CacheProgress => 100;
            public bool IsCacheComplete => true;

            public ArrayPcmReader(int sampleRate, int channels, int frames, float value)
            {
                Info = new PcmStreamInfo
                {
                    SampleRate = sampleRate,
                    Channels = channels,
                    TotalFrames = (ulong)frames,
                    Format = "test",
                    CanSeek = true
                };
                _samples = CreateSamples(frames * channels, value);
            }

            public long ReadFrames(float[] buffer, int framesToRead)
            {
                var frames = (int)Math.Min((ulong)framesToRead, Info.TotalFrames - CurrentFrame);
                Array.Copy(
                    _samples,
                    checked((int)CurrentFrame * Info.Channels),
                    buffer,
                    0,
                    frames * Info.Channels);
                CurrentFrame += (ulong)frames;
                return frames;
            }

            public bool Seek(ulong frameIndex)
            {
                CurrentFrame = Math.Min(frameIndex, Info.TotalFrames);
                return true;
            }

            public void CancelPendingSeek() { }
            public void Dispose() => _disposed = true;
        }
    }
}
