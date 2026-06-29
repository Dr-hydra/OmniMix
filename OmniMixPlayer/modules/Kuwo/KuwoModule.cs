using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.SDK;
using OmniMixPlayer.SDK.Attributes;
using OmniMixPlayer.SDK.Events;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Module.Kuwo
{
    [MusicModule(ModuleInfo.MODULE_ID, ModuleInfo.MODULE_NAME,
        Version = ModuleInfo.MODULE_VERSION,
        Author = ModuleInfo.MODULE_AUTHOR,
        Description = ModuleInfo.MODULE_DESCRIPTION,
        Priority = 61)]
    public class KuwoModule : IMusicModule, IStreamingMusicSourceProvider, ICoverProvider, ILyricProvider, IModuleUIProvider
    {
        private IModuleContext _context;
        private ILogger _logger;
        private KuwoBridge _bridge;
        private KuwoSongRegistry _registry;
        private readonly Dictionary<string, KuwoSongInfo> _songInfoMap = new();
        private readonly Dictionary<string, (byte[] data, string mimeType)> _coverCache = new();
        private string _statusText = "";
        private bool _isReady;

        public string ModuleId => ModuleInfo.MODULE_ID;
        public string DisplayName => ModuleInfo.MODULE_NAME;
        public string Version => ModuleInfo.MODULE_VERSION;
        public int Priority => 61;
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
            Directory.CreateDirectory(context.GetModuleDataPath(ModuleId));

            _bridge = new KuwoBridge(_logger);
            _registry = new KuwoSongRegistry(_context, ModuleId);

            await RefreshAsync();
            _isReady = true;
            OnReadyStateChanged?.Invoke(true);
        }

        public void OnEnable() { }
        public void OnDisable() { }
        public void OnUnload() => ClearCache();

        public Task<List<Track>> GetMusicListAsync()
        {
            return Task.FromResult(_context.Library.QueryTracks(new TrackQuery { ModuleId = ModuleId, Limit = 0 }).ToList());
        }

        public async Task RefreshAsync()
        {
            var rawIds = _context.ConfigManager.GetValue<string>("PlaylistIds", "");
            var limit = Math.Clamp(_context.ConfigManager.GetValue<int>("PlaylistPageSize", 100), 1, 1000);

            _context.Library.UnregisterModule(ModuleId);
            _songInfoMap.Clear();

            var ids = ParseIds(rawIds);
            if (ids.Count == 0)
            {
                _statusText = "请填写酷我歌单 ID 或链接";
                PublishLibraryRefresh();
                return;
            }

            int playlistCount = 0;
            foreach (var rawId in ids)
            {
                var id = KuwoBridge.ExtractPlaylistId(rawId);
                var playlist = new KuwoPlaylistInfo { Id = id, Name = $"Kuwo {id}" };
                var allSongs = new List<KuwoSongInfo>();

                for (int page = 1; page <= 20; page++)
                {
                    var result = await _bridge.GetPlaylistAsync(id, page, limit);
                    if (result.playlist != null)
                        playlist = result.playlist;
                    if (result.songs.Count == 0)
                        break;

                    foreach (var song in result.songs)
                    {
                        var uuid = KuwoSongRegistry.GenerateUuid(song.Id);
                        _songInfoMap[uuid] = song;
                    }
                    allSongs.AddRange(result.songs);

                    if (result.songs.Count < limit)
                        break;
                }

                _registry.RegisterPlaylist(playlist, allSongs);
                playlistCount++;
            }

            _statusText = $"已导入 {playlistCount} 个歌单，{_songInfoMap.Count} 首歌曲";
            PublishLibraryRefresh();
        }

        public async Task<PlayableSource> ResolveAsync(string uuid, AudioQuality quality = AudioQuality.ExHigh, CancellationToken cancellationToken = default)
        {
            if (!_songInfoMap.TryGetValue(uuid, out var song))
            {
                var track = _context.Library.GetTrack(uuid);
                if (track == null) return null;
                song = new KuwoSongInfo { Id = track.SourcePath, Title = track.Title, Artist = track.Artist, Duration = track.Duration };
            }

            var url = await _bridge.GetPlayableUrlAsync(song.Id, cancellationToken);
            if (string.IsNullOrWhiteSpace(url)) return null;

            return new PlayableSource
            {
                UUID = uuid,
                SourceType = PlayableSourceType.Remote,
                Url = url,
                Format = AudioFormat.Mp3,
                Quality = quality,
                Headers = new Dictionary<string, string>
                {
                    ["User-Agent"] = KuwoBridge.UserAgent,
                    ["Referer"] = "http://www.kuwo.cn/"
                },
                CachePath = Path.Combine(Path.GetTempPath(), "chillpatcher_audio_cache", $"kuwo_{song.Id}.mp3"),
                UseCachePath = true
            };
        }

        public Task<PlayableSource> RefreshUrlAsync(string uuid, AudioQuality quality = AudioQuality.ExHigh, CancellationToken cancellationToken = default)
        {
            return ResolveAsync(uuid, quality, cancellationToken);
        }

        public string GetLyric(string uuid)
        {
            var id = _songInfoMap.TryGetValue(uuid, out var song)
                ? song.Id
                : _context.Library.GetTrack(uuid)?.SourcePath;
            if (string.IsNullOrWhiteSpace(id)) return "";
            try { return _bridge.GetLyricAsync(id).GetAwaiter().GetResult(); }
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
            var ids = _context?.ConfigManager?.GetValue<string>("PlaylistIds", "") ?? "";
            var pageSize = _context?.ConfigManager?.GetValue<int>("PlaylistPageSize", 100) ?? 100;

            return SlintUi.Column(spacing: 14, padding: 20)
                .AddChild(SlintUi.Text("酷我音乐", fontSize: 18))
                .AddChild(SlintUi.Text(string.IsNullOrWhiteSpace(_statusText) ? "填写酷我歌单 ID 或链接后导入。" : _statusText, fontSize: 12, color: "#94a3b8"))
                .AddChild(SlintUi.Input("playlist_ids", "歌单 ID 或链接，逗号分隔", ids))
                .AddChild(SlintUi.Select("playlist_page_size", "每页歌曲数", pageSize.ToString(), new List<SlintOption>
                {
                    new SlintOption("50", "50"),
                    new SlintOption("100", "100"),
                    new SlintOption("200", "200"),
                    new SlintOption("500", "500"),
                }))
                .AddChild(SlintUi.Button("refresh_btn", "导入歌单"));
        }

        public void HandleUIEvent(string nodeId, string action, string value)
        {
            switch (nodeId)
            {
                case "playlist_ids":
                    _context.ConfigManager.SetValue("PlaylistIds", value ?? "");
                    _context.ConfigManager.Save();
                    break;
                case "playlist_page_size":
                    if (int.TryParse(value, out var pageSize))
                    {
                        _context.ConfigManager.SetValue("PlaylistPageSize", Math.Clamp(pageSize, 1, 1000));
                        _context.ConfigManager.Save();
                    }
                    break;
                case "refresh_btn":
                    _ = RefreshAndPushUiAsync();
                    break;
            }

            PushUI?.Invoke(BuildUI());
        }

        public Task<byte[]> ServeRawContent(string path) => Task.FromResult<byte[]>(null);
        public string ServeRawContentType(string path) => null;

        private async Task RefreshAndPushUiAsync()
        {
            await RefreshAsync();
            PushUI?.Invoke(BuildUI());
        }

        private void PublishLibraryRefresh()
        {
            _context?.EventBus?.Publish(new PlaylistUpdatedEvent
            {
                SourceRefId = "kuwo_playlists",
                UpdateType = PlaylistUpdateType.FullRefresh
            });
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

        private static List<string> ParseIds(string raw)
        {
            return (raw ?? "")
                .Split(new[] { ',', '，', ';', '；', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
        }
    }
}
