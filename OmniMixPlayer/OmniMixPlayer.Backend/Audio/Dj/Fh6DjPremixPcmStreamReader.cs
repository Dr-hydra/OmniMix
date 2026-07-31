using System;
using OmniMixPlayer.SDK.Interfaces;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    /// <summary>
    /// Read-driven composite timeline. Pausing the caller pauses both sources because
    /// this reader has no independent clock or worker. Seeking is expressed in the
    /// composite timeline and translated to the wrapped song reader.
    /// </summary>
    public sealed class Fh6DjPremixPcmStreamReader : IPcmStreamReader
    {
        private readonly object _sync = new();
        private readonly IPcmStreamReader _song;
        private readonly Fh6DjPcmClip _dj;
        private readonly Fh6DjMixPlan _plan;
        private readonly bool _ownsSongReader;
        private readonly PcmStreamInfo _info;
        private readonly long _djFramesAtOutputRate;
        private float[] _songBuffer = [];
        private ulong _currentFrame;
        private bool _disposed;

        public PcmStreamInfo Info => _info;
        public ulong SongTotalFrames => _song.Info.TotalFrames;
        public double SongDurationSeconds => _song.Info.SampleRate > 0 && _song.Info.TotalFrames > 0
            ? (double)_song.Info.TotalFrames / _song.Info.SampleRate
            : 0d;
        public ulong CurrentFrame { get { lock (_sync) return _currentFrame; } }
        public bool IsEndOfStream
        {
            get
            {
                lock (_sync)
                {
                    if (_info.TotalFrames > 0)
                        return _currentFrame >= _info.TotalFrames;
                    return _song.IsEndOfStream && _currentFrame >= (ulong)_djFramesAtOutputRate;
                }
            }
        }
        // The constructor only accepts a format-ready song reader, so the DJ-only
        // prefix can start immediately even if a remote song needs more buffering.
        public bool IsReady => !_disposed;
        public bool CanSeek => !_disposed && _song.CanSeek;
        public bool HasPendingSeek => !_disposed && _song.HasPendingSeek;
        public long PendingSeekFrame
        {
            get
            {
                if (_disposed || !_song.HasPendingSeek || _song.PendingSeekFrame < 0)
                    return -1;
                var relative = Math.Max(0L, _song.PendingSeekFrame - checked((long)_plan.SongSourceStartFrame));
                return checked(relative + _plan.SongTimelineStartFrame);
            }
        }
        public double CacheProgress => _disposed ? -1 : _song.CacheProgress;
        public bool IsCacheComplete => !_disposed && _song.IsCacheComplete;

        public Fh6DjPremixPcmStreamReader(
            IPcmStreamReader songReader,
            Fh6DjPcmClip djClip,
            Fh6DjMixPlan plan,
            bool ownsSongReader = true)
        {
            _song = songReader ?? throw new ArgumentNullException(nameof(songReader));
            _dj = djClip ?? throw new ArgumentNullException(nameof(djClip));
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _ownsSongReader = ownsSongReader;

            if (_song.Info.SampleRate <= 0 || _song.Info.Channels <= 0)
                throw new InvalidOperationException("The song reader format must be ready before creating a DJ premix.");
            ValidatePlan(plan);

            _djFramesAtOutputRate = checked((long)Math.Ceiling(
                (double)_dj.TotalFrames * _song.Info.SampleRate / _dj.SampleRate));
            var totalFrames = CalculateTotalFrames(_song.Info.TotalFrames, plan, _djFramesAtOutputRate);
            _info = new PcmStreamInfo
            {
                SampleRate = _song.Info.SampleRate,
                Channels = _song.Info.Channels,
                TotalFrames = totalFrames,
                Format = string.IsNullOrWhiteSpace(_song.Info.Format)
                    ? "fh6-dj-premix"
                    : _song.Info.Format + "+fh6-dj",
                CanSeek = _song.CanSeek
            };

            if (_song.CurrentFrame != plan.SongSourceStartFrame &&
                !_song.Seek(plan.SongSourceStartFrame))
                throw new InvalidOperationException("The song reader could not seek to the planned premix entry point.");
        }

        public long ReadFrames(float[] buffer, int framesToRead)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (framesToRead < 0)
                throw new ArgumentOutOfRangeException(nameof(framesToRead));
            if (buffer.Length < checked(framesToRead * _info.Channels))
                throw new ArgumentException("The PCM buffer is smaller than framesToRead * channels.", nameof(buffer));

            lock (_sync)
            {
                if (_disposed || framesToRead == 0)
                    return 0;

                Array.Clear(buffer, 0, framesToRead * _info.Channels);
                if (_info.TotalFrames > 0 && _currentFrame >= _info.TotalFrames)
                    return 0;

                var requested = framesToRead;
                if (_info.TotalFrames > 0)
                    requested = (int)Math.Min((ulong)requested, _info.TotalFrames - _currentFrame);

                var timelinePosition = checked((long)_currentFrame);
                var djOnlyPrefix = timelinePosition < _plan.SongTimelineStartFrame
                    ? (int)Math.Min(requested, _plan.SongTimelineStartFrame - timelinePosition)
                    : 0;
                var songRequested = requested - djOnlyPrefix;
                var songRead = 0;

                if (songRequested > 0 && !_song.IsEndOfStream)
                {
                    EnsureSongBuffer(songRequested);
                    var read = _song.ReadFrames(_songBuffer, songRequested);
                    if (read < 0)
                        return -1;
                    songRead = checked((int)read);

                    if (songRead == 0 && !_song.IsEndOfStream)
                    {
                        if (djOnlyPrefix == 0)
                            return 0;
                        requested = djOnlyPrefix;
                        songRequested = 0;
                    }
                    else if (songRead < songRequested)
                    {
                        requested = djOnlyPrefix + songRead;
                    }
                }
                else if (songRequested > 0)
                {
                    var remainingDj = Math.Max(0, _djFramesAtOutputRate - timelinePosition);
                    requested = (int)Math.Min(requested, remainingDj);
                    songRequested = 0;
                }

                if (requested == 0)
                    return 0;

                for (var outputFrame = 0; outputFrame < requested; outputFrame++)
                {
                    var absoluteFrame = timelinePosition + outputFrame;
                    var songFrameInBuffer = outputFrame - djOnlyPrefix;
                    var hasSong = songFrameInBuffer >= 0 && songFrameInBuffer < songRead;
                    var songGain = hasSong ? CalculateSongGain(absoluteFrame) : 0f;
                    var djGain = CalculateDjGain(absoluteFrame);
                    var djSourceFrame = (double)absoluteFrame * _dj.SampleRate / _info.SampleRate;

                    for (var channel = 0; channel < _info.Channels; channel++)
                    {
                        var value = hasSong
                            ? _songBuffer[songFrameInBuffer * _info.Channels + channel] * songGain
                            : 0f;
                        if (djGain > 0f)
                            value += _dj.SampleAt(djSourceFrame, channel, _info.Channels) * djGain;
                        buffer[outputFrame * _info.Channels + channel] = Math.Clamp(value, -1f, 1f);
                    }
                }

                _currentFrame += checked((ulong)requested);
                return requested;
            }
        }

        public bool Seek(ulong frameIndex)
        {
            lock (_sync)
            {
                if (_disposed || !_song.CanSeek)
                    return false;

                if (_info.TotalFrames > 0)
                    frameIndex = Math.Min(frameIndex, _info.TotalFrames);

                var timelineFrame = checked((long)frameIndex);
                var songRelative = Math.Max(0, timelineFrame - _plan.SongTimelineStartFrame);
                var songFrame = checked(_plan.SongSourceStartFrame + (ulong)songRelative);
                if (!_song.Seek(songFrame))
                    return false;

                _currentFrame = frameIndex;
                return true;
            }
        }

        public double MapTimelineFrameToSongSeconds(ulong timelineFrame)
        {
            var songFrame = checked(
                (double)timelineFrame - _plan.SongTimelineStartFrame + _plan.SongSourceStartFrame);
            if (_song.Info.TotalFrames > 0)
                songFrame = Math.Min(songFrame, _song.Info.TotalFrames);
            return songFrame / _info.SampleRate;
        }

        public ulong MapSongSecondsToTimelineFrame(double songSeconds)
        {
            if (double.IsNaN(songSeconds) || double.IsInfinity(songSeconds))
                throw new ArgumentOutOfRangeException(nameof(songSeconds));

            var songFrame = Math.Max(0d, songSeconds) * _info.SampleRate;
            if (_song.Info.TotalFrames > 0)
                songFrame = Math.Min(songFrame, _song.Info.TotalFrames);
            var songRelative = Math.Max(0d, songFrame - _plan.SongSourceStartFrame);
            var timelineFrame = _plan.SongTimelineStartFrame + songRelative;
            if (_info.TotalFrames > 0)
                timelineFrame = Math.Min(timelineFrame, _info.TotalFrames);
            return checked((ulong)Math.Round(timelineFrame));
        }

        public void CancelPendingSeek()
        {
            if (!_disposed)
                _song.CancelPendingSeek();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _songBuffer = [];
                if (_ownsSongReader)
                    _song.Dispose();
            }
        }

        private float CalculateSongGain(long timelineFrame)
        {
            var fade = _plan.SongFadeInFrames <= 0
                ? 1f
                : Math.Clamp(
                    (float)(timelineFrame - _plan.SongTimelineStartFrame) / _plan.SongFadeInFrames,
                    0f,
                    1f);

            float duck;
            if (timelineFrame < _plan.DjEndFrame)
            {
                duck = _plan.DuckedSongGain;
            }
            else if (_plan.DuckReleaseFrames <= 0)
            {
                duck = 1f;
            }
            else
            {
                var release = Math.Clamp(
                    (float)(timelineFrame - _plan.DjEndFrame) / _plan.DuckReleaseFrames,
                    0f,
                    1f);
                duck = _plan.DuckedSongGain + (1f - _plan.DuckedSongGain) * release;
            }

            return fade * duck;
        }

        private float CalculateDjGain(long timelineFrame)
        {
            if (timelineFrame < 0 || timelineFrame >= _djFramesAtOutputRate)
                return 0f;
            if (_plan.DjFadeOutFrames <= 0)
                return _plan.DjGain;

            var fadeStart = Math.Max(0, _djFramesAtOutputRate - _plan.DjFadeOutFrames);
            if (timelineFrame < fadeStart)
                return _plan.DjGain;
            return _plan.DjGain * Math.Clamp(
                (float)(_djFramesAtOutputRate - timelineFrame) / _plan.DjFadeOutFrames,
                0f,
                1f);
        }

        private void EnsureSongBuffer(int frames)
        {
            var samples = checked(frames * _info.Channels);
            if (_songBuffer.Length < samples)
                _songBuffer = new float[samples];
            else
                Array.Clear(_songBuffer, 0, samples);
        }

        private static ulong CalculateTotalFrames(
            ulong songTotalFrames,
            Fh6DjMixPlan plan,
            long djFrames)
        {
            if (songTotalFrames == 0)
                return 0;

            var remainingSong = songTotalFrames > plan.SongSourceStartFrame
                ? songTotalFrames - plan.SongSourceStartFrame
                : 0;
            var songEnd = checked((ulong)plan.SongTimelineStartFrame + remainingSong);
            return Math.Max(songEnd, checked((ulong)djFrames));
        }

        private static void ValidatePlan(Fh6DjMixPlan plan)
        {
            if (plan.SongTimelineStartFrame < 0 || plan.SongFadeInFrames < 0 ||
                plan.DjEndFrame <= 0 || plan.DuckReleaseFrames < 0 || plan.DjFadeOutFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(plan), "Premix frame values are invalid.");
            if (plan.DuckedSongGain is < 0f or > 1f || plan.DjGain < 0f)
                throw new ArgumentOutOfRangeException(nameof(plan), "Premix gains are invalid.");
        }
    }
}
