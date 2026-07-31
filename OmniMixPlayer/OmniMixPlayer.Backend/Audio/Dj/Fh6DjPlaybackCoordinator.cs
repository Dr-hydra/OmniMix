using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.Backend.Http;
using OmniMixPlayer.SDK.Caching;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    public sealed record Fh6DjRuntimeConfiguration(
        bool Enabled,
        int HostNumber,
        string GameRoot,
        string CacheDirectory,
        string Scope,
        Fh6DjInsertionContent Content,
        int Frequency)
    {
        public bool CanPrepare => Enabled &&
            !string.IsNullOrWhiteSpace(GameRoot) &&
            Directory.Exists(GameRoot) &&
            !string.IsNullOrWhiteSpace(CacheDirectory);

        public bool CanPremixForInstance(string instanceId, Fh6DjPlaybackScope playbackScope) =>
            CanPrepare && Fh6DjSettings.ScopeIncludes(Scope, instanceId, playbackScope);

        public string CacheKey => string.Join("|",
            HostNumber,
            GameRoot ?? string.Empty,
            CacheDirectory ?? string.Empty,
            Fh6DjSettings.NormalizeScope(Scope),
            Fh6DjSettings.ToConfigValue(Content),
            Fh6DjSettings.NormalizeFrequency(Frequency));
    }

    /// <summary>
    /// Owns extraction of the selected FH6 host. It is independent of an active game
    /// session so enabling DJ mode can prepare assets before the game is launched.
    /// </summary>
    public sealed class Fh6DjAssetPreparationCoordinator : IDisposable
    {
        private readonly object _sync = new();
        private readonly GlobalConfigManager _config;
        private readonly ILogger _logger;
        private readonly Func<IFh6DjClipExtractor> _extractorFactory;
        private CancellationTokenSource _preparationCts;
        private Task<Fh6DjPreparationResult> _preparationTask;
        private string _preparationKey;
        private bool _disposed;

        public Fh6DjAssetPreparationCoordinator(
            GlobalConfigManager config,
            ILogger logger,
            Func<IFh6DjClipExtractor> extractorFactory = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;
            _extractorFactory = extractorFactory ?? (() => new VgmstreamCliDjClipExtractor());
        }

        public Fh6DjRuntimeConfiguration GetConfiguration()
        {
            var enabled = _config.GetValue(Fh6DjConfigurationKeys.Enabled, false);
            var host = Math.Clamp(_config.GetValue(Fh6DjConfigurationKeys.Host, 1), 1, Fh6DjHosts.All.Count);
            var gameRoot = _config.GetValue<string>(Fh6DjConfigurationKeys.GameRoot, null);
            var scope = Fh6DjSettings.NormalizeScope(_config.GetValue<string>(Fh6DjConfigurationKeys.Scope, null));
            var content = Fh6DjSettings.ParseContent(_config.GetValue<string>(Fh6DjConfigurationKeys.Content, null));
            var frequency = Fh6DjSettings.NormalizeFrequency(_config.GetValue(Fh6DjConfigurationKeys.Frequency, 1));
            try
            {
                if (!string.IsNullOrWhiteSpace(gameRoot))
                    gameRoot = Path.GetFullPath(gameRoot);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                gameRoot = null;
            }

            return new Fh6DjRuntimeConfiguration(
                enabled,
                host,
                gameRoot,
                CachePaths.DjDirectory,
                scope,
                content,
                frequency);
        }

        public void NotifyConfigurationChanged()
        {
            var configuration = GetConfiguration();
            if (!configuration.CanPrepare)
            {
                CancelPreparation();
                return;
            }

            _ = EnsurePreparedAsync(configuration, CancellationToken.None);
        }

        public Task<Fh6DjPreparationResult> EnsurePreparedAsync(CancellationToken cancellationToken = default)
        {
            return EnsurePreparedAsync(GetConfiguration(), cancellationToken);
        }

        public Task<Fh6DjPreparationResult> EnsurePreparedAsync(
            Fh6DjRuntimeConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (!configuration.CanPrepare || _disposed)
                return Task.FromResult<Fh6DjPreparationResult>(null);

            Task<Fh6DjPreparationResult> task;
            lock (_sync)
            {
                if (_disposed)
                    return Task.FromResult<Fh6DjPreparationResult>(null);

                if (_preparationTask == null ||
                    _preparationTask.IsCanceled ||
                    _preparationTask.IsFaulted ||
                    !string.Equals(_preparationKey, configuration.CacheKey, StringComparison.Ordinal))
                {
                    _preparationCts?.Cancel();
                    _preparationCts?.Dispose();
                    _preparationCts = new CancellationTokenSource();
                    _preparationKey = configuration.CacheKey;
                    var token = _preparationCts.Token;
                    _preparationTask = Task.Run(
                        () => PrepareAsync(configuration, token),
                        token);
                }

                task = _preparationTask;
            }

            return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
        }

        private async Task<Fh6DjPreparationResult> PrepareAsync(
            Fh6DjRuntimeConfiguration configuration,
            CancellationToken cancellationToken)
        {
            try
            {
                var service = new Fh6DjPreparationService(
                    configuration.CacheDirectory,
                    _extractorFactory());
                var result = await service
                    .PrepareAsync(configuration.GameRoot, configuration.HostNumber, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var protectedPaths = new string[result.Manifest.Clips.Count + 1];
                protectedPaths[0] = Path.Combine(result.Identity.CacheDirectory, "manifest.json");
                for (var index = 0; index < result.Manifest.Clips.Count; index++)
                    protectedPaths[index + 1] = result.Manifest.Clips[index].FilePath;
                CacheQuotaManager.Default.EnforceQuota(protectedPaths);

                _logger?.LogInformation(
                    "FH6 DJ host {Host} is prepared from local game files ({Cached})",
                    configuration.HostNumber,
                    result.WasAlreadyPrepared ? "cache hit" : "extracted");
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception)
            {
                // DJ is optional. A failed preparation must leave normal game playback usable.
                _logger?.LogWarning(exception, "FH6 DJ preparation failed; normal playback will be used");
                return null;
            }
        }

        private void CancelPreparation()
        {
            lock (_sync)
            {
                _preparationCts?.Cancel();
                _preparationCts?.Dispose();
                _preparationCts = null;
                _preparationTask = null;
                _preparationKey = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelPreparation();
        }
    }

    /// <summary>
    /// Prepares a single composite PCM reader for the next FH6 track. All extraction,
    /// decoding, intro analysis, and clip loading run before the handoff to the game.
    /// The bridge receives only the resulting regular PCM stream.
    /// </summary>
    public sealed class Fh6DjPlaybackCoordinator : IDisposable
    {
        private const int ReaderWarmupTimeoutMilliseconds = 15_000;
        private const int MaximumPreparedReaders = 3;

        private readonly object _sync = new();
        private readonly ILogger _logger;
        private readonly string _instanceId;
        private readonly Fh6DjPlaybackScope _playbackScope;
        private readonly PlaybackTimelineStore _timeline;
        private readonly ILibraryRegistry _library;
        private readonly Fh6DjAssetPreparationCoordinator _assets;
        private readonly Func<Track, CancellationToken, Task<IPcmStreamReader>> _readerFactory;
        private CancellationTokenSource _warmupCts;
        private string _warmupKey;
        private readonly Dictionary<string, PreparedPremix> _preparedReaders =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _frequencyDecisions =
            new(StringComparer.Ordinal);
        private string _frequencyConfigurationKey;
        private int _nextFrequencyOrdinal;
        private string _lastSoundName;
        private bool _disposed;

        public Fh6DjPlaybackCoordinator(
            ILogger logger,
            string instanceId,
            Fh6DjPlaybackScope playbackScope,
            PlaybackTimelineStore timeline,
            ILibraryRegistry library,
            Fh6DjAssetPreparationCoordinator assets,
            Func<Track, CancellationToken, Task<IPcmStreamReader>> readerFactory)
        {
            _logger = logger;
            _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
            _playbackScope = playbackScope;
            _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
            _timeline.OnTimelineChanged += HandleTimelineChanged;
        }

        public static bool TryGetPlaybackScope(InstanceProfile profile, out Fh6DjPlaybackScope playbackScope)
        {
            if (profile != null &&
                profile.Kind == InstanceKind.GameMod &&
                string.Equals(profile.ModId, "forza_horizon_6", StringComparison.OrdinalIgnoreCase))
            {
                playbackScope = Fh6DjPlaybackScope.Fh6;
                return true;
            }

            if (profile != null &&
                profile.Kind == InstanceKind.Gui &&
                string.Equals(profile.ModId, "omnimix.vbnet.desktop", StringComparison.OrdinalIgnoreCase))
            {
                playbackScope = Fh6DjPlaybackScope.Desktop;
                return true;
            }

            playbackScope = default;
            return false;
        }

        public void NotifyConfigurationChanged() => ScheduleWarmup();

        public void OnTrackStarted() => ScheduleWarmup();

        public IPcmStreamReader TakePreparedReader(Track track)
        {
            if (track == null || _disposed)
                return null;

            var configuration = _assets.GetConfiguration();
            if (!configuration.CanPremixForInstance(_instanceId, _playbackScope))
            {
                ClearPrepared(resetFrequencyState: true);
                return null;
            }

            lock (_sync)
            {
                string preparedKey = null;
                PreparedPremix prepared = null;
                foreach (var pair in _preparedReaders)
                {
                    if (!string.Equals(pair.Value.TrackUuid, track.Uuid, StringComparison.Ordinal) ||
                        !string.Equals(pair.Value.ConfigurationKey, configuration.CacheKey, StringComparison.Ordinal))
                        continue;

                    preparedKey = pair.Key;
                    prepared = pair.Value;
                    break;
                }

                if (preparedKey == null)
                    return null;
                _preparedReaders.Remove(preparedKey);
                return prepared.Reader;
            }
        }

        private void HandleTimelineChanged(string instanceId)
        {
            if (string.Equals(instanceId, _instanceId, StringComparison.Ordinal))
                ScheduleWarmup();
        }

        private void ScheduleWarmup()
        {
            if (_disposed)
                return;

            var configuration = _assets.GetConfiguration();
            if (!configuration.CanPremixForInstance(_instanceId, _playbackScope))
            {
                ClearPrepared(resetFrequencyState: true);
                return;
            }

            var preview = _timeline.PeekNaturalEnd(_instanceId);
            if (string.IsNullOrWhiteSpace(preview.CurrentUuid))
            {
                ClearPrepared();
                return;
            }

            var track = _library.GetTrack(preview.CurrentUuid);
            if (track == null || track.IsExcluded)
            {
                ClearPrepared();
                return;
            }

            if (!ShouldPrepareForTrack(configuration, track.Uuid))
            {
                ClearPrepared();
                return;
            }

            var key = string.Join("|", configuration.CacheKey, track.Uuid);
            CancellationToken token;
            lock (_sync)
            {
                if (_preparedReaders.ContainsKey(key))
                    return;
                if (string.Equals(_warmupKey, key, StringComparison.Ordinal) &&
                    _warmupCts is { IsCancellationRequested: false })
                    return;

                _warmupCts?.Cancel();
                _warmupCts?.Dispose();
                _warmupCts = new CancellationTokenSource();
                _warmupKey = key;
                token = _warmupCts.Token;
            }

            _ = Task.Run(() => WarmupAsync(track, configuration, key, token), token);
        }

        private async Task WarmupAsync(
            Track track,
            Fh6DjRuntimeConfiguration configuration,
            string warmupKey,
            CancellationToken cancellationToken)
        {
            Fh6DjPremixPcmStreamReader premix = null;
            IPcmStreamReader reader = null;
            try
            {
                var preparedHost = await _assets
                    .EnsurePreparedAsync(configuration, cancellationToken)
                    .ConfigureAwait(false);
                if (preparedHost?.Manifest == null || cancellationToken.IsCancellationRequested)
                    return;

                var clip = Fh6DjClipSelector.Select(
                    preparedHost.Manifest,
                    configuration.Content,
                    track.Uuid,
                    _lastSoundName);
                reader = await _readerFactory(track, cancellationToken).ConfigureAwait(false);
                if (reader == null)
                    return;

                if (!await WaitForReaderReadyAsync(reader, cancellationToken).ConfigureAwait(false))
                {
                    reader.Dispose();
                    return;
                }

                Fh6SongIntroAnalysis intro;
                try
                {
                    intro = Fh6SongIntroPreviewAnalyzer.Analyze(reader, cancellationToken: cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
                {
                    // A deterministic fallback keeps the handoff light when a source
                    // cannot provide a full preview yet.
                    var sampleRate = reader.Info.SampleRate;
                    intro = new Fh6SongIntroAnalysis(
                        FirstAudibleFrame: 0,
                        StableEntryFrame: sampleRate * 750L / 1000L,
                        Confidence: 0f,
                        UsedFallback: true);
                }

                var djClip = Fh6DjPcmClip.LoadWave(clip.FilePath);
                var plan = Fh6DjMixPlanner.Create(
                    intro,
                    reader.Info.SampleRate,
                    djClip.TotalFrames,
                    djClip.SampleRate);
                premix = new Fh6DjPremixPcmStreamReader(reader, djClip, plan);
                reader = null;

                lock (_sync)
                {
                    if (_disposed || cancellationToken.IsCancellationRequested ||
                        !string.Equals(_warmupKey, warmupKey, StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (_preparedReaders.Remove(warmupKey, out var replaced))
                        replaced.Reader.Dispose();
                    _preparedReaders[warmupKey] = new PreparedPremix(
                        warmupKey,
                        configuration.CacheKey,
                        track.Uuid,
                        clip.SoundName,
                        premix,
                        Environment.TickCount64);
                    TrimPreparedReaders();
                    _lastSoundName = clip.SoundName;
                    premix = null;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                // Missing local assets, an unavailable stream, or a failed decoder only
                // disables DJ for this handoff. Playback falls back to the base reader.
                _logger?.LogDebug(exception, "FH6 DJ premix warmup was unavailable for {Track}", track.Uuid);
            }
            finally
            {
                premix?.Dispose();
                reader?.Dispose();
            }
        }

        private bool ShouldPrepareForTrack(Fh6DjRuntimeConfiguration configuration, string trackUuid)
        {
            var configurationKey = configuration.CacheKey;
            var trackKey = string.Join("|", configurationKey, trackUuid ?? string.Empty);
            lock (_sync)
            {
                if (!string.Equals(_frequencyConfigurationKey, configurationKey, StringComparison.Ordinal))
                {
                    _frequencyConfigurationKey = configurationKey;
                    _frequencyDecisions.Clear();
                    _nextFrequencyOrdinal = 0;
                }

                if (_frequencyDecisions.TryGetValue(trackKey, out var existing))
                    return existing;

                _nextFrequencyOrdinal++;
                var shouldInsert = Fh6DjSettings.ShouldInsertAtOrdinal(
                    configuration.Frequency,
                    _nextFrequencyOrdinal);
                _frequencyDecisions[trackKey] = shouldInsert;
                return shouldInsert;
            }
        }

        private static async Task<bool> WaitForReaderReadyAsync(
            IPcmStreamReader reader,
            CancellationToken cancellationToken)
        {
            var deadline = Environment.TickCount64 + ReaderWarmupTimeoutMilliseconds;
            var scratch = new float[8 * 1024];
            while (!cancellationToken.IsCancellationRequested && Environment.TickCount64 < deadline)
            {
                if (reader.IsReady && reader.Info.SampleRate > 0 && reader.Info.Channels > 0 && reader.CanSeek)
                {
                    return reader.CurrentFrame == 0 || reader.Seek(0);
                }

                var read = reader.ReadFrames(scratch, 1024);
                if (read < 0 || reader.IsEndOfStream)
                    return false;
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        private void ClearPrepared(bool resetFrequencyState = false)
        {
            List<Fh6DjPremixPcmStreamReader> readers;
            lock (_sync)
            {
                _warmupCts?.Cancel();
                _warmupCts?.Dispose();
                _warmupCts = null;
                _warmupKey = null;
                if (resetFrequencyState)
                {
                    _frequencyDecisions.Clear();
                    _frequencyConfigurationKey = null;
                    _nextFrequencyOrdinal = 0;
                }
                readers = new List<Fh6DjPremixPcmStreamReader>(_preparedReaders.Count);
                foreach (var prepared in _preparedReaders.Values)
                    readers.Add(prepared.Reader);
                _preparedReaders.Clear();
            }
            foreach (var reader in readers)
                reader.Dispose();
        }

        private void TrimPreparedReaders()
        {
            while (_preparedReaders.Count > MaximumPreparedReaders)
            {
                string oldestKey = null;
                PreparedPremix oldest = null;
                foreach (var pair in _preparedReaders)
                {
                    if (oldest == null || pair.Value.PreparedAtTicks < oldest.PreparedAtTicks)
                    {
                        oldestKey = pair.Key;
                        oldest = pair.Value;
                    }
                }

                if (oldestKey == null)
                    return;
                _preparedReaders.Remove(oldestKey);
                oldest.Reader.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timeline.OnTimelineChanged -= HandleTimelineChanged;
            ClearPrepared();
        }

        private sealed record PreparedPremix(
            string WarmupKey,
            string ConfigurationKey,
            string TrackUuid,
            string SoundName,
            Fh6DjPremixPcmStreamReader Reader,
            long PreparedAtTicks);
    }
}
