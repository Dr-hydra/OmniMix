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

namespace OmniMixPlayer.Module.Kugou
{
    public class KugouBridge
    {
        public const string UserAgent = "IPhone-8990-searchSong";
        private const int AppId = 1005;
        private const int LoginAppId = 1001;
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
            values["qrcode_txt"] = $"https://h5.kugou.com/apps/loginQRCode/html/index.html?appid={AppId}&";
            values["srcappid"] = SrcAppId.ToString();
            values["signature"] = SignatureWeb(values);

            var json = await GetStringAsync("https://login-user.kugou.com/v2/qrcode", values, null, cancellationToken);
            var root = JObject.Parse(json);
            var key = root["data"]?["qrcode"]?.ToString();
            var image = root["data"]?["qrcode_img"]?.ToString();
            if (string.IsNullOrWhiteSpace(key)) return null;

            return new KugouQrLoginInfo
            {
                Key = key,
                ImageBytes = DecodeDataUrl(image),
                StatusText = "请使用酷狗音乐扫码登录"
            };
        }

        public async Task<(int status, KugouSession session, string message)> CheckQrLoginAsync(string key, KugouSession current, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return (0, null, "二维码无效");

            var values = BuildDefaultParams(current, web: true);
            values["appid"] = AppId.ToString();
            values["plat"] = "4";
            values["srcappid"] = SrcAppId.ToString();
            values["qrcode"] = key;
            values["signature"] = SignatureWeb(values);

            var json = await GetStringAsync("https://login-user.kugou.com/v2/get_userinfo_qrcode", values, current, cancellationToken);
            var root = JObject.Parse(json);
            var status = root["data"]?["status"]?.ToObject<int?>() ?? root["status"]?.ToObject<int?>() ?? 0;

            if (status == 4)
            {
                var data = root["data"];
                var session = EnsureSession(current);
                session.Token = data?["token"]?.ToString() ?? session.Token;
                session.UserId = data?["userid"]?.ToString() ?? session.UserId;
                session.LoginTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return (status, session, "登录成功");
            }

            var message = status switch
            {
                1 => "等待扫码",
                2 => "已扫码，请在手机上确认",
                0 => "二维码已过期",
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

            var data = new Dictionary<string, object>
            {
                ["listid"] = listId,
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

            var json = await PostJsonAsync("https://gateway.kugou.com/v4/get_list_all_file", values, data, session, cancellationToken,
                new Dictionary<string, string> { ["x-router"] = "cloudlist.service.kugou.com" });
            return ParseSongs(JObject.Parse(json));
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
                if (string.IsNullOrWhiteSpace(title))
                    title = item["songname"]?.ToString() ?? item["song_name"]?.ToString() ?? name;

                result.Add(new KugouSongInfo
                {
                    Hash = hash,
                    AlbumAudioId = item["album_audio_id"]?.ToObject<long?>() ?? item["audio_id"]?.ToObject<long?>() ?? 0,
                    AlbumId = item["album_id"]?.ToObject<long?>() ?? 0,
                    Title = title,
                    Artist = artist,
                    Album = item["albumname"]?.ToString() ?? item["album_name"]?.ToString() ?? "",
                    CoverUrl = NormalizeCover(item["image"]?.ToString() ?? item["img"]?.ToString() ?? item["album_img"]?.ToString()),
                    Duration = ParseDuration(item["duration"]?.ToString() ?? item["time_length"]?.ToString())
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

        private static byte[] DecodeDataUrl(string dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return null;
            var marker = "base64,";
            var idx = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            var raw = idx >= 0 ? dataUrl.Substring(idx + marker.Length) : dataUrl;
            try { return Convert.FromBase64String(raw); } catch { return null; }
        }

        private static string ToQueryString(Dictionary<string, string> values)
        {
            return string.Join("&", values.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
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
