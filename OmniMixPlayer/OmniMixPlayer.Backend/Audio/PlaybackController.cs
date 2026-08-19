using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.Backend.Audio.Dj;
using OmniMixPlayer.Backend.Http;
using OmniMixPlayer.Backend.ModuleSystem;
using OmniMixPlayer.SDK.Events;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Backend.Audio
{
    public class PlaybackController : IDisposable
    {
        private const int ConsecutiveFailureNoticeThreshold = 3;
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(8);

        private readonly ILogger _logger;
        private readonly SharedMemoryServer _sharedMemory;
        private readonly IEventBus _eventBus;
        private readonly ILibraryRegistry _library;
        private readonly IStreamingService _streamingService;
        private readonly PlaybackTimelineStore _timeline;
        private readonly Fh6DjPlaybackCoordinator _djCoordinator;
        private readonly object _lock = new();

        public string Id { get; }

        private readonly Equalizer _equalizer = new();
        public Equalizer Equalizer => _equalizer;

        private CancellationTokenSource _playbackCts;
        private Task _playbackTask;
        private int _playbackGeneration;
        private readonly VolumeNode _volumeNode = new();
        public VolumeNode VolumeNode => _volumeNode;
        private volatile int _playState;
        private float _targetLatency = 0.1f;

        private IPcmStreamReader _currentReader;
        private Track _playingTrack;
        private float _currentDuration;
        private bool _disposed;
        private int _consecutivePlaybackFailures;

        public event Action<Track> OnTrackChanged;
        public event Action<Track, float> OnPlaybackMetadataChanged;
        public event Action<int> OnStateChanged;
        public event Action<float> OnPositionChanged;
        public event Action<PlaybackFailureNotice> OnFailureNotice;

        public Track CurrentTrack => _timeline.GetCurrentTrack(Id);
        public bool IsPlaying => _playState == 1;
        public float Position { get; private set; }
        public float CurrentDuration => _currentDuration;
        public float Volume { get => _volumeNode.Volume; set => _volumeNode.Volume = value; }
        public float TargetLatency { get => _targetLatency; set => _targetLatency = Math.Clamp(value, 0.03f, 1.0f); }
        public bool Shuffle => _timeline.Get(Id).Shuffle;
        public RepeatMode RepeatMode => _timeline.Get(Id).RepeatMode;

        public PlaybackController(
            ILogger logger,
            SharedMemoryServer sharedMemory,
            IEventBus eventBus,
            ILibraryRegistry library,
            IStreamingService streamingService,
            PlaybackTimelineStore timeline,
            string instanceId,
            bool serverControlledPlayback = false,
            Fh6DjAssetPreparationCoordinator djAssets = null,
            InstanceProfile profile = null)
        {
            _logger = logger;
            _sharedMemory = sharedMemory;
            _eventBus = eventBus;
            _library = library;
            _streamingService = streamingService;
            _timeline = timeline;
            Id = instanceId;
            ServerControlledPlayback = serverControlledPlayback;
            if (djAssets != null && Fh6DjPlaybackCoordinator.TryGetPlaybackScope(profile, out var djPlaybackScope))
            {
                _djCoordinator = new Fh6DjPlaybackCoordinator(
                    logger,
                    instanceId,
                    djPlaybackScope,
                    timeline,
                    library,
                    djAssets,
                    CreateBaseReaderForTrackAsync);
            }
        }

        /// <summary>
        /// 后端是否控制播放流程。false = 客户端自己管理队列和切歌，
        /// 此时后端不应在歌曲自然结束时自动推进。
        /// </summary>
        public bool ServerControlledPlayback { get; set; }

        public void ApplyProfile(InstanceProfile profile)
        {
            if (profile == null) return;
            lock (_lock)
            {
                SetVolume(profile.Volume);
                SetTargetLatency(profile.TargetLatency);
                if (profile.Equalizer != null)
                    _equalizer.UpdateState(MapEqualizerStateToInternal(profile.Equalizer));
            }
        }

        public void Play(string uuid = null)
        {
            lock (_lock)
            {
                TimelineAdvanceResult result;
                if (!string.IsNullOrWhiteSpace(uuid))
                {
                    result = _timeline.PlayExplicit(Id, uuid);
                }
                else if (_playState == 0)
                {
                    result = _timeline.EnsureCurrentOrTakeNext(Id);
                }
                else
                {
                    Resume();
                    return;
                }

                PlayTimelineResult(result, PlaySource.UserClick);
            }
        }

        public void Pause() { lock (_lock) { SetPlayState(2); } }
        public void Resume() { lock (_lock) { SetPlayState(1); } }

        public void Toggle()
        {
            lock (_lock)
            {
                if (_playState == 1) SetPlayState(2);
                else if (_playState == 2) SetPlayState(1);
                else Play();
            }
        }

        public void Next()
        {
            lock (_lock)
            {
                var result = _timeline.Next(Id);
                if (string.IsNullOrWhiteSpace(result.CurrentUuid)) StopInternal(clearTimeline: false);
                else PlayTimelineResult(result, PlaySource.Queue);
            }
        }

        public void Prev()
        {
            lock (_lock)
            {
                var before = _playingTrack?.Uuid;
                var result = _timeline.Previous(Id);
                if (string.IsNullOrWhiteSpace(result.CurrentUuid) || result.CurrentUuid == before)
                    return;
                PlayTimelineResult(result, PlaySource.Previous);
            }
        }

        public void Seek(float position)
        {
            IPcmStreamReader reader;
            int sampleRate;
            long targetFrame;
            lock (_lock)
            {
                Position = position;
                reader = _currentReader;
                sampleRate = reader?.Info.SampleRate > 0 ? reader.Info.SampleRate : 44100;
                targetFrame = reader is Fh6DjPremixPcmStreamReader premix
                    ? checked((long)premix.MapSongSecondsToTimelineFrame(position))
                    : Math.Max(0, (long)(position * sampleRate));
                if (reader != null && !reader.Seek((ulong)targetFrame))
                {
                    _logger.LogWarning("Seek failed: position={Position}, frame={Frame}", position, targetFrame);
                    return;
                }
                OnPositionChanged?.Invoke(position);
            }
            if (reader != null)
            {
                _sharedMemory?.AdvanceGeneration(targetFrame);
                _sharedMemory?.RequestSeek(targetFrame);
            }
        }

        public void Stop()
        {
            lock (_lock) { StopInternal(clearTimeline: true); }
        }

        public void SetVolume(float volume) { _volumeNode.Volume = Math.Clamp(volume, 0f, 1f); }
        public void SetTargetLatency(float latency) { _targetLatency = Math.Clamp(latency, 0.03f, 1.0f); }

        private void PlayTimelineResult(TimelineAdvanceResult result, PlaySource fallbackSource)
        {
            if (string.IsNullOrWhiteSpace(result?.CurrentUuid))
            {
                StopInternal(clearTimeline: false);
                return;
            }

            var track = _library.GetTrack(result.CurrentUuid);
            if (track == null || track.IsExcluded)
            {
                _logger.LogWarning("Timeline current track not playable: {Uuid}", result.CurrentUuid);
                StopInternal(clearTimeline: false);
                return;
            }

            var source = result.Reason switch
            {
                TimelineAdvanceReason.NaturalEnd => PlaySource.AutoNext,
                TimelineAdvanceReason.UserPrevious => PlaySource.Previous,
                TimelineAdvanceReason.UserNext => PlaySource.Queue,
                TimelineAdvanceReason.RepeatOne => PlaySource.AutoNext,
                TimelineAdvanceReason.ExplicitPlay => PlaySource.UserClick,
                _ => fallbackSource
            };
            PlayTrack(track, source);
        }

        private void PlayTrack(Track track, PlaySource source)
        {
            ReleaseCurrentReader(_playingTrack);
            _playingTrack = track;
            SetPlayState(1);
            Position = 0;
            _currentDuration = Math.Max(0f, track.Duration);
            OnTrackChanged?.Invoke(track);

            _eventBus.Publish(new PlayStartedEvent { Music = track, Source = source });

            // This only schedules background preparation for the following track.
            // The active track keeps its normal reader when no prepared premix exists.
            _djCoordinator?.OnTrackStarted();

            var gen = Interlocked.Increment(ref _playbackGeneration);
            _playbackCts?.Cancel();
            _playbackCts = new CancellationTokenSource();
            _playbackTask = Task.Run(() => PlaybackLoopAsync(track, gen, _playbackCts.Token));
        }

        private void StopInternal(bool clearTimeline, bool preserveSharedMemoryTerminalState = false)
        {
            _playState = 0;
            if (!preserveSharedMemoryTerminalState)
                _sharedMemory?.StopStream();
            OnStateChanged?.Invoke(0);
            var track = _playingTrack;
            _playingTrack = null;
            if (clearTimeline)
                _timeline.ClearCurrent(Id);
            ReleaseCurrentReader(track);
            Interlocked.Increment(ref _playbackGeneration);
        }

        private async Task PlaybackLoopAsync(Track track, int generation, CancellationToken ct)
        {
            var completedNaturally = false;
            try
            {
                if (track.SourceType == SourceType.Stream ||
                    track.SourceType == SourceType.Url ||
                    track.SourceType == SourceType.File)
                {
                    var reader = await CreateReaderForTrackAsync(track, ct);

                    if (ct.IsCancellationRequested) return;

                    if (reader == null)
                    {
                        // Failed to resolve playable source — advance to next instead of
                        // leaving the instance stuck in a zombie "playing" state.
                        if (generation == _playbackGeneration)
                        {
                            _logger.LogWarning(
                                "Track {Uuid} failed to resolve — advancing to next track",
                                track.Uuid);
                            _sharedMemory?.MarkError(
                                SDK.Ipc.SharedMemoryStreamError.DecoderFailed);
                            lock (_lock) { HandlePlaybackFailureInternal(track); }
                        }
                        return;
                    }

                    lock (_lock) { _currentReader = reader; }
                    ResetPlaybackFailureStreak();

                    if (reader is Fh6DjPremixPcmStreamReader initialPremix)
                    {
                        lock (_lock) { Position = (float)initialPremix.MapTimelineFrameToSongSeconds(0); }
                        OnPositionChanged?.Invoke(Position);
                    }

                    long totalFramesHint = track.Duration > 0 ? (long)(track.Duration * 44100f) : 0;
                    _sharedMemory?.BeginStream(track.Uuid, totalFramesHint);

                    var formatReady = false;
                    var sampleRate = 44100;
                    var channels = 2;
                    float[] buffer = Array.Empty<float>();
                    var targetBufferedFrames = CalculateTargetBufferedFrames(sampleRate);

                    var decoderFailed = false;
                    while (!ct.IsCancellationRequested && !reader.IsEndOfStream)
                    {
                        if (_playState == 2)
                        {
                            await Task.Delay(10, ct);
                            continue;
                        }
                        if (_playState == 0)
                            break;

                        if (!formatReady)
                        {
                            var info = reader.Info;
                            if (reader.IsReady && info.SampleRate > 0 && info.Channels > 0)
                            {
                                sampleRate = info.SampleRate;
                                channels = Math.Max(1, info.Channels);
                                totalFramesHint = info.TotalFrames > 0
                                    ? (long)info.TotalFrames
                                    : (track.Duration > 0 ? (long)(track.Duration * sampleRate) : 0);
                                UpdateDurationFromReader(track, reader);
                                _sharedMemory?.MarkFormatReady(sampleRate, channels, totalFramesHint);
                                buffer = new float[1024 * channels];
                                targetBufferedFrames = CalculateTargetBufferedFrames(sampleRate);
                                formatReady = true;
                            }
                            else
                            {
                                await Task.Delay(25, ct);
                                continue;
                            }
                        }

                        var currentInfo = reader.Info;
                        if (currentInfo.SampleRate > 0 && currentInfo.Channels > 0 &&
                            (currentInfo.SampleRate != sampleRate || currentInfo.Channels != channels))
                        {
                            sampleRate = currentInfo.SampleRate;
                            channels = Math.Max(1, currentInfo.Channels);
                            totalFramesHint = currentInfo.TotalFrames > 0
                                ? (long)currentInfo.TotalFrames
                                : (track.Duration > 0 ? (long)(track.Duration * sampleRate) : 0);
                            UpdateDurationFromReader(track, reader);
                            _sharedMemory?.MarkFormatReady(sampleRate, channels, totalFramesHint);
                            buffer = new float[1024 * channels];
                            targetBufferedFrames = CalculateTargetBufferedFrames(sampleRate);
                        }

                        targetBufferedFrames = CalculateTargetBufferedFrames(sampleRate);
                        while (!ct.IsCancellationRequested &&
                               _sharedMemory?.GetReadableFrames() >= targetBufferedFrames)
                        {
                            await Task.Delay(1, ct);
                        }

                        var frames = reader.ReadFrames(buffer, buffer.Length / channels);
                        if (frames < 0)
                        {
                            _sharedMemory?.MarkError(SDK.Ipc.SharedMemoryStreamError.DecoderFailed);
                            decoderFailed = true;
                            break;
                        }
                        if (frames == 0)
                        {
                            await Task.Delay(10, ct);
                            continue;
                        }

                        _volumeNode.Process(buffer, (int)frames, channels);
                        _equalizer.Process(buffer, (int)frames, channels, sampleRate);

                        var audibleFrame = _sharedMemory != null
                            ? Math.Max(0, _sharedMemory.GetAudibleCursor())
                            : checked((long)reader.CurrentFrame);
                        lock (_lock)
                        {
                            Position = reader is Fh6DjPremixPcmStreamReader premix
                                ? (float)premix.MapTimelineFrameToSongSeconds(checked((ulong)audibleFrame))
                                : (float)audibleFrame / sampleRate;
                        }
                        OnPositionChanged?.Invoke(Position);

                        _sharedMemory?.WriteFrames(buffer, (int)frames);
                    }

                    var naturalDecoderEof = !decoderFailed && reader.IsEndOfStream;
                    if (!ct.IsCancellationRequested && generation == _playbackGeneration && naturalDecoderEof)
                    {
                        _sharedMemory?.MarkDecoderEof((long)reader.CurrentFrame);
                        await WaitForClientDrainAsync(track, sampleRate, generation, ct).ConfigureAwait(false);
                        if (!ct.IsCancellationRequested && generation == _playbackGeneration)
                        {
                            _sharedMemory?.MarkEnded();
                            completedNaturally = true;
                        }
                    }

                    reader.Dispose();
                    lock (_lock) { _currentReader = null; }
                    _eventBus.Publish(new MusicResourcesReleasedEvent { Music = track });

                    if (!ct.IsCancellationRequested && generation == _playbackGeneration && decoderFailed)
                    {
                        _logger.LogWarning("Track {Uuid} decoder failed — advancing to next track", track.Uuid);
                        lock (_lock) { HandlePlaybackFailureInternal(track); }
                        return;
                    }
                }
                else
                {
                    await Task.Delay(100, ct);
                }

                if (!ct.IsCancellationRequested && generation == _playbackGeneration && completedNaturally)
                {
                    lock (_lock)
                    {
                        _eventBus.Publish(new PlayEndedEvent { Music = track, Reason = PlayEndReason.Completed });
                        ResetPlaybackFailureStreak();

                        if (ServerControlledPlayback)
                        {
                            // 服务端控制模式：自动推进到下一首
                            var result = _timeline.NaturalEnd(Id);
                            if (!string.IsNullOrWhiteSpace(result.CurrentUuid))
                                PlayTimelineResult(result, PlaySource.AutoNext);
                            else
                                StopInternal(clearTimeline: false, preserveSharedMemoryTerminalState: true);
                        }
                        else
                        {
                            // 客户端管理模式：仅停止，由客户端决定下一首
                            _logger.LogInformation(
                                "Track {Uuid} ended (client-managed mode), stopping and waiting for client to choose next",
                                track.Uuid);
                            StopInternal(clearTimeline: false, preserveSharedMemoryTerminalState: true);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playback error for {Uuid}", track.Uuid);
                lock (_lock)
                {
                    RegisterPlaybackFailure(track);
                }
            }
        }

        private async Task WaitForClientDrainAsync(Track track, int sampleRate, int generation, CancellationToken ct)
        {
            if (_sharedMemory == null) return;

            var toleranceFrames = Math.Max(256, Math.Max(1, sampleRate) / 20);
            var startedAt = Environment.TickCount64;
            while (!ct.IsCancellationRequested && generation == _playbackGeneration)
            {
                if (_sharedMemory.IsClientDrained(toleranceFrames))
                {
                    _logger.LogInformation("Track {Uuid} PCM drained by client", track.Uuid);
                    return;
                }

                if (Environment.TickCount64 - startedAt >= DrainTimeout.TotalMilliseconds)
                {
                    _logger.LogWarning(
                        "Track {Uuid} PCM drain timed out after {TimeoutMs} ms: read={ReadCursor}, audible={AudibleCursor}, final={FinalCursor}",
                        track.Uuid,
                        (long)DrainTimeout.TotalMilliseconds,
                        _sharedMemory.GetReadCursor(),
                        _sharedMemory.GetAudibleCursor(),
                        _sharedMemory.ReadI64(SDK.Ipc.SharedMemoryProtocol.FinalWriteCursor));
                    return;
                }

                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }

        private void HandlePlaybackFailureInternal(Track track)
        {
            _eventBus.Publish(new PlayEndedEvent { Music = track, Reason = PlayEndReason.Failed });
            RegisterPlaybackFailure(track);

            if (ServerControlledPlayback)
            {
                var result = _timeline.NaturalEnd(Id);
                if (!string.IsNullOrWhiteSpace(result.CurrentUuid))
                {
                    PlayTimelineResult(result, PlaySource.AutoNext);
                    return;
                }
            }

            StopInternal(clearTimeline: false, preserveSharedMemoryTerminalState: true);
        }

        private void RegisterPlaybackFailure(Track track)
        {
            _consecutivePlaybackFailures++;
            if (_consecutivePlaybackFailures < ConsecutiveFailureNoticeThreshold) return;

            var moduleName = ResolveModuleDisplayName(track?.ModuleId);
            OnFailureNotice?.Invoke(new PlaybackFailureNotice
            {
                InstanceId = Id,
                ModuleId = track?.ModuleId ?? "",
                ModuleName = moduleName,
                Count = _consecutivePlaybackFailures,
                Message = $"{moduleName} 最近连续 {_consecutivePlaybackFailures} 首歌曲无法播放，可能是登录失效、会员/VIP 权限不足或平台版权限制。建议重新登录或检查账号会员状态。",
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        private void ResetPlaybackFailureStreak()
        {
            _consecutivePlaybackFailures = 0;
        }

        private static string ResolveModuleDisplayName(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId)) return "当前音源";
            var module = ModuleLoader.Instance?.GetModule(moduleId);
            if (!string.IsNullOrWhiteSpace(module?.DisplayName)) return module.DisplayName;
            return moduleId;
        }

        private async Task<IPcmStreamReader> CreateReaderForTrackAsync(Track track, CancellationToken ct)
        {
            var prepared = _djCoordinator?.TakePreparedReader(track);
            if (prepared != null)
            {
                _logger.LogInformation("Using precomputed FH6 DJ PCM timeline for {Uuid}", track.Uuid);
                return prepared;
            }

            return await CreateBaseReaderForTrackAsync(track, ct).ConfigureAwait(false);
        }

        private async Task<IPcmStreamReader> CreateBaseReaderForTrackAsync(Track track, CancellationToken ct)
        {
            var decoderProvider = ModuleLoader.Instance?.GetProvider<IModuleAudioDecoderProvider>(track.ModuleId);
            if (decoderProvider != null && decoderProvider.CanDecode(track.Uuid))
            {
                _logger.LogInformation("Creating module PCM decoder for {Uuid} via {ModuleId}", track.Uuid, track.ModuleId);
                var reader = await decoderProvider.CreateDecoderAsync(track.Uuid, AudioQuality.ExHigh, ct);
                if (reader != null)
                    return reader;
            }

            var resolver = ModuleLoader.Instance?.GetProvider<IPlayableSourceResolver>(track.ModuleId);
            if (resolver != null)
            {
                var source = await resolver.ResolveAsync(track.Uuid, AudioQuality.ExHigh, ct);
                if (source == null)
                {
                    _logger.LogWarning("Module {ModuleId} did not resolve playable source for {Uuid}", track.ModuleId, track.Uuid);
                    return null;
                }

                var format = FormatToString(source.Format, source.Url, source.CachePath);
                var duration = track.Duration > 0 ? track.Duration : 0;
                _logger.LogInformation(
                    "Resolved playable source for {Uuid}: type={Type}, format={Format}, cachePath={CachePath}, urlPresent={UrlPresent}",
                    track.Uuid,
                    source.SourceType,
                    format,
                    source.CachePath ?? "",
                    !string.IsNullOrWhiteSpace(source.Url));
                if (source.UseCachePath && !string.IsNullOrWhiteSpace(source.CachePath))
                {
                    return _streamingService.CreateStream(
                        source.GetPath(),
                        format,
                        duration,
                        source.CachePath,
                        source.Headers,
                        useCachePath: true);
                }

                var cacheKey = !string.IsNullOrWhiteSpace(source.CacheKey)
                    ? source.CacheKey
                    : $"{track.ModuleId}_{track.Uuid}";
                return _streamingService.CreateStream(
                    source.GetPath(),
                    format,
                    duration,
                    cacheKey,
                    source.Headers);
            }

            if (string.IsNullOrWhiteSpace(track.SourcePath))
            {
                _logger.LogWarning("Track {Uuid} has no source path", track.Uuid);
                return null;
            }

            var fallbackFormat = FormatToString(AudioFormat.Unknown, track.SourcePath, null);
            return _streamingService.CreateStream(
                track.SourcePath,
                fallbackFormat,
                track.Duration,
                $"{track.ModuleId}_{track.Uuid}");
        }

        private int CalculateTargetBufferedFrames(int sampleRate)
        {
            var latency = Math.Clamp(TargetLatency, 0.03f, 1.0f);
            return Math.Max(2048, (int)(Math.Max(1, sampleRate) * latency));
        }

        private static string FormatToString(AudioFormat format, string url, string cachePath)
        {
            return AudioFormatExtensions.ToFormatString(format, url, cachePath);
        }


        private void SetPlayState(int state)
        {
            _playState = state;
            _sharedMemory?.SetPlayState(state);
            OnStateChanged?.Invoke(state);
        }

        private void ReleaseCurrentReader(Track track)
        {
            var reader = _currentReader;
            _currentReader = null;
            if (reader == null) return;
            reader.Dispose();
            if (track != null)
                _eventBus.Publish(new MusicResourcesReleasedEvent { Music = track });
        }

        private static Audio.EqualizerState MapEqualizerStateToInternal(SDK.Protos.Models.EqualizerState proto)
        {
            var state = new Audio.EqualizerState
            {
                Enabled = proto.Enabled,
                GlobalGainDb = proto.GlobalGainDb,
                SoftClipEnabled = proto.SoftClipEnabled
            };
            foreach (var pt in proto.Points)
            {
                state.Points.Add(new Audio.EqualizerPoint
                {
                    Id = pt.Id,
                    Frequency = pt.Frequency,
                    GainDb = pt.GainDb,
                    Q = pt.Q,
                    Type = MapEqualizerFilterType(pt.Type)
                });
            }
            return state;
        }

        private static Audio.EqualizerFilterType MapEqualizerFilterType(SDK.Protos.Models.EqualizerFilterType type)
        {
            return type switch
            {
                SDK.Protos.Models.EqualizerFilterType.EqFilterTypeLowShelf => Audio.EqualizerFilterType.LowShelf,
                SDK.Protos.Models.EqualizerFilterType.EqFilterTypeHighShelf => Audio.EqualizerFilterType.HighShelf,
                SDK.Protos.Models.EqualizerFilterType.EqFilterTypeLowPass => Audio.EqualizerFilterType.LowPass,
                SDK.Protos.Models.EqualizerFilterType.EqFilterTypeHighPass => Audio.EqualizerFilterType.HighPass,
                _ => Audio.EqualizerFilterType.Peaking
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _playbackCts?.Cancel();
            _playbackCts?.Dispose();
            ReleaseCurrentReader(_playingTrack);
            _djCoordinator?.Dispose();
        }

        private void UpdateDurationFromReader(Track track, IPcmStreamReader reader)
        {
            var info = reader?.Info;
            if (track == null || info == null || info.SampleRate <= 0 || info.TotalFrames == 0)
                return;

            var duration = reader is Fh6DjPremixPcmStreamReader premix && premix.SongDurationSeconds > 0
                ? (float)premix.SongDurationSeconds
                : (float)info.TotalFrames / info.SampleRate;
            if (duration <= 0f || Math.Abs(duration - _currentDuration) < 0.01f)
                return;

            _currentDuration = duration;
            OnPlaybackMetadataChanged?.Invoke(track, duration);
        }

        public void NotifyGlobalConfigurationChanged()
        {
            _djCoordinator?.NotifyConfigurationChanged();
        }
    }
}
