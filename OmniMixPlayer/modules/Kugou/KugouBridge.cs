using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using QRCoder;

namespace OmniMixPlayer.Module.Kugou
{
    public class KugouBridge
    {
        public const string UserAgent = "IPhone-8990-searchSong";
        private const int AppId = 1005;
        private const int LoginAppId = 1001;
        private const int QrLoginAppId = 3116;
        private const int SrcAppId = 2919;
        private const int ClientVersion = 20489;
        private const string AndroidUserAgent = "Android15-1070-11083-46-0-DiscoveryDRADProtocol-wifi";
        private const string AndroidSignatureSalt = "OIlwieks28dk2k092lksi2UIkp";
        private const string WebSignatureSalt = "NVPh5oo715z5DIWAeQlhMDsWXXQV4hwt";
        private const string PrivUrlKeySalt = "185672dd44712f60bb1736df5a377e82";

        private readonly ILogger _logger;
        private readonly HttpClient _client;

        public KugouBridge(ILogger logger)
        {
            _logger = logger;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("UNI-UserAgent", "iOS11.4-Phone8990-1009-0-WiFi");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        }

        public async Task<KugouQrLoginInfo> CreateQrLoginAsync(CancellationToken cancellationToken = default)
        {
            var values = BuildDefaultParams(null, web: true);
            values["appid"] = LoginAppId.ToString();
            values["type"] = "1";
            values["plat"] = "4";
            values["qrcode_txt"] = $"https://h5.kugou.com/apps/loginQRCode/html/index.html?appid={QrLoginAppId}&";
            values["srcappid"] = SrcAppId.ToString();
            values["signature"] = SignatureWeb(values);

            var json = await GetStringAsync("https://login-user.kugou.com/v2/qrcode", values, null, cancellationToken);
            var root = JObject.Parse(json);
            var key = PickStringDeep(root, "qrcode", "key", "qrkey");
            if (string.IsNullOrWhiteSpace(key)) return null;
            var imageUrl = BuildQrCodeUrl(key);

            return new KugouQrLoginInfo
            {
                Key = key,
                ImageBytes = GenerateQrPng(imageUrl),
                StatusText = "请使用酷狗音乐扫码登录"
            };
        }

        public async Task<(int status, KugouSession session, string message)> CheckQrLoginAsync(string key, KugouSession current, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return (0, null, "二维码无效");

            var values = BuildDefaultParams(current, web: true);
            values["appid"] = QrLoginAppId.ToString();
            values["plat"] = "4";
            values["srcappid"] = SrcAppId.ToString();
            values["qrcode"] = key;
            values["signature"] = SignatureWeb(values);

            var json = await GetStringAsync("https://login-user.kugou.com/v2/get_userinfo_qrcode", values, current, cancellationToken);
            var root = JObject.Parse(json);
            var status = PickQrStatus(root);

            if (status == 4)
            {
                var session = EnsureSession(current);
                session.Token = PickStringDeep(root, "token") ?? session.Token;
                session.UserId = PickStringDeep(root, "userid", "user_id", "uid") ?? session.UserId;
                session.VipType = PickStringDeep(root, "vip_type", "viptype") ?? session.VipType;
                session.VipToken = PickStringDeep(root, "vip_token", "viptoken") ?? session.VipToken;
                session.LoginTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (!session.IsLoggedIn)
                    return (status, null, "扫码已确认，但未取得有效登录凭据，请刷新二维码重试");
                return (status, session, "登录成功");
            }

            var message = status switch
            {
                1 => "等待扫码",
                2 => "已扫码，请在手机上确认",
                0 or -1 => "二维码已过期",
                _ => root["error_msg"]?.ToString() ?? root["msg"]?.ToString() ?? $"状态 {status}"
            };
            return (status, null, message);
        }

        public async Task<KugouSession> RefreshLoginAsync(KugouSession session, CancellationToken cancellationToken = default)
        {
            session = EnsureSession(session);
            if (!session.IsLoggedIn) return session;

            var values = BuildDefaultParams(session);
            values["plat"] = "1";
            values["userid"] = session.UserId;
            values["token"] = session.Token;
            values["signature"] = SignatureAndroid(values, "");

            var data = new Dictionary<string, object>
            {
                ["userid"] = session.UserId,
                ["token"] = session.Token,
                ["total_ver"] = 979,
                ["type"] = 2,
                ["page"] = 1,
                ["pagesize"] = 1
            };

            try
            {
                await PostJsonAsync("https://gateway.kugou.com/v7/get_all_list", values, data, session, cancellationToken,
                    new Dictionary<string, string> { ["x-router"] = "cloudlist.service.kugou.com" });
            }
            catch
            {
                // A lightweight authenticated call is enough to keep existing token behavior observable.
            }

            return session;
        }

