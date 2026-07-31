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
using OmniMixPlayer.SDK.Caching;
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
        private long _qrImageVersion;
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
            KugouImportDebugLog.Initialize(_logger, Path.Combine(_dataPath, "kugou_debug.log"));
            KugouImportDebugLog.Write($"Module initialized sessionFile={File.Exists(_sessionPath)}, log='{KugouImportDebugLog.Path}'");
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
            var refreshId = Guid.NewGuid().ToString("N").Substring(0, 8);
            KugouImportDebugLog.Write($"[{refreshId}] Refresh begin loggedIn={_session.IsLoggedIn}, userId='{_session.UserId}'");
            _context.Library.UnregisterModule(ModuleId);
            _songInfoMap.Clear();
            _playlists.Clear();

            if (!_session.IsLoggedIn)
            {
                _statusText = "未登录";
                KugouImportDebugLog.Write($"[{refreshId}] Refresh stopped: session is not logged in");
                PublishLibraryRefresh();
                return;
            }

            try
            {
                _session = await _bridge.EnsureRegisteredDeviceAsync(_session);
                _session = await _bridge.RefreshLoginAsync(_session);
                SaveSession();

                var pageSize = Math.Clamp(_context.ConfigManager.GetValue<int>("PlaylistPageSize", 100), 20, 200);
                var importMode = _context.ConfigManager.GetValue<string>("ImportMode", "user");
                var rawPlaylistIds = _context.ConfigManager.GetValue<string>("PlaylistIds", "");
                var manualIds = ParseIds(rawPlaylistIds);
                string importStatus = null;
                KugouImportDebugLog.Write($"[{refreshId}] Import config mode='{importMode}', pageSize={pageSize}, manualIds={manualIds.Count}");

                if (string.Equals(importMode, "manual", StringComparison.OrdinalIgnoreCase))
                {
                    if (manualIds.Count == 0)
                    {
                        _statusText = "未填写歌单 ID";
                        KugouImportDebugLog.Write($"[{refreshId}] Manual import requested but PlaylistIds is empty");
                        PublishLibraryRefresh();
                        return;
                    }

                    var failedIds = new List<string>();
                    foreach (var id in manualIds)
                    {
                        KugouImportDebugLog.Write($"[{refreshId}] Manual playlist start id='{MaskPlaylistIdForLog(id)}'");
                        var playlist = await _bridge.GetPlaylistInfoAsync(id, _session)
                                       ?? new KugouPlaylistInfo { Id = id, Name = $"Kugou {id}" };
                        var songs = await LoadPlaylistSongsAsync(playlist, pageSize);
                        KugouImportDebugLog.Write($"[{refreshId}] Manual playlist done id='{MaskPlaylistIdForLog(playlist.Id)}', name='{playlist.Name ?? ""}', songs={songs.Count}");
                        _playlists.Add(playlist);
                        _registry.RegisterPlaylist(playlist, songs);
                        if (songs.Count == 0)
                            failedIds.Add(id);
                    }

                    if (failedIds.Count > 0)
                        importStatus = $"已导入 {_playlists.Count - failedIds.Count}/{_playlists.Count} 个歌单，{_songInfoMap.Count} 首歌曲；失败：{string.Join(", ", failedIds.Take(3))}";
                }
                else
                {
                    var playlists = await _bridge.GetUserPlaylistsAsync(_session, 1, 100);
                    KugouImportDebugLog.Write($"[{refreshId}] User playlists count={playlists.Count}, first={string.Join(", ", playlists.Take(3).Select(p => $"{MaskPlaylistIdForLog(p.Id)}:{p.Count}"))}");
                    _playlists.AddRange(playlists);
                    foreach (var playlist in playlists)
                    {
                        var songs = await LoadPlaylistSongsAsync(playlist, pageSize);
                        _registry.RegisterPlaylist(playlist, songs);
                    }
                }

                _statusText = importStatus ?? $"已导入 {_playlists.Count} 个歌单，{_songInfoMap.Count} 首歌曲";
                KugouImportDebugLog.Write($"[{refreshId}] Refresh done playlists={_playlists.Count}, tracks={_songInfoMap.Count}");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kugou] Refresh failed");
                KugouImportDebugLog.Write($"[{refreshId}] Refresh failed", ex);
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
            var oldDfid = _session?.Dfid;
            var oldRegisteredAt = _session?.DfidRegisteredAt ?? 0;
            _session = await _bridge.EnsureRegisteredDeviceAsync(_session, cancellationToken: cancellationToken);
            if (SessionDeviceChanged(oldDfid, oldRegisteredAt))
                SaveSession();
            var playable = await _bridge.GetPlayableUrlAsync(song, _session, maxBitrate, cancellationToken);
            if (SessionDeviceChanged(oldDfid, oldRegisteredAt))
                SaveSession();
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
                CachePath = Path.Combine(CachePaths.GetModuleDirectory("Kugou"), $"kugou_{song.Hash}.{playable.Format}"),
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
                .AddChild(SlintUi.Text("酷狗音乐", fontSize: 18))
                .AddChild(SlintUi.Text(loggedIn ? $"已登录: {_session.UserId}" : "未登录", fontSize: 12, color: loggedIn ? "#4caf50" : "#f97316"))
                .AddChild(SlintUi.Text(string.IsNullOrWhiteSpace(_statusText) ? "登录后可导入账号歌单；VIP 内容取决于账号权限和平台返回结果。" : _statusText, fontSize: 12, color: "#94a3b8"));

            if (!loggedIn)
            {
                root.AddChild(SlintUi.Image("qr_image", "/api/modules/" + ModuleId + "/content/qr-image/" + _qrImageVersion, width: 200, height: 200))
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
            KugouImportDebugLog.Write($"UI event node='{nodeId}', action='{action}', valueLength={value?.Length ?? 0}");
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
                    KugouImportDebugLog.Write($"PlaylistIds saved length={value?.Length ?? 0}");
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
            if ((path ?? "").StartsWith("qr-image", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(_qrLogin?.ImageBytes);
            return Task.FromResult<byte[]>(null);
        }

        public string ServeRawContentType(string path)
        {
            return (path ?? "").StartsWith("qr-image", StringComparison.OrdinalIgnoreCase) ? "image/png" : null;
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
            KugouImportDebugLog.Write($"Playlist load start id='{MaskPlaylistIdForLog(playlist.Id)}', name='{playlist.Name ?? ""}', pageSize={pageSize}");
            for (int page = 1; page <= 20; page++)
            {
                var batch = await _bridge.GetPlaylistSongsAsync(playlist.Id, _session, page, pageSize);
                KugouImportDebugLog.Write($"Playlist page id='{MaskPlaylistIdForLog(playlist.Id)}', page={page}, songs={batch.Count}, total={songs.Count + batch.Count}");
                if (batch.Count == 0) break;

                foreach (var song in batch)
                {
                    var uuid = KugouSongRegistry.GenerateUuid(song.Hash, song.AlbumAudioId);
                    _songInfoMap[uuid] = song;
                }
                songs.AddRange(batch);

                if (batch.Count < pageSize) break;
            }
            KugouImportDebugLog.Write($"Playlist load done id='{MaskPlaylistIdForLog(playlist.Id)}', total={songs.Count}");
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
                _qrImageVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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

        private bool SessionDeviceChanged(string oldDfid, long oldRegisteredAt)
        {
            return !string.Equals(oldDfid, _session?.Dfid, StringComparison.Ordinal)
                   || oldRegisteredAt != (_session?.DfidRegisteredAt ?? 0);
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
                .Select(NormalizePlaylistId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
        }

        private static string NormalizePlaylistId(string raw)
        {
            var value = (raw ?? "").Trim().Trim('\'', '"');
            while (value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);

            if (HasQueryValue(value, "global_collection_id")
                && HasQueryValue(value, "uid")
                && HasQueryValue(value, "sign")
                && HasQueryValue(value, "token"))
                return value;

            var globalId = ExtractQueryValue(value, "global_collection_id");
            if (!string.IsNullOrWhiteSpace(globalId))
                return globalId;

            var listId = ExtractQueryValue(value, "listid");
            if (!string.IsNullOrWhiteSpace(listId) && !value.Contains("global_collection_id", StringComparison.OrdinalIgnoreCase))
                return listId;

            return value;
        }

        private static bool HasQueryValue(string value, string name)
        {
            return !string.IsNullOrWhiteSpace(ExtractQueryValue(value, name));
        }

        private static string ExtractQueryValue(string value, string name)
        {
            var queryStart = value.IndexOf('?');
            var query = queryStart >= 0 ? value.Substring(queryStart + 1) : value;
            foreach (var part in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var index = part.IndexOf('=');
                if (index <= 0) continue;
                var key = Uri.UnescapeDataString(part.Substring(0, index));
                if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
                var item = Uri.UnescapeDataString(part.Substring(index + 1)).Trim();
                while (item.EndsWith("/", StringComparison.Ordinal))
                    item = item.Substring(0, item.Length - 1);
                return item;
            }
            return "";
        }

        private static string MaskPlaylistIdForLog(string value)
        {
            value = (value ?? "").Trim();
            var queryStart = value.IndexOf('?');
            if (queryStart < 0) return value;

            var prefix = value.Substring(0, queryStart + 1);
            var query = value.Substring(queryStart + 1)
                .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part =>
                {
                    var index = part.IndexOf('=');
                    if (index <= 0) return part;
                    var key = Uri.UnescapeDataString(part.Substring(0, index));
                    if (string.Equals(key, "token", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "sign", StringComparison.OrdinalIgnoreCase))
                        return key + "=***";
                    return part;
                });
            return prefix + string.Join("&", query);
        }

        private static string FormatSongsForLog(IEnumerable<KugouSongInfo> songs)
        {
            return string.Join(" | ", songs.Select(s => $"{s.Artist} - {s.Title}#{s.Hash}/{s.AlbumAudioId}"));
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
