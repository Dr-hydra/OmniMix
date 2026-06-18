using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OmniMixPlayer.SDK;
using OmniMixPlayer.SDK.Attributes;
using OmniMixPlayer.SDK.Events;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Module.Kugou
{
    [MusicModule(ModuleInfo.MODULE_ID, ModuleInfo.MODULE_NAME,
        Version = ModuleInfo.MODULE_VERSION,
        Author = ModuleInfo.MODULE_AUTHOR,
        Description = ModuleInfo.MODULE_DESCRIPTION,
        Priority = 60)]
    public class KugouModule : IMusicModule, IStreamingMusicSourceProvider, ICoverProvider, ILyricProvider, IModuleUIProvider
    {
        private IModuleContext _context;
        private ILogger _logger;
        private KugouBridge _bridge;
        private KugouSongRegistry _registry;
        private readonly Dictionary<string, KugouSongInfo> _songInfoMap = new();
        private readonly Dictionary<string, (byte[] data, string mimeType)> _coverCache = new();
        private readonly List<KugouPlaylistInfo> _playlists = new();
        private string _dataPath;
        private string _sessionPath;
        private KugouSession _session;
        private KugouQrLoginInfo _qrLogin;
        private CancellationTokenSource _qrPollingCts;
        private string _statusText = "";
        private bool _isReady;

        public string ModuleId => ModuleInfo.MODULE_ID;
        public string DisplayName => ModuleInfo.MODULE_NAME;
        public string Version => ModuleInfo.MODULE_VERSION;
        public int Priority => 60;
        public SourceType SourceType => SourceType.Stream;
        public bool IsReady => _isReady;
        public event Action<bool> OnReadyStateChanged;

        public ModuleCapabilities Capabilities => new ModuleCapabilities
        {
            CanDelete = false,
            CanFavorite = false,
            CanExclude = false,
            ProvidesCover = true,
            ProvidesAlbum = true,
            ProvidesPlaylist = true
        };

        public async Task InitializeAsync(IModuleContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = context.Logger;
            _dataPath = context.GetModuleDataPath(ModuleId);
            Directory.CreateDirectory(_dataPath);
            _sessionPath = Path.Combine(_dataPath, "kugou_session.json");
            _session = LoadSession();

            _bridge = new KugouBridge(_logger);
            _registry = new KugouSongRegistry(_context, ModuleId);

            if (_session.IsLoggedIn)
                await RefreshAsync();

            _isReady = true;
            OnReadyStateChanged?.Invoke(true);
        }

        public void OnEnable() { }
        public void OnDisable() { }
        public void OnUnload()
        {
            _qrPollingCts?.Cancel();
            ClearCache();
        }

        public Task<List<Track>> GetMusicListAsync()
        {
            return Task.FromResult(_context.Library.QueryTracks(new TrackQuery { ModuleId = ModuleId, Limit = 0 }).ToList());
        }

        public async Task RefreshAsync()
        {
            _context.Library.UnregisterModule(ModuleId);
            _songInfoMap.Clear();
            _playlists.Clear();

            if (!_session.IsLoggedIn)
            {
                _statusText = "未登录";
                PublishLibraryRefresh();
                return;
            }

            try
            {
                _session = await _bridge.RefreshLoginAsync(_session);
                SaveSession();

                var pageSize = Math.Clamp(_context.ConfigManager.GetValue<int>("PlaylistPageSize", 100), 20, 200);
                var importMode = _context.ConfigManager.GetValue<string>("ImportMode", "user");
                var manualIds = ParseIds(_context.ConfigManager.GetValue<string>("PlaylistIds", ""));

                if (string.Equals(importMode, "manual", StringComparison.OrdinalIgnoreCase) && manualIds.Count > 0)
                {
                    foreach (var id in manualIds)
                    {
                        var playlist = new KugouPlaylistInfo { Id = id, Name = $"Kugou {id}" };
                        var songs = await LoadPlaylistSongsAsync(playlist, pageSize);
                        _registry.RegisterPlaylist(playlist, songs);
                    }
                }
                else
                {
                    var playlists = await _bridge.GetUserPlaylistsAsync(_session, 1, 100);
                    _playlists.AddRange(playlists);
                    foreach (var playlist in playlists)
                    {
                        var songs = await LoadPlaylistSongsAsync(playlist, pageSize);
                        _registry.RegisterPlaylist(playlist, songs);
                    }
                }

                _statusText = $"已导入 {_playlists.Count} 个歌单，{_songInfoMap.Count} 首歌曲";
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kugou] Refresh failed");
                _statusText = "刷新失败：" + ex.Message;
            }

            PublishLibraryRefresh();
        }

        public async Task<PlayableSource> ResolveAsync(string uuid, AudioQuality quality = AudioQuality.ExHigh, CancellationToken cancellationToken = default)
        {
            if (!_songInfoMap.TryGetValue(uuid, out var song))
            {
                var track = _context.Library.GetTrack(uuid);
                if (track == null) return null;
                song = new KugouSongInfo { Hash = track.SourcePath, Title = track.Title, Artist = track.Artist, Duration = track.Duration };
            }

            var maxBitrate = MapQuality(quality);
            var playable = await _bridge.GetPlayableUrlAsync(song, _session, maxBitrate, cancellationToken);
            if (playable == null || string.IsNullOrWhiteSpace(playable.Url)) return null;

            var format = AudioFormatExtensions.FromExtension(playable.Format);
            return new PlayableSource
            {
                UUID = uuid,
                SourceType = PlayableSourceType.Remote,
                Url = playable.Url,
                Format = format == AudioFormat.Unknown ? AudioFormat.Mp3 : format,
                Quality = quality,
                FileSize = playable.FileSize > 0 ? playable.FileSize : null,
                Headers = new Dictionary<string, string>
                {
                    ["User-Agent"] = KugouBridge.UserAgent
                },
                CachePath = Path.Combine(Path.GetTempPath(), "chillpatcher_audio_cache", $"kugou_{song.Hash}.{playable.Format}"),
                UseCachePath = true
            };
        }

        public Task<PlayableSource> RefreshUrlAsync(string uuid, AudioQuality quality = AudioQuality.ExHigh, CancellationToken cancellationToken = default)
        {
            return ResolveAsync(uuid, quality, cancellationToken);
        }

        public string GetLyric(string uuid)
        {
            var hash = _songInfoMap.TryGetValue(uuid, out var song)
                ? song.Hash
                : _context.Library.GetTrack(uuid)?.SourcePath;
            if (string.IsNullOrWhiteSpace(hash)) return "";
            try { return _bridge.GetLyricAsync(hash).GetAwaiter().GetResult(); }
            catch { return ""; }
        }

        public async Task<(byte[] data, string mimeType)> GetMusicCoverAsync(string uuid)
        {
            var track = _context.Library.GetTrack(uuid);
            if (track == null || string.IsNullOrWhiteSpace(track.CoverUri))
                return (_context.DefaultCover.DefaultMusicCover, "image/png");
            return await DownloadCoverAsync(track.CoverUri) ?? (_context.DefaultCover.DefaultMusicCover, "image/png");
        }

        public async Task<(byte[] data, string mimeType)> GetAlbumCoverAsync(string albumId)
        {
            var album = _context.Library.GetAlbum(albumId);
            if (album == null || string.IsNullOrWhiteSpace(album.CoverUri))
                return (_context.DefaultCover.DefaultAlbumCover, "image/png");
            return await DownloadCoverAsync(album.CoverUri) ?? (_context.DefaultCover.DefaultAlbumCover, "image/png");
        }

        public void ClearCache() => _coverCache.Clear();
        public void RemoveMusicCoverCache(string uuid) { }
        public void RemoveAlbumCoverCache(string albumId) { }

        public bool HasSettingsUI => true;
        public Action<SlintNode> PushUI { get; set; }

        public SlintNode BuildUI()
        {
            var loggedIn = _session?.IsLoggedIn == true;
            var importMode = _context?.ConfigManager?.GetValue<string>("ImportMode", "user") ?? "user";
            var ids = _context?.ConfigManager?.GetValue<string>("PlaylistIds", "") ?? "";
            var pageSize = _context?.ConfigManager?.GetValue<int>("PlaylistPageSize", 100) ?? 100;

            var root = SlintUi.Column(spacing: 14, padding: 20)
                .AddChild(SlintUi.Text("Kugou Music", fontSize: 18))
                .AddChild(SlintUi.Text(loggedIn ? $"已登录: {_session.UserId}" : "未登录", fontSize: 12, color: loggedIn ? "#4caf50" : "#f97316"))
                .AddChild(SlintUi.Text(string.IsNullOrWhiteSpace(_statusText) ? "登录后可导入账号歌单；VIP 内容取决于账号权限和平台返回结果。" : _statusText, fontSize: 12, color: "#94a3b8"));

            if (!loggedIn)
            {
                root.AddChild(SlintUi.Image("qr_image", "/api/modules/" + ModuleId + "/content/qr-image", width: 200, height: 200))
                    .AddChild(SlintUi.Button("qr_login", _qrLogin?.ImageBytes == null ? "获取二维码" : "刷新二维码"));
                return root;
            }

            return root
                .AddChild(SlintUi.Select("import_mode", "导入模式", importMode, new List<SlintOption>
                {
                    new SlintOption("user", "我的歌单"),
                    new SlintOption("manual", "指定歌单 ID"),
                }))
                .AddChild(SlintUi.Input("playlist_ids", "歌单 ID，逗号分隔", ids))
                .AddChild(SlintUi.Select("playlist_page_size", "每页歌曲数", pageSize.ToString(), new List<SlintOption>
                {
                    new SlintOption("50", "50"),
                    new SlintOption("100", "100"),
                    new SlintOption("200", "200"),
                }))
                .AddChild(SlintUi.Button("refresh_btn", "导入歌单"))
                .AddChild(SlintUi.Button("logout_btn", "退出登录", variant: "danger"));
        }

        public void HandleUIEvent(string nodeId, string action, string value)
        {
            switch (nodeId)
            {
                case "qr_login":
                    _ = StartQrLoginAsync();
                    break;
                case "import_mode":
                    _context.ConfigManager.SetValue("ImportMode", value ?? "user");
                    _context.ConfigManager.Save();
                    break;
                case "playlist_ids":
                    _context.ConfigManager.SetValue("PlaylistIds", value ?? "");
                    _context.ConfigManager.Save();
                    break;
                case "playlist_page_size":
                    if (int.TryParse(value, out var pageSize))
                    {
                        _context.ConfigManager.SetValue("PlaylistPageSize", Math.Clamp(pageSize, 20, 200));
                        _context.ConfigManager.Save();
                    }
                    break;
                case "refresh_btn":
                    _ = RefreshAndPushUiAsync();
                    break;
                case "logout_btn":
                    Logout();
                    break;
            }

            PushUI?.Invoke(BuildUI());
        }

        public Task<byte[]> ServeRawContent(string path)
        {
            if (path == "qr-image")
                return Task.FromResult(_qrLogin?.ImageBytes);
            return Task.FromResult<byte[]>(null);
        }

        public string ServeRawContentType(string path)
        {
            return path == "qr-image" ? "image/png" : null;
        }

        private async Task RefreshAndPushUiAsync()
        {
            await RefreshAsync();
            PushUI?.Invoke(BuildUI());
        }

        private void PublishLibraryRefresh()
        {
            _context?.EventBus?.Publish(new PlaylistUpdatedEvent
            {
                SourceRefId = KugouSongRegistry.PLAYLIST_SEARCH,
                UpdateType = PlaylistUpdateType.FullRefresh
            });
        }

        private async Task<List<KugouSongInfo>> LoadPlaylistSongsAsync(KugouPlaylistInfo playlist, int pageSize)
        {
            var songs = new List<KugouSongInfo>();
            for (int page = 1; page <= 20; page++)
            {
                var batch = await _bridge.GetPlaylistSongsAsync(playlist.Id, _session, page, pageSize);
                if (batch.Count == 0) break;

                foreach (var song in batch)
                {
                    var uuid = KugouSongRegistry.GenerateUuid(song.Hash, song.AlbumAudioId);
                    _songInfoMap[uuid] = song;
                }
                songs.AddRange(batch);

                if (batch.Count < pageSize) break;
            }
            return songs;
        }

        private async Task StartQrLoginAsync()
        {
            _qrPollingCts?.Cancel();
            _qrPollingCts = new CancellationTokenSource();
            var token = _qrPollingCts.Token;

            try
            {
                _qrLogin = await _bridge.CreateQrLoginAsync(token);
                _statusText = _qrLogin?.StatusText ?? "二维码创建失败";
                PushUI?.Invoke(BuildUI());
                if (_qrLogin == null) return;

                for (int i = 0; i < 120 && !token.IsCancellationRequested; i++)
                {
                    await Task.Delay(2000, token);
                    var check = await _bridge.CheckQrLoginAsync(_qrLogin.Key, _session, token);
                    _statusText = check.message;
                    if (check.session != null)
                    {
                        _session = check.session;
                        SaveSession();
                        _qrLogin = null;
                        await RefreshAsync();
                        PushUI?.Invoke(BuildUI());
                        return;
                    }
                    if (check.status == 0) break;
                    PushUI?.Invoke(BuildUI());
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _statusText = "登录失败：" + ex.Message;
                _logger?.LogWarning(ex, "[Kugou] QR login failed");
            }

            PushUI?.Invoke(BuildUI());
        }

        private KugouSession LoadSession()
        {
            try
            {
                if (File.Exists(_sessionPath))
                    return JsonConvert.DeserializeObject<KugouSession>(File.ReadAllText(_sessionPath)) ?? new KugouSession();
            }
            catch { }
            return new KugouSession();
        }

        private void SaveSession()
        {
            try { File.WriteAllText(_sessionPath, JsonConvert.SerializeObject(_session, Formatting.Indented)); }
            catch { }
        }

        private void Logout()
        {
            _qrPollingCts?.Cancel();
            _session = new KugouSession();
            _qrLogin = null;
            _statusText = "已退出登录";
            _songInfoMap.Clear();
            _playlists.Clear();
            _context.Library.UnregisterModule(ModuleId);
            try { if (File.Exists(_sessionPath)) File.Delete(_sessionPath); } catch { }
            PublishLibraryRefresh();
        }

        private static List<string> ParseIds(string raw)
        {
            return (raw ?? "")
                .Split(new[] { ',', '，', ';', '；', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
        }

        private async Task<(byte[] data, string mimeType)?> DownloadCoverAsync(string url)
        {
            if (_coverCache.TryGetValue(url, out var cached)) return cached;
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var data = await client.GetByteArrayAsync(url);
                if (data.Length > 0)
                {
                    var result = (data, "image/jpeg");
                    _coverCache[url] = result;
                    return result;
                }
            }
            catch { }
            return null;
        }

        private static int MapQuality(AudioQuality quality)
        {
            return quality switch
            {
                AudioQuality.Standard => 128,
                AudioQuality.Higher => 192,
                _ => 320
            };
        }
    }
}