        public async Task<List<KugouPlaylistInfo>> GetUserPlaylistsAsync(KugouSession session, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            session = EnsureSession(session);
            if (!session.IsLoggedIn) return new List<KugouPlaylistInfo>();

            var data = new Dictionary<string, object>
            {
                ["userid"] = session.UserId,
                ["token"] = session.Token,
                ["total_ver"] = 979,
                ["type"] = 2,
                ["page"] = Math.Max(1, page),
                ["pagesize"] = Math.Clamp(pageSize, 1, 100)
            };
            var body = ToJson(data);
            var values = BuildDefaultParams(session);
            values["plat"] = "1";
            values["userid"] = session.UserId;
            values["token"] = session.Token;
            values["signature"] = SignatureAndroid(values, body);

            var json = await PostJsonAsync("https://gateway.kugou.com/v7/get_all_list", values, data, session, cancellationToken,
                new Dictionary<string, string> { ["x-router"] = "cloudlist.service.kugou.com" });
            return ParsePlaylistList(JObject.Parse(json));
        }

        public async Task<List<KugouSongInfo>> GetPlaylistSongsAsync(string listId, KugouSession session, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(listId)) return new List<KugouSongInfo>();
            session = EnsureSession(session);

            var playlistInput = NormalizePlaylistInput(listId);
            var publicId = ExtractQueryValue(playlistInput, "global_collection_id");
            if (string.IsNullOrWhiteSpace(publicId) && IsPublicCollectionId(playlistInput))
                publicId = playlistInput.TrimEnd('/');
            KugouImportDebugLog.Write($"Bridge.GetPlaylistSongs input='{MaskPlaylistInput(playlistInput)}', publicId='{publicId ?? ""}', page={Math.Max(1, page)}, pageSize={Math.Clamp(pageSize, 1, 200)}, loggedIn={session.IsLoggedIn}, userId='{session.UserId}'");

            if (!string.IsNullOrWhiteSpace(publicId))
            {
                KugouImportDebugLog.Write($"Try endpoint=mobile_shared id='{publicId}', page={Math.Max(1, page)}");
                var sharedSongs = await GetMobileSharedPlaylistSongsAsync(playlistInput, publicId, page, cancellationToken);
                KugouImportDebugLog.Write($"Endpoint result mobile_shared id='{publicId}', page={Math.Max(1, page)}, songs={sharedSongs.Count}");
                if (sharedSongs.Count > 0) return sharedSongs;

                KugouImportDebugLog.Write($"Try endpoint=public_playlist id='{publicId}', page={Math.Max(1, page)}");
                var publicSongs = await GetPublicPlaylistSongsAsync(publicId, session, page, pageSize, cancellationToken);
                KugouImportDebugLog.Write($"Endpoint result public_playlist id='{publicId}', page={Math.Max(1, page)}, songs={publicSongs.Count}");
                if (publicSongs.Count > 0) return publicSongs;
            }

            KugouImportDebugLog.Write($"Try endpoint=account_playlist listId='{MaskPlaylistInput(playlistInput)}', page={Math.Max(1, page)}");
            var data = new Dictionary<string, object>
            {
                ["listid"] = playlistInput,
                ["userid"] = session.UserId,
                ["area_code"] = 1,
                ["show_relate_goods"] = 0,
                ["pagesize"] = Math.Clamp(pageSize, 1, 200),
                ["allplatform"] = 1,
                ["show_cover"] = 1,
                ["type"] = 0,
                ["token"] = session.Token,
                ["page"] = Math.Max(1, page)
            };
            var body = ToJson(data);
            var values = BuildDefaultParams(session);
            values["signature"] = SignatureAndroid(values, body);
            KugouImportDebugLog.Write($"Account playlist request params={FormatParamsForLog(values)}, body={body}");

            var json = await PostJsonAsync("https://gateway.kugou.com/v4/get_list_all_file", values, data, session, cancellationToken,
                new Dictionary<string, string> { ["x-router"] = "cloudlist.service.kugou.com" });
            LogJsonSummary("Account playlist endpoint", json);
            var songs = ParseSongs(JObject.Parse(json));
            KugouImportDebugLog.Write($"Endpoint result account_playlist listId='{MaskPlaylistInput(playlistInput)}', page={Math.Max(1, page)}, songs={songs.Count}, sample={FormatSongsForLog(songs.Take(3))}");
            return songs;
        }

        public async Task<KugouPlaylistInfo> GetPlaylistInfoAsync(string playlistIdOrLink, KugouSession session, CancellationToken cancellationToken = default)
        {
            var playlistInput = NormalizePlaylistInput(playlistIdOrLink);
            var publicId = ExtractQueryValue(playlistInput, "global_collection_id");
            if (string.IsNullOrWhiteSpace(publicId) && IsPublicCollectionId(playlistInput))
                publicId = playlistInput.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(publicId)) return null;

