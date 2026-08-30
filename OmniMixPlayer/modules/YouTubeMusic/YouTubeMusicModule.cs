using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.SDK.Attributes;
using OmniMixPlayer.SDK.Caching;
using OmniMixPlayer.SDK.Events;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Module.YouTubeMusic
{
    [MusicModule(ModuleInfo.MODULE_ID, ModuleInfo.MODULE_NAME,
        Version = ModuleInfo.MODULE_VERSION,
        Author = ModuleInfo.MODULE_AUTHOR,
        Description = ModuleInfo.MODULE_DESCRIPTION,
        Priority = 80)]
    public sealed class YouTubeMusicModule : IMusicModule, IStreamingMusicSourceProvider, ICoverProvider, IModuleUIProvider
    {
        private const string ConfigPlaylistUrls = "PlaylistUrls";
        private const string ConfigYtDlpPath = "YtDlpPath";
        private const string ConfigCookiesPath = "CookiesPath";
        private const string ConfigMaxItems = "MaxItems";
        private const string ConfigFormatSelector = "FormatSelector";
        private const string ConfigImportOnStartup = "ImportOnStartup";
        private const string ConfigUseCachePath = "UseCachePath";

        private static readonly HttpClient CoverClient = new();

        private IModuleContext _context;
        private ILogger _logger;
        private YouTubeMusicBridge _bridge;
        private readonly Dictionary<string, YouTubeMusicEntry> _entriesByUuid = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (byte[] data, string mimeType)> _coverCache = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource _refreshCts;
        private string _statusText = "Not imported";
        private string _toolStatusText = "Not checked";
        private bool _isReady;

        public string ModuleId => ModuleInfo.MODULE_ID;
        public string DisplayName => ModuleInfo.MODULE_NAME;
        public string Version => ModuleInfo.MODULE_VERSION;
        public int Priority => 80;
        public SourceType SourceType => SourceType.Stream;
        public bool IsReady => _isReady;
        public event Action<bool> OnReadyStateChanged;

        public ModuleCapabilities Capabilities => new()
        {
            CanDelete = false,
            CanFavorite = false,
            CanExclude = false,
            ProvidesCover = true,
            ProvidesAlbum = false,
            ProvidesPlaylist = true
        };

        public Action<SlintNode> PushUI { get; set; }
        public bool HasSettingsUI => true;

        public async Task InitializeAsync(IModuleContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = context.Logger;
            var moduleDataPath = context.GetModuleDataPath(ModuleId);
            Directory.CreateDirectory(moduleDataPath);

            _bridge = new YouTubeMusicBridge(_logger, context.GetModuleNativePath(ModuleId), moduleDataPath);
            await UpdateToolStatusAsync(CancellationToken.None).ConfigureAwait(false);

            _isReady = true;
            OnReadyStateChanged?.Invoke(true);

            if (context.ConfigManager.GetBool(ConfigImportOnStartup, false) &&
                ParseUrls(GetPlaylistUrls()).Count > 0)
            {
                await RefreshAsync().ConfigureAwait(false);
            }
        }

        public void OnEnable() { }
        public void OnDisable() { }

        public void OnUnload()
        {
            _refreshCts?.Cancel();
            ClearCache();
        }

        public Task<List<Track>> GetMusicListAsync()
        {
            return Task.FromResult(_context.Library
                .QueryTracks(new TrackQuery { ModuleId = ModuleId, Limit = 0 })
                .ToList());
        }

        public async Task RefreshAsync()
        {
            _refreshCts?.Cancel();
            _refreshCts = new CancellationTokenSource();
            var token = _refreshCts.Token;

            var urls = ParseUrls(GetPlaylistUrls());
            if (urls.Count == 0)
            {
                _context.Library.UnregisterModule(ModuleId);
                _entriesByUuid.Clear();
                _statusText = "Please enter YouTube or YouTube Music URLs first";
                PublishLibraryRefresh();
                return;
            }

            try
            {
                await UpdateToolStatusAsync(token).ConfigureAwait(false);
                _context.Library.UnregisterModule(ModuleId);
                _entriesByUuid.Clear();
                _coverCache.Clear();

                var totalPlaylists = 0;
                var totalTracks = 0;
                var maxItems = Math.Clamp(_context.ConfigManager.GetInt(ConfigMaxItems, 50), 1, 500);

                foreach (var url in urls)
                {
                    token.ThrowIfCancellationRequested();
                    _statusText = $"Importing {url}";
                    PushUI?.Invoke(BuildUI());

                    var playlist = await _bridge.ImportAsync(
                        url,
                        GetYtDlpPath(),
                        GetCookiesPath(),
                        maxItems,
                        token).ConfigureAwait(false);

                    RegisterPlaylist(playlist);
                    totalPlaylists++;
                    totalTracks += playlist.Entries.Count;
                }

                _statusText = $"Imported {totalPlaylists} source(s), {totalTracks} track(s)";
            }
            catch (OperationCanceledException)
            {
                _statusText = "Import cancelled";
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[YouTubeMusic] Refresh failed");
                _statusText = "Import failed: " + ex.Message;
            }

            PublishLibraryRefresh();
        }

        public async Task<PlayableSource> ResolveAsync(
            string uuid,
            AudioQuality quality = AudioQuality.ExHigh,
            CancellationToken cancellationToken = default)
        {
            var entry = ResolveEntry(uuid);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
                return null;

            var playable = await _bridge.ResolvePlayableAsync(
                entry.Url,
                GetYtDlpPath(),
                GetCookiesPath(),
                GetFormatSelector(),
                cancellationToken).ConfigureAwait(false);

            var ext = string.IsNullOrWhiteSpace(playable.Extension) ? "m4a" : playable.Extension;
            var source = new PlayableSource
            {
                UUID = uuid,
                SourceType = PlayableSourceType.Remote,
                Url = playable.Url,
                Format = playable.Format == AudioFormat.Unknown ? AudioFormat.Aac : playable.Format,
                Quality = quality,
                FileSize = playable.FileSize,
                ExpiresAt = playable.ExpiresAt,
                Headers = playable.Headers,
                CacheKey = "ytm_" + uuid
            };

            if (_context.ConfigManager.GetBool(ConfigUseCachePath, true))
            {
                source.CachePath = Path.Combine(
                    CachePaths.GetModuleDirectory("YouTubeMusic"),
                    $"youtube_music_{uuid}.{ext}");
                source.UseCachePath = true;
            }

            return source;
        }

        public Task<PlayableSource> RefreshUrlAsync(
            string uuid,
            AudioQuality quality = AudioQuality.ExHigh,
            CancellationToken cancellationToken = default)
        {
            return ResolveAsync(uuid, quality, cancellationToken);
        }

        public async Task<(byte[] data, string mimeType)> GetMusicCoverAsync(string uuid)
        {
            var track = _context.Library.GetTrack(uuid);
            if (track == null || string.IsNullOrWhiteSpace(track.CoverUri))
                return (_context.DefaultCover.DefaultMusicCover, "image/png");

            var downloaded = await DownloadCoverAsync(track.CoverUri).ConfigureAwait(false);
            return downloaded ?? (_context.DefaultCover.DefaultMusicCover, "image/png");
        }

        public async Task<(byte[] data, string mimeType)> GetAlbumCoverAsync(string albumId)
        {
            await Task.CompletedTask;
            return (_context.DefaultCover.DefaultAlbumCover, "image/png");
        }

        public void ClearCache() => _coverCache.Clear();
        public void RemoveMusicCoverCache(string uuid) { }
        public void RemoveAlbumCoverCache(string albumId) { }

        public SlintNode BuildUI()
        {
            var urls = GetPlaylistUrls();
            var ytDlpPath = GetYtDlpPath();
            var cookiesPath = GetCookiesPath();
            var maxItems = _context?.ConfigManager?.GetInt(ConfigMaxItems, 50) ?? 50;
            var importOnStartup = _context?.ConfigManager?.GetBool(ConfigImportOnStartup, false) ?? false;
            var useCachePath = _context?.ConfigManager?.GetBool(ConfigUseCachePath, true) ?? true;
            var formatSelector = GetFormatSelector();

            return SlintUi.Column(spacing: 14, padding: 20)
                .AddChild(SlintUi.Text("YouTube Music", fontSize: 18))
                .AddChild(SlintUi.Text(_toolStatusText, fontSize: 12, color: ToolStatusColor()))
                .AddChild(SlintUi.Text(_statusText, fontSize: 12, color: "#94a3b8"))
                .AddChild(SlintUi.Text("Import Sources", fontSize: 16))
                .AddChild(SlintUi.Input(
                    "playlist_urls",
                    "One video, playlist, or music.youtube.com URL per line",
                    urls))
                .AddChild(SlintUi.Select("max_items", "Max items per source", maxItems.ToString(), new List<SlintOption>
                {
                    new("25", "25"),
                    new("50", "50"),
                    new("100", "100"),
                    new("200", "200"),
                    new("500", "500")
                }))
                .AddChild(SlintUi.Button("refresh_btn", "Import / Refresh", variant: "primary"))
                .AddChild(SlintUi.Text("Playback Tools", fontSize: 16))
                .AddChild(SlintUi.Input("yt_dlp_path", "yt-dlp.exe path; leave empty to use module directory or PATH", ytDlpPath))
                .AddChild(SlintUi.Input("cookies_path", "cookies.txt path; leave empty for public content", cookiesPath))
                .AddChild(SlintUi.Input("format_selector", "yt-dlp format selector", formatSelector))
                .AddChild(SlintUi.Switch("use_cache_path", "Cache resolved audio files", useCachePath))
                .AddChild(SlintUi.Switch("import_on_startup", "Auto import on startup", importOnStartup))
                .AddChild(SlintUi.Button("download_tool_btn", "Download / Update yt-dlp", variant: null))
                .AddChild(SlintUi.Button("check_tool_btn", "Check yt-dlp", variant: null));
        }

        public SlintNode BuildSettingsUI() => BuildUI();

        public void HandleSettingsUIEvent(string nodeId, string action, string value)
        {
            HandleUIEvent(nodeId, action, value);
        }

        public void HandleUIEvent(string nodeId, string action, string value)
        {
            switch (nodeId)
            {
                case "playlist_urls":
                    _context.ConfigManager.SetValue(ConfigPlaylistUrls, value ?? "");
                    _context.ConfigManager.Save();
                    break;
                case "yt_dlp_path":
                    _context.ConfigManager.SetValue(ConfigYtDlpPath, value ?? "");
                    _context.ConfigManager.Save();
                    _ = CheckToolAndPushAsync();
                    break;
                case "cookies_path":
                    _context.ConfigManager.SetValue(ConfigCookiesPath, value ?? "");
                    _context.ConfigManager.Save();
                    break;
                case "format_selector":
                    _context.ConfigManager.SetValue(ConfigFormatSelector, value ?? "");
                    _context.ConfigManager.Save();
                    break;
                case "max_items":
                    if (int.TryParse(value, out var maxItems))
                    {
                        _context.ConfigManager.SetValue(ConfigMaxItems, Math.Clamp(maxItems, 1, 500));
                        _context.ConfigManager.Save();
                    }
                    break;
                case "use_cache_path":
                    _context.ConfigManager.SetValue(ConfigUseCachePath, ParseBool(value));
                    _context.ConfigManager.Save();
                    break;
                case "import_on_startup":
                    _context.ConfigManager.SetValue(ConfigImportOnStartup, ParseBool(value));
                    _context.ConfigManager.Save();
                    break;
                case "refresh_btn":
                    _ = RefreshAndPushUiAsync();
                    break;
                case "check_tool_btn":
                    _ = CheckToolAndPushAsync();
                    break;
                case "download_tool_btn":
                    _ = DownloadToolAndPushAsync();
                    break;
            }

            PushUI?.Invoke(BuildUI());
        }

        private void RegisterPlaylist(YouTubeMusicPlaylistImport playlist)
        {
            var playlistId = string.IsNullOrWhiteSpace(playlist.Id)
                ? "ytm_playlist_" + YouTubeMusicBridge.HashId(playlist.Url)
                : playlist.Id;

            _context.Library.UpsertPlaylist(new Playlist
            {
                Id = playlistId,
                Name = string.IsNullOrWhiteSpace(playlist.Name) ? "YouTube Music" : playlist.Name,
                ModuleId = ModuleId,
                Kind = PlaylistKind.Imported,
                CoverUri = playlist.Entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.CoverUrl))?.CoverUrl ?? ""
            });

            var entries = new List<PlaylistEntrySpec>();
            var position = 0;
            foreach (var entry in playlist.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Url))
                    continue;

                var uuid = YouTubeMusicBridge.GenerateUuid(
                    string.IsNullOrWhiteSpace(entry.Id) ? entry.Url : entry.Id);
                _entriesByUuid[uuid] = entry;

                _context.Library.UpsertTrack(new Track
                {
                    Uuid = uuid,
                    Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Id : entry.Title,
                    Artist = entry.Artist ?? "",
                    AlbumId = "",
                    SourceType = SourceType.Stream,
                    SourcePath = entry.Url,
                    Duration = entry.Duration,
                    ModuleId = ModuleId,
                    CoverUri = entry.CoverUrl ?? ""
                });

                entries.Add(new PlaylistEntrySpec
                {
                    TrackUuid = uuid,
                    Position = position++
                });
            }

            _context.Library.ReplacePlaylistEntries(playlistId, entries);
        }

        private YouTubeMusicEntry ResolveEntry(string uuid)
        {
            if (_entriesByUuid.TryGetValue(uuid, out var entry))
                return entry;

            var track = _context.Library.GetTrack(uuid);
            if (track == null || string.IsNullOrWhiteSpace(track.SourcePath))
                return null;

            entry = new YouTubeMusicEntry
            {
                Id = uuid,
                Title = track.Title ?? "",
                Artist = track.Artist ?? "",
                Url = track.SourcePath,
                CoverUrl = track.CoverUri ?? "",
                Duration = track.Duration
            };
            _entriesByUuid[uuid] = entry;
            return entry;
        }

        private async Task<(byte[] data, string mimeType)?> DownloadCoverAsync(string url)
        {
            if (_coverCache.TryGetValue(url, out var cached) && cached.data != null)
                return cached;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
                using var response = await CoverClient.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (data.Length == 0)
                    return null;

                var mime = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var result = (data, mime);
                _coverCache[url] = result;
                return result;
            }
            catch
            {
                return null;
            }
        }

        private async Task RefreshAndPushUiAsync()
        {
            await RefreshAsync().ConfigureAwait(false);
            PushUI?.Invoke(BuildUI());
        }

        private async Task CheckToolAndPushAsync()
        {
            await UpdateToolStatusAsync(CancellationToken.None).ConfigureAwait(false);
            PushUI?.Invoke(BuildUI());
        }

        private async Task DownloadToolAndPushAsync()
        {
            try
            {
                _toolStatusText = "Downloading yt-dlp...";
                PushUI?.Invoke(BuildUI());
                await _bridge.DownloadYtDlpAsync(CancellationToken.None).ConfigureAwait(false);
                await UpdateToolStatusAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[YouTubeMusic] yt-dlp download failed");
                _toolStatusText = "yt-dlp download failed: " + ex.Message;
            }
            PushUI?.Invoke(BuildUI());
        }

        private async Task UpdateToolStatusAsync(CancellationToken cancellationToken)
        {
            var status = await _bridge.CheckToolAsync(GetYtDlpPath(), cancellationToken).ConfigureAwait(false);
            _toolStatusText = status.ok ? status.message : "yt-dlp unavailable: " + status.message;
        }

        private string GetPlaylistUrls() => _context?.ConfigManager?.GetString(ConfigPlaylistUrls, "") ?? "";
        private string GetYtDlpPath() => _context?.ConfigManager?.GetString(ConfigYtDlpPath, "") ?? "";
        private string GetCookiesPath() => _context?.ConfigManager?.GetString(ConfigCookiesPath, "") ?? "";
        private string GetFormatSelector()
        {
            return _context?.ConfigManager?.GetString(
                ConfigFormatSelector,
                "bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio/best")
                   ?? "bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio/best";
        }

        private static List<string> ParseUrls(string raw)
        {
            return (raw ?? "")
                .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(line => line.Split(new[] { ' ', '，' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim().Trim('"', '\''))
                .Where(x => x.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            x.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ParseBool(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private string ToolStatusColor()
        {
            return _toolStatusText.StartsWith("yt-dlp unavailable", StringComparison.OrdinalIgnoreCase)
                ? "#f97316"
                : "#4caf50";
        }

        private void PublishLibraryRefresh()
        {
            _context?.EventBus?.Publish(new PlaylistUpdatedEvent
            {
                SourceRefId = ModuleId,
                UpdateType = PlaylistUpdateType.FullRefresh
            });
        }
    }
}