            try
            {
                session = EnsureSession(session);
                var data = new Dictionary<string, object>
                {
                    ["data"] = new[] { new Dictionary<string, object> { ["global_collection_id"] = publicId } },
                    ["userid"] = session.UserId,
                    ["token"] = session.Token ?? ""
                };
                var body = ToJson(data);
                var values = BuildDefaultParams(session);
                values["signature"] = SignatureAndroid(values, body);
                KugouImportDebugLog.Write($"Public playlist detail request id='{publicId}', params={FormatParamsForLog(values)}, body={body}");

                var json = await PostJsonAsync("https://gateway.kugou.com/v3/get_list_info", values, data, session, cancellationToken,
                    new Dictionary<string, string> { ["x-router"] = "pubsongs.kugou.com" });
                LogJsonSummary("Public playlist detail endpoint", json);
                var playlist = ParsePlaylistList(JObject.Parse(json)).FirstOrDefault();
                KugouImportDebugLog.Write($"Public playlist detail result id='{publicId}', found={playlist != null}, name='{playlist?.Name ?? ""}', count={playlist?.Count ?? 0}, cover='{playlist?.CoverUrl ?? ""}'");
                return playlist;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kugou] Public playlist detail failed: {PlaylistId}", publicId);
                KugouImportDebugLog.Write($"Public playlist detail failed id='{publicId}'", ex);
                return null;
            }
        }

        public async Task<KugouSearchResult> SearchAsync(string keyword, int page, int limit, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return KugouSearchResult.Ok(new List<KugouSongInfo>());

            try
            {
                var query = new Dictionary<string, string>
                {
                    ["api_ver"] = "1",
                    ["area_code"] = "1",
                    ["correct"] = "1",
                    ["pagesize"] = Math.Max(1, limit).ToString(),
                    ["plat"] = "2",
                    ["tag"] = "1",
                    ["sver"] = "5",
                    ["showtype"] = "10",
                    ["page"] = Math.Max(1, page).ToString(),
                    ["keyword"] = keyword,
                    ["version"] = "8990"
                };

                var url = "http://mobilecdn.kugou.com/api/v3/search/song?" + ToQueryString(query);
                var json = await _client.GetStringAsync(url, cancellationToken);
                var root = JObject.Parse(json);
                var list = root["data"]?["info"] as JArray;
                var songs = new List<KugouSongInfo>();

                foreach (var item in list ?? new JArray())
                {
                    var hash = item["hash"]?.ToString();
                    if (string.IsNullOrWhiteSpace(hash)) continue;

                    var filename = item["filename"]?.ToString() ?? item["fileName"]?.ToString() ?? "";
                    var (artist, title) = SplitArtistTitle(filename);
                    var album = item["album_name"]?.ToString() ?? "";
                    var duration = item["duration"]?.ToObject<float?>() ?? item["time"]?.ToObject<float?>() ?? 0;

                    songs.Add(new KugouSongInfo
                    {
                        Hash = hash,
                        AlbumAudioId = item["album_audio_id"]?.ToObject<long?>() ?? 0,
                        AlbumId = item["album_id"]?.ToObject<long?>() ?? 0,
                        Title = title,
                        Artist = artist,
                        Album = album,
                        CoverUrl = NormalizeCover(item["album_img"]?.ToString() ?? item["imgUrl"]?.ToString()),
                        Duration = duration
                    });
                }

                return KugouSearchResult.Ok(songs);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kugou] Search failed: {Keyword}", keyword);
                return KugouSearchResult.Failed(ex.Message);
            }
        }

        public async Task<KugouPlayableUrl> GetPlayableUrlAsync(string hash, int maxBitrate, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(hash)) return null;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "http://media.store.kugou.com/v1/get_res_privilege");
                var body = new JObject
                {
                    ["relate"] = 1,
                    ["userid"] = "0",
                    ["vip"] = 0,
                    ["appid"] = 1000,
                    ["token"] = "",
                    ["behavior"] = "download",
                    ["area_code"] = "1",
                    ["clientver"] = "8990",
                    ["resource"] = new JArray(new JObject
                    {
                        ["id"] = 0,
                        ["type"] = "audio",
                        ["hash"] = hash
                    })
                };
                request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

                var response = await _client.SendAsync(request, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                response.EnsureSuccessStatusCode();

                var root = JObject.Parse(json);
                var goods = root["data"]?[0]?["relate_goods"] as JArray;
                if (goods == null || goods.Count == 0) return null;

                JObject best = null;
                var bestBitrate = 0;
                foreach (var item in goods)
                {
                    var info = item["info"];
                    var bitrate = info?["bitrate"]?.ToObject<int?>() ?? 0;
                    if (bitrate <= 0 || bitrate > maxBitrate) continue;
                    if (bitrate > bestBitrate)
                    {
                        best = (JObject)item;
                        bestBitrate = bitrate;
                    }
                }

                best ??= goods.OfType<JObject>().FirstOrDefault();
                var bestHash = best?["hash"]?.ToString();
                if (string.IsNullOrWhiteSpace(bestHash)) return null;

                return await GetTrackerUrlAsync(bestHash, cancellationToken)
                    ?? await GetMobilePlayUrlAsync(hash, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kugou] Resolve URL failed: {Hash}", hash);
                return null;
            }
        }

        public async Task<KugouPlayableUrl> GetPlayableUrlAsync(KugouSongInfo song, KugouSession session, int maxBitrate, CancellationToken cancellationToken = default)
        {
            if (song == null || string.IsNullOrWhiteSpace(song.Hash)) return null;
            if (session?.IsLoggedIn == true)
            {
                var loginUrl = await GetPrivilegedUrlAsync(song, session, cancellationToken);
                if (loginUrl != null && !string.IsNullOrWhiteSpace(loginUrl.Url))
                    return loginUrl;
            }

            return await GetPlayableUrlAsync(song.Hash, maxBitrate, cancellationToken);
        }

        public async Task<string> GetLyricAsync(string hash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(hash)) return "";

            try
            {
                var searchUrl = "http://krcs.kugou.com/search?" + ToQueryString(new Dictionary<string, string>
                {
                    ["keyword"] = "%20-%20",
                    ["ver"] = "1",
                    ["hash"] = hash,
                    ["client"] = "mobi",
                    ["man"] = "yes"
                });

                var searchJson = await _client.GetStringAsync(searchUrl, cancellationToken);
                var candidate = JObject.Parse(searchJson)["candidates"]?.FirstOrDefault();
                var accessKey = candidate?["accesskey"]?.ToString();
                var id = candidate?["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(id)) return "";

                var downloadUrl = "http://lyrics.kugou.com/download?" + ToQueryString(new Dictionary<string, string>
                {
                    ["charset"] = "utf8",
                    ["accesskey"] = accessKey,
                    ["id"] = id,
                    ["client"] = "mobi",
                    ["fmt"] = "lrc",
                    ["ver"] = "1"
                });

                var json = await _client.GetStringAsync(downloadUrl, cancellationToken);
                var content = JObject.Parse(json)["content"]?.ToString();
                if (string.IsNullOrWhiteSpace(content)) return "";
                return Encoding.UTF8.GetString(Convert.FromBase64String(content));
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kugou] Lyric failed: {Hash}", hash);
                return "";
            }
        }

        private async Task<KugouPlayableUrl> GetTrackerUrlAsync(string hash, CancellationToken cancellationToken)
        {
            var url = "http://trackercdn.kugou.com/i/v2/?" + ToQueryString(new Dictionary<string, string>
            {
                ["hash"] = hash,
                ["key"] = Md5Hex(hash + "kgcloudv2"),
                ["pid"] = "3",
                ["behavior"] = "download",
                ["cmd"] = "25"
            });

            var json = await _client.GetStringAsync(url, cancellationToken);
            var root = JObject.Parse(json);
            var urls = root["url"] as JArray;
            var playUrl = urls?.FirstOrDefault()?.ToString() ?? root["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(playUrl)) return null;

            return new KugouPlayableUrl
            {
                Url = playUrl,
                Bitrate = (root["bitRate"]?.ToObject<int?>() ?? 0) / 1000,
                FileSize = root["fileSize"]?.ToObject<long?>() ?? 0,
                Format = InferFormat(playUrl)
            };
        }

        private async Task<List<KugouSongInfo>> GetPublicPlaylistSongsAsync(string globalCollectionId, KugouSession session, int page, int pageSize, CancellationToken cancellationToken)
        {
            try
            {
                session = EnsureSession(session);
                var values = BuildDefaultParams(session);
                values["area_code"] = "1";
                values["begin_idx"] = ((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200)).ToString();
                values["plat"] = "1";
                values["type"] = "1";
                values["mode"] = "1";
                values["personal_switch"] = "1";
                values["extend_fields"] = "abtags,hot_cmt,popularization";
                values["pagesize"] = Math.Clamp(pageSize, 1, 200).ToString();
                values["global_collection_id"] = globalCollectionId;
                values["signature"] = SignatureAndroid(values, "");
                KugouImportDebugLog.Write($"Public playlist request id='{globalCollectionId}', page={Math.Max(1, page)}, params={FormatParamsForLog(values)}");

                var json = await GetStringAsync("https://gateway.kugou.com/pubsongs/v2/get_other_list_file_nofilt", values, session, cancellationToken);
                LogJsonSummary("Public playlist endpoint", json);
                if (string.IsNullOrWhiteSpace(json)) return new List<KugouSongInfo>();
                var songs = ParseSongs(JObject.Parse(json));
                KugouImportDebugLog.Write($"Public playlist parsed id='{globalCollectionId}', page={Math.Max(1, page)}, songs={songs.Count}, sample={FormatSongsForLog(songs.Take(3))}");
                return songs;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kugou] Public playlist songs failed: {PlaylistId}, page={Page}", globalCollectionId, Math.Max(1, page));
                KugouImportDebugLog.Write($"Public playlist songs failed id='{globalCollectionId}', page={Math.Max(1, page)}", ex);
                return new List<KugouSongInfo>();
            }
        }

        private async Task<List<KugouSongInfo>> GetMobileSharedPlaylistSongsAsync(string playlistInput, string globalCollectionId, int page, CancellationToken cancellationToken)
        {
            try
            {
                var query = ParseQueryValues(playlistInput);
                var required = new[] { "uid", "sign", "_t", "token" };
                if (required.Any(name => string.IsNullOrWhiteSpace(GetQueryValue(query, name))))
                {
                    KugouImportDebugLog.Write($"Mobile shared skipped id='{globalCollectionId}': missing params hasUid={!string.IsNullOrWhiteSpace(GetQueryValue(query, "uid"))}, hasSign={!string.IsNullOrWhiteSpace(GetQueryValue(query, "sign"))}, hasTime={!string.IsNullOrWhiteSpace(GetQueryValue(query, "_t"))}, hasToken={!string.IsNullOrWhiteSpace(GetQueryValue(query, "token"))}, input='{MaskPlaylistInput(playlistInput)}'");
                    return new List<KugouSongInfo>();
                }

                query["listid"] = "2";
                query["type"] = "0";
                query["global_collection_id"] = globalCollectionId;
                query["page"] = Math.Max(1, page).ToString();

                var url = "https://m3ws.kugou.com/zlist/list?" + ToQueryString(query);
                KugouImportDebugLog.Write($"Mobile shared request url='{MaskPlaylistInput(url)}'");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 Mobile Safari/537.36");
                using var response = await _client.SendAsync(request, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                KugouImportDebugLog.Write($"Mobile shared HTTP status={(int)response.StatusCode}, id='{globalCollectionId}', page={Math.Max(1, page)}");
                response.EnsureSuccessStatusCode();
                LogJsonSummary("Mobile shared playlist endpoint", json);
                if (string.IsNullOrWhiteSpace(json)) return new List<KugouSongInfo>();
                var songs = ParseSongs(JObject.Parse(json));
                KugouImportDebugLog.Write($"Mobile shared parsed id='{globalCollectionId}', page={Math.Max(1, page)}, songs={songs.Count}, sample={FormatSongsForLog(songs.Take(3))}");
                return songs;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kugou] Mobile shared playlist songs failed: {PlaylistId}, page={Page}", globalCollectionId, Math.Max(1, page));
                KugouImportDebugLog.Write($"Mobile shared songs failed id='{globalCollectionId}', page={Math.Max(1, page)}", ex);
                return new List<KugouSongInfo>();
            }
        }

        private async Task<KugouPlayableUrl> GetMobilePlayUrlAsync(string hash, CancellationToken cancellationToken)
        {
            var url = "https://m.kugou.com/app/i/getSongInfo.php?" + ToQueryString(new Dictionary<string, string>
            {
                ["cmd"] = "playInfo",
                ["hash"] = hash,
                ["from"] = "mkugou"
            });

            var json = await _client.GetStringAsync(url, cancellationToken);
            var root = JObject.Parse(json);
            var playUrl = root["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(playUrl))
            {
                var backup = root["backup_url"] as JArray;
                playUrl = backup?.FirstOrDefault()?.ToString();
            }

            if (string.IsNullOrWhiteSpace(playUrl)) return null;

            return new KugouPlayableUrl
            {
                Url = playUrl,
                Bitrate = root["bitrate"]?.ToObject<int?>() ?? 0,
                FileSize = root["fileSize"]?.ToObject<long?>() ?? 0,
                Format = InferFormat(playUrl)
            };
        }

        private async Task<KugouPlayableUrl> GetPrivilegedUrlAsync(KugouSongInfo song, KugouSession session, CancellationToken cancellationToken)
        {
            try
            {
                session = EnsureSession(session);
                var clienttimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var data = new Dictionary<string, object>
                {
                    ["area_code"] = "1",
                    ["behavior"] = "play",
                    ["qualities"] = new[] { "128", "320", "flac", "high", "multitrack", "viper_atmos", "viper_tape", "viper_clear", "super" },
                    ["resource"] = new Dictionary<string, object>
                    {
                        ["album_audio_id"] = song.AlbumAudioId,
                        ["collect_list_id"] = "3",
                        ["collect_time"] = clienttimeMs,
                        ["hash"] = song.Hash,
                        ["id"] = 0,
                        ["page_id"] = 1,
                        ["type"] = "audio"
                    },
                    ["token"] = session.Token,
                    ["tracker_param"] = new Dictionary<string, object>
                    {
                        ["all_m"] = 1,
                        ["auth"] = "",
                        ["is_free_part"] = 0,
                        ["key"] = Md5Hex($"{song.Hash}{PrivUrlKeySalt}{AppId}{session.Mid}{session.UserId}"),
                        ["module_id"] = 0,
                        ["need_climax"] = 1,
                        ["need_xcdn"] = 1,
                        ["open_time"] = "",
                        ["pid"] = "411",
                        ["pidversion"] = "3001",
                        ["priv_vip_type"] = "6",
                        ["viptoken"] = session.VipToken ?? ""
                    },
                    ["userid"] = session.UserId,
                    ["vip"] = session.VipType ?? "0"
                };
                var body = ToJson(data);
                var values = BuildDefaultParams(session);
                values["signature"] = SignatureAndroid(values, body);

                var json = await PostJsonAsync("http://tracker.kugou.com/v6/priv_url", values, data, session, cancellationToken);
                var root = JObject.Parse(json);
                var url = FindFirstUrl(root);
                if (string.IsNullOrWhiteSpace(url)) return null;

                return new KugouPlayableUrl
                {
                    Url = url,
                    Bitrate = 0,
                    FileSize = 0,
                    Format = InferFormat(url)
                };
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kugou] Privileged URL failed: {Hash}", song.Hash);
                return null;
            }
        }

        private async Task<string> GetStringAsync(string baseUrl, Dictionary<string, string> values, KugouSession session, CancellationToken cancellationToken, Dictionary<string, string> extraHeaders = null)
        {
            var url = baseUrl + "?" + ToQueryString(values);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAndroidHeaders(request, values, session, extraHeaders);
            using var response = await _client.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return json;
        }

        private async Task<string> PostJsonAsync(string baseUrl, Dictionary<string, string> values, Dictionary<string, object> data, KugouSession session, CancellationToken cancellationToken, Dictionary<string, string> extraHeaders = null)
        {
            var url = baseUrl + "?" + ToQueryString(values);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(ToJson(data), Encoding.UTF8, "application/json");
            ApplyAndroidHeaders(request, values, session, extraHeaders);
            using var response = await _client.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return json;
        }

        private static void ApplyAndroidHeaders(HttpRequestMessage request, Dictionary<string, string> values, KugouSession session, Dictionary<string, string> extraHeaders)
        {
            session = EnsureSession(session);
            request.Headers.TryAddWithoutValidation("User-Agent", AndroidUserAgent);
            request.Headers.TryAddWithoutValidation("dfid", session.Dfid);
            request.Headers.TryAddWithoutValidation("mid", session.Mid);
            request.Headers.TryAddWithoutValidation("clienttime", values.TryGetValue("clienttime", out var time) ? time : DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            request.Headers.TryAddWithoutValidation("kg-rc", "1");
            request.Headers.TryAddWithoutValidation("kg-thash", "5d816a0");
            request.Headers.TryAddWithoutValidation("kg-rec", "1");
            request.Headers.TryAddWithoutValidation("kg-rf", "B9EDA08A64250DEFFBCADDEE00F8F25F");
            if (session.IsLoggedIn)
                request.Headers.TryAddWithoutValidation("Cookie", ToCookieHeader(session));
            if (extraHeaders != null)
            {
                foreach (var kv in extraHeaders)
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        private static Dictionary<string, string> BuildDefaultParams(KugouSession session, bool web = false)
        {
            session = EnsureSession(session);
            var values = new Dictionary<string, string>
            {
                ["dfid"] = session.Dfid,
                ["mid"] = session.Mid,
                ["uuid"] = "-",
                ["appid"] = (web ? LoginAppId : AppId).ToString(),
                ["clientver"] = ClientVersion.ToString(),
                ["clienttime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
            };

            if (!string.IsNullOrWhiteSpace(session.Token))
                values["token"] = session.Token;
            if (!string.IsNullOrWhiteSpace(session.UserId) && session.UserId != "0")
                values["userid"] = session.UserId;

            return values;
        }

        private static KugouSession EnsureSession(KugouSession session)
        {
            session ??= new KugouSession();
            if (string.IsNullOrWhiteSpace(session.Guid))
                session.Guid = Guid.NewGuid().ToString("D").ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(session.Dfid))
                session.Dfid = RandomString(24);
            if (string.IsNullOrWhiteSpace(session.Mid) || session.Mid == "0")
                session.Mid = Md5Hex(session.Guid);
            if (string.IsNullOrWhiteSpace(session.UserId))
                session.UserId = "0";
            return session;
        }

        private static string SignatureAndroid(Dictionary<string, string> values, string data)
        {
            var joined = string.Join("", values
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));
            return Md5Hex(AndroidSignatureSalt + joined + (data ?? "") + AndroidSignatureSalt);
        }

        private static string SignatureWeb(Dictionary<string, string> values)
        {
            var joined = string.Join("", values
                .OrderBy(kv => $"{kv.Key}={kv.Value}", StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));
            return Md5Hex(WebSignatureSalt + joined + WebSignatureSalt);
        }

        private static string ToJson(Dictionary<string, object> data)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.None);
        }

        private static string ToCookieHeader(KugouSession session)
        {
            var parts = new List<string>
            {
                $"userid={session.UserId}",
                $"token={session.Token}",
                $"dfid={session.Dfid}",
                $"KUGOU_API_MID={session.Mid}",
                $"vip_type={session.VipType ?? "0"}"
            };
            if (!string.IsNullOrWhiteSpace(session.VipToken))
                parts.Add($"vip_token={session.VipToken}");
            return string.Join("; ", parts);
        }

        private static List<KugouPlaylistInfo> ParsePlaylistList(JObject root)
        {
            var result = new List<KugouPlaylistInfo>();
            foreach (var item in FindArrays(root, "lists", "list", "info").SelectMany(a => a.Children<JObject>()))
            {
                var id = item["global_collection_id"]?.ToString()
                         ?? item["listid"]?.ToString()
                         ?? item["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                result.Add(new KugouPlaylistInfo
                {
                    Id = id,
                    Name = item["name"]?.ToString() ?? item["listname"]?.ToString() ?? item["title"]?.ToString() ?? id,
                    CoverUrl = NormalizeCover(item["pic"]?.ToString() ?? item["img"]?.ToString() ?? item["cover"]?.ToString()),
                    Count = item["count"]?.ToObject<int?>() ?? item["total"]?.ToObject<int?>() ?? 0
                });
            }
            return result.GroupBy(p => p.Id).Select(g => g.First()).ToList();
        }

        private static List<KugouSongInfo> ParseSongs(JObject root)
        {
            var result = new List<KugouSongInfo>();
            foreach (var item in FindArrays(root, "songs", "list", "info", "files").SelectMany(a => a.Children<JObject>()))
            {
                var hash = item["hash"]?.ToString() ?? item["audio_hash"]?.ToString();
                if (string.IsNullOrWhiteSpace(hash)) continue;
                var name = item["name"]?.ToString() ?? item["filename"]?.ToString() ?? item["fileName"]?.ToString() ?? "";
                var (artist, title) = SplitArtistTitle(name);
                if (string.IsNullOrWhiteSpace(artist))
                    artist = item["singername"]?.ToString() ?? item["author_name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(artist) && item["singerinfo"] is JArray singers)
                    artist = string.Join(", ", singers.Children<JObject>()
                        .Select(s => s["name"]?.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                if (string.IsNullOrWhiteSpace(title))
                    title = item["songname"]?.ToString() ?? item["song_name"]?.ToString() ?? name;

                result.Add(new KugouSongInfo
                {
                    Hash = hash,
                    AlbumAudioId = item["album_audio_id"]?.ToObject<long?>() ?? item["audio_id"]?.ToObject<long?>() ?? 0,
                    AlbumId = item["album_id"]?.ToObject<long?>() ?? 0,
                    Title = title,
                    Artist = artist,
                    Album = item["albumname"]?.ToString()
                            ?? item["album_name"]?.ToString()
                            ?? item["albuminfo"]?["name"]?.ToString()
                            ?? "",
                    CoverUrl = NormalizeCover(item["image"]?.ToString()
                                              ?? item["img"]?.ToString()
                                              ?? item["album_img"]?.ToString()
                                              ?? item["cover"]?.ToString()
                                              ?? item["trans_param"]?["union_cover"]?.ToString()),
                    Duration = ParseDuration(item["duration"]?.ToString()
                                             ?? item["time_length"]?.ToString()
                                             ?? item["timelen"]?.ToString()
                                             ?? item["timeLen"]?.ToString()
                                             ?? item["duration_ms"]?.ToString())
                });
            }
            return result;
        }

        private static IEnumerable<JArray> FindArrays(JToken token, params string[] names)
        {
            if (token == null) yield break;
            if (token is JArray arr)
            {
                yield return arr;
                yield break;
            }
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    if (names.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value is JArray namedArray)
                        yield return namedArray;
                    foreach (var nested in FindArrays(prop.Value, names))
                        yield return nested;
                }
            }
        }

        private static string FindFirstUrl(JToken token)
        {
            if (token == null) return null;
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop.Name.Equals("url", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value is JArray arr)
                        {
                            var first = arr.FirstOrDefault()?.ToString();
                            if (!string.IsNullOrWhiteSpace(first)) return first;
                        }
                        var value = prop.Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            return value;
                    }
                    var nested = FindFirstUrl(prop.Value);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            if (token is JArray array)
            {
                foreach (var child in array)
                {
                    var nested = FindFirstUrl(child);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            return null;
        }

        private static string BuildQrCodeUrl(string key)
        {
            return "https://h5.kugou.com/apps/loginQRCode/html/index.html?qrcode=" + Uri.EscapeDataString(key ?? "");
        }

        private static byte[] GenerateQrPng(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
            var qr = new PngByteQRCode(data);
            return qr.GetGraphic(8);
        }

        private static string PickStringDeep(JToken token, params string[] names)
        {
            if (token == null) return null;
            if (token is JObject obj)
            {
                foreach (var name in names)
                {
                    var value = obj.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                foreach (var prop in obj.Properties())
                {
                    var nested = PickStringDeep(prop.Value, names);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            if (token is JArray array)
            {
                foreach (var child in array)
                {
                    var nested = PickStringDeep(child, names);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            return null;
        }

        private static int PickIntDeep(JToken token, string name, int fallback)
        {
            var value = PickStringDeep(token, name);
            return int.TryParse(value, out var result) ? result : fallback;
        }

        private static int PickQrStatus(JObject root)
        {
            var dataStatus = root?["data"]?["status"]?.ToString();
            if (int.TryParse(dataStatus, out var status)) return status;
            return PickIntDeep(root, "status", -1);
        }

        private static string ToQueryString(Dictionary<string, string> values)
        {
            return string.Join("&", values.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
        }

        private static string NormalizePlaylistInput(string raw)
        {
            var value = (raw ?? "").Trim().Trim('\'', '"');
            while (value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }

        private static bool IsPublicCollectionId(string value)
        {
            value = NormalizePlaylistInput(value);
            return value.StartsWith("collection_", StringComparison.OrdinalIgnoreCase)
                   || value.StartsWith("gcid_", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> ParseQueryValues(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            value = NormalizePlaylistInput(value);
            var queryStart = value.IndexOf('?');
            var query = queryStart >= 0 ? value.Substring(queryStart + 1) : value;
            foreach (var part in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var index = part.IndexOf('=');
                if (index <= 0) continue;
                var name = Uri.UnescapeDataString(part.Substring(0, index));
                var raw = part.Substring(index + 1);
                result[name] = Uri.UnescapeDataString(raw);
            }
            return result;
        }

        private static string ExtractQueryValue(string value, string name)
        {
            return GetQueryValue(ParseQueryValues(value), name);
        }

        private static string GetQueryValue(Dictionary<string, string> query, string name)
        {
            return query.TryGetValue(name, out var value) ? NormalizePlaylistInput(value) : "";
        }

        private void LogJsonSummary(string label, string json)
        {
            if (_logger == null) return;
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("[Kugou] {Label} returned empty response", label);
                return;
            }

            try
            {
                var root = JObject.Parse(json);
                var status = PickStringDeep(root, "status", "errcode", "error_code") ?? "";
                var message = PickStringDeep(root, "error", "errmsg", "error_msg", "msg", "message") ?? "";
                var arrays = FindArrays(root, "songs", "list", "info", "files", "lists")
                    .Select(a => a.Count)
                    .Take(5)
                    .ToList();
                KugouImportDebugLog.Write($"{label} response summary: status='{status}', message='{message}', arrayCounts=[{string.Join(",", arrays)}], preview={KugouImportDebugLog.Truncate(json, 4000)}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Kugou] {Label} returned non-JSON or unparsable response: preview={Preview}", label, TruncateForLog(json, 500));
                KugouImportDebugLog.Write($"{label} returned non-JSON or unparsable response: preview={KugouImportDebugLog.Truncate(json, 4000)}", ex);
            }
        }

        private static string TruncateForLog(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("\r", "\\r").Replace("\n", "\\n");
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private static string MaskPlaylistInput(string value)
        {
            value = NormalizePlaylistInput(value);
            if (string.IsNullOrWhiteSpace(value)) return "";
            var queryStart = value.IndexOf('?');
            if (queryStart < 0) return value;

            var prefix = value.Substring(0, queryStart + 1);
            var query = ParseQueryValues(value);
            foreach (var secret in new[] { "token", "sign" })
            {
                if (query.ContainsKey(secret))
                    query[secret] = "***";
            }
            return prefix + ToQueryString(query);
        }

        private static string FormatParamsForLog(Dictionary<string, string> values)
        {
            return string.Join("&", values
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv =>
                {
                    var key = kv.Key;
                    var value = kv.Value ?? "";
                    if (key.Equals("token", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("signature", StringComparison.OrdinalIgnoreCase))
                        value = "***";
                    return $"{key}={value}";
                }));
        }

        private static string FormatSongsForLog(IEnumerable<KugouSongInfo> songs)
        {
            return string.Join(" | ", songs.Select(s => $"{s.Artist} - {s.Title}#{s.Hash}/{s.AlbumAudioId}"));
        }

        private static (string artist, string title) SplitArtistTitle(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return ("", "");
            var parts = filename.Split(new[] { " - " }, 2, StringSplitOptions.None);
            if (parts.Length == 2) return (parts[0].Replace("、", ", "), parts[1]);
            return ("", filename);
        }

        private static string Md5Hex(string value)
        {
            using var md5 = MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        private static string RandomString(int length)
        {
            const string chars = "1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var bytes = RandomNumberGenerator.GetBytes(length);
            var sb = new StringBuilder(length);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }

        private static float ParseDuration(string value)
        {
            if (!float.TryParse(value, out var duration)) return 0;
            return duration > 10000 ? duration / 1000f : duration;
        }

        private static string InferFormat(string url)
        {
            var path = url.Split('?', '#')[0].ToLowerInvariant();
            if (path.EndsWith(".flac")) return "flac";
            if (path.EndsWith(".m4a") || path.EndsWith(".aac")) return "aac";
            return "mp3";
        }

        private static string NormalizeCover(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var url = raw.Replace("{size}", "480");
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url.Substring("http://".Length);
            return url;
        }
    }
}
