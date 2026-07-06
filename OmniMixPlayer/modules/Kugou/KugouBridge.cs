using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        private const int SrcAppId = 2919;
        private const int ClientVersion = 20489;
        private const string AndroidUserAgent = "Android15-1070-11083-46-0-DiscoveryDRADProtocol-wifi";
        private const string AndroidSignatureSalt = "OIlwieks28dk2k092lksi2UIkp";
        private const string WebSignatureSalt = "NVPh5oo715z5DIWAeQlhMDsWXXQV4hwt";
        private const string PrivUrlKeySalt = "185672dd44712f60bb1736df5a377e82";
        private const string TokenRefreshAesKey = "90b8382a1bb4ccdcf063102053fd75b8";
        private const string TokenRefreshAesIv = "f063102053fd75b8";
        private const string RsaPublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDIAG7QOELSYoIJvTFJhMpe1s/gbjDJX51HBNnEl5HXqTW6lQ7LC8jr9fWZTwusknp+sVGzwd40MwP6U5yDE27M/X1+UR4tvOGOqp94TJtQ1EPnWGWXngpeIW5GxoQGao1rmYWAu6oi1z9XkChrsUdC6DJE5E221wf/4WLFxwAtRQIDAQAB\n-----END PUBLIC KEY-----";
        private static readonly byte[] KrcXorKey = { 64, 71, 97, 119, 94, 50, 116, 71, 81, 54, 49, 45, 206, 210, 110, 105 };

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
            values["appid"] = AppId.ToString();
            values["plat"] = "4";
            values["srcappid"] = SrcAppId.ToString();
            values["qrcode"] = key;
            values["signature"] = SignatureWeb(values);

            var json = await GetStringAsync("https://login-user.kugou.com/v2/get_userinfo_qrcode", values, current, cancellationToken);
            var root = JObject.Parse(json);
            var status = PickQrStatus(root);
            KugouImportDebugLog.Write($"QR login poll status={status}");

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
                session = await EnsureRegisteredDeviceAsync(session, force: true, cancellationToken);
                session = await RefreshLoginAsync(session, cancellationToken);
                KugouImportDebugLog.Write($"Login success userId='{session.UserId}', vipType='{session.VipType ?? "0"}', hasVipToken={!string.IsNullOrWhiteSpace(session.VipToken)}");
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

            try
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var tokenPayload = new Dictionary<string, object>
                {
                    ["clienttime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["token"] = session.Token
                };
                var p3 = AesEncryptHex(ToJson(tokenPayload), TokenRefreshAesKey, TokenRefreshAesIv);
                var encryptParams = AesEncryptWithRandomKey(new Dictionary<string, object>());
                var pk = RsaRawEncryptHex(ToJson(new Dictionary<string, object>
                {
                    ["clienttime_ms"] = nowMs,
                    ["key"] = encryptParams.Key
                }));

                var data = new Dictionary<string, object>
                {
                    ["dfid"] = session.Dfid,
                    ["p3"] = p3,
                    ["plat"] = 1,
                    ["t1"] = 0,
                    ["t2"] = 0,
                    ["t3"] = "MCwwLDAsMCwwLDAsMCwwLDA=",
                    ["pk"] = pk,
                    ["params"] = encryptParams.Hex,
                    ["userid"] = session.UserId,
                    ["clienttime_ms"] = nowMs
                };
                var body = ToJson(data);
                var values = BuildDefaultParams(session);
                values["signature"] = SignatureAndroid(values, body);

                var json = await PostJsonAsync("http://login.user.kugou.com/v5/login_by_token", values, data, session, cancellationToken);
                var root = JObject.Parse(json);
                if ((root["status"]?.ToObject<int?>() ?? 0) != 1) return session;

                var dataObj = root["data"] as JObject;
                if (dataObj?["secu_params"] != null)
                {
                    var decrypted = AesDecryptHex(dataObj["secu_params"].ToString(), encryptParams.Key);
                    if (!string.IsNullOrWhiteSpace(decrypted) && decrypted.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    {
                        var secu = JObject.Parse(decrypted);
                        foreach (var prop in secu.Properties())
                            dataObj[prop.Name] = prop.Value;
                    }
                    else if (!string.IsNullOrWhiteSpace(decrypted))
                    {
                        dataObj["token"] = decrypted;
                    }
                }

                session.T1 = dataObj?["t1"]?.ToString() ?? session.T1;
                session.Token = dataObj?["token"]?.ToString() ?? session.Token;
                session.UserId = dataObj?["userid"]?.ToString() ?? session.UserId;
                session.VipType = dataObj?["vip_type"]?.ToString() ?? session.VipType;
                session.VipToken = dataObj?["vip_token"]?.ToString() ?? session.VipToken;
                session.LoginTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                KugouImportDebugLog.Write($"Token refresh ok userId='{session.UserId}', vipType='{session.VipType ?? "0"}', hasVipToken={!string.IsNullOrWhiteSpace(session.VipToken)}");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kugou] Token refresh failed");
                KugouImportDebugLog.Write("Token refresh failed", ex);
            }

            return session;
        }

        public async Task<KugouSession> EnsureRegisteredDeviceAsync(KugouSession session, bool force = false, CancellationToken cancellationToken = default)
        {
            session = EnsureSession(session);
            if (!session.IsLoggedIn) return session;
            if (!force && HasRegisteredDfid(session)) return session;

            try
            {
                var data = new Dictionary<string, object>
                {
                    ["availableRamSize"] = 4983533568L,
                    ["availableRomSize"] = 48114719,
                    ["availableSDSize"] = 48114717,
                    ["basebandVer"] = "",
                    ["batteryLevel"] = 100,
                    ["batteryStatus"] = 3,
                    ["brand"] = "Redmi",
                    ["buildSerial"] = "unknown",
                    ["device"] = "marble",
                    ["imei"] = session.Guid,
                    ["imsi"] = "",
                    ["manufacturer"] = "Xiaomi",
                    ["uuid"] = session.Guid,
                    ["accelerometer"] = false,
                    ["accelerometerValue"] = "",
                    ["gravity"] = false,
                    ["gravityValue"] = "",
                    ["gyroscope"] = false,
                    ["gyroscopeValue"] = "",
                    ["light"] = false,
                    ["lightValue"] = "",
                    ["magnetic"] = false,
                    ["magneticValue"] = "",
                    ["orientation"] = false,
                    ["orientationValue"] = "",
                    ["pressure"] = false,
                    ["pressureValue"] = "",
                    ["step_counter"] = false,
                    ["step_counterValue"] = "",
                    ["temperature"] = false,
                    ["temperatureValue"] = ""
                };

                var encrypted = PlaylistAesEncrypt(data);
                var p = RsaPkcs1EncryptHex(ToJson(new Dictionary<string, object>
                {
                    ["aes"] = encrypted.Key,
                    ["uid"] = session.UserId,
                    ["token"] = session.Token ?? ""
                }));

                var values = BuildDefaultParams(session);
                values["part"] = "1";
                values["platid"] = "1";
                values["p"] = p;
                values["signature"] = SignatureAndroid(values, encrypted.Base64);

                var response = await PostRawBytesAsync("https://userservice.kugou.com/risk/v2/r_register_dev",
                    values, encrypted.Base64, session, cancellationToken);
                var decrypted = PlaylistAesDecrypt(response, encrypted.Key);
                LogJsonSummary("Device register", decrypted);
                var root = JObject.Parse(decrypted);
                var status = root["status"]?.ToObject<int?>() ?? 0;
                var dfid = PickStringDeep(root["data"], "dfid") ?? PickStringDeep(root, "dfid");
                if (status == 1 && !string.IsNullOrWhiteSpace(dfid))
                {
                    session.Dfid = dfid;
                    session.DfidRegisteredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    KugouImportDebugLog.Write($"Device register ok dfid='{MaskDfid(session.Dfid)}'");
                }
                else
                {
                    var message = PickStringDeep(root, "msg", "message", "error_msg") ?? "";
                    KugouImportDebugLog.WriteWarning($"Device register no-dfid status={status}, message='{KugouImportDebugLog.Truncate(message, 120)}'");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kugou] Device register failed");
                KugouImportDebugLog.Write("Device register failed", ex);
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
            KugouImportDebugLog.Write($"Playlist songs request id='{MaskPlaylistInput(playlistInput)}', publicId='{publicId ?? ""}', page={Math.Max(1, page)}, loggedIn={session.IsLoggedIn}");

            if (!string.IsNullOrWhiteSpace(publicId))
            {
                KugouImportDebugLog.Write($"Playlist endpoint try mobile_shared id='{publicId}', page={Math.Max(1, page)}");
                var sharedSongs = await GetMobileSharedPlaylistSongsAsync(playlistInput, publicId, page, cancellationToken);
                KugouImportDebugLog.Write($"Playlist endpoint result mobile_shared id='{publicId}', songs={sharedSongs.Count}");
                if (sharedSongs.Count > 0) return sharedSongs;

                KugouImportDebugLog.Write($"Playlist endpoint try public_playlist id='{publicId}', page={Math.Max(1, page)}");
                var publicSongs = await GetPublicPlaylistSongsAsync(publicId, session, page, pageSize, cancellationToken);
                KugouImportDebugLog.Write($"Playlist endpoint result public_playlist id='{publicId}', songs={publicSongs.Count}");
                if (publicSongs.Count > 0) return publicSongs;
            }

            KugouImportDebugLog.Write($"Playlist endpoint try account_playlist id='{MaskPlaylistInput(playlistInput)}', page={Math.Max(1, page)}");
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

            var json = await PostJsonAsync("https://gateway.kugou.com/v4/get_list_all_file", values, data, session, cancellationToken,
                new Dictionary<string, string> { ["x-router"] = "cloudlist.service.kugou.com" });
            LogJsonSummary("Playlist endpoint account_playlist", json);
            var songs = ParseSongs(JObject.Parse(json));
            KugouImportDebugLog.Write($"Playlist endpoint result account_playlist id='{MaskPlaylistInput(playlistInput)}', songs={songs.Count}");
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

                var json = await PostJsonAsync("https://gateway.kugou.com/v3/get_list_info", values, data, session, cancellationToken,
                    new Dictionary<string, string> { ["x-router"] = "pubsongs.kugou.com" });
                LogJsonSummary("Playlist detail public", json);
                var playlist = ParsePlaylistList(JObject.Parse(json)).FirstOrDefault();
                KugouImportDebugLog.Write($"Playlist detail result id='{publicId}', found={playlist != null}, name='{playlist?.Name ?? ""}', count={playlist?.Count ?? 0}");
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
            KugouImportDebugLog.Write($"PlayUrl request hash='{MaskHash(song.Hash)}', albumAudioId={song.AlbumAudioId}, loggedIn={session?.IsLoggedIn == true}, vipType='{session?.VipType ?? "0"}', hasVipToken={!string.IsNullOrWhiteSpace(session?.VipToken)}");
            if (session?.IsLoggedIn == true)
            {
                session = await EnsureRegisteredDeviceAsync(session, cancellationToken: cancellationToken);
                var loginUrl = await GetPrivilegedUrlAsync(song, session, cancellationToken);
                if (loginUrl != null && !string.IsNullOrWhiteSpace(loginUrl.Url))
                {
                    KugouImportDebugLog.Write($"PlayUrl result route=priv_url hash='{MaskHash(song.Hash)}', format='{loginUrl.Format}', bitrate={loginUrl.Bitrate}");
                    return loginUrl;
                }
                if (session.IsLoggedIn)
                {
                    KugouImportDebugLog.Write($"PlayUrl priv_url retry after device register hash='{MaskHash(song.Hash)}'");
                    session = await EnsureRegisteredDeviceAsync(session, force: true, cancellationToken);
                    loginUrl = await GetPrivilegedUrlAsync(song, session, cancellationToken);
                    if (loginUrl != null && !string.IsNullOrWhiteSpace(loginUrl.Url))
                    {
                        KugouImportDebugLog.Write($"PlayUrl result route=priv_url_retry hash='{MaskHash(song.Hash)}', format='{loginUrl.Format}', bitrate={loginUrl.Bitrate}");
                        return loginUrl;
                    }
                }
                KugouImportDebugLog.Write($"PlayUrl priv_url empty hash='{MaskHash(song.Hash)}', fallback=anonymous");
            }

            var fallback = await GetPlayableUrlAsync(song.Hash, maxBitrate, cancellationToken);
            KugouImportDebugLog.Write($"PlayUrl result route=anonymous hash='{MaskHash(song.Hash)}', success={fallback != null && !string.IsNullOrWhiteSpace(fallback.Url)}, format='{fallback?.Format ?? ""}', bitrate={fallback?.Bitrate ?? 0}");
            return fallback;
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

                var krc = await GetKrcLyricAsync(id, accessKey, cancellationToken);
                if (!string.IsNullOrWhiteSpace(krc))
                    return krc;

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

        private async Task<string> GetKrcLyricAsync(string id, string accessKey, CancellationToken cancellationToken)
        {
            try
            {
                var downloadUrl = "http://lyrics.kugou.com/download?" + ToQueryString(new Dictionary<string, string>
                {
                    ["charset"] = "utf8",
                    ["accesskey"] = accessKey,
                    ["id"] = id,
                    ["client"] = "mobi",
                    ["fmt"] = "krc",
                    ["ver"] = "1"
                });

                var json = await _client.GetStringAsync(downloadUrl, cancellationToken);
                var root = JObject.Parse(json);
                var content = root["content"]?.ToString();
                if (string.IsNullOrWhiteSpace(content)) return "";

                var rawKrc = root["contenttype"]?.ToObject<int?>() == 0
                    ? DecodeKrc(content)
                    : Encoding.UTF8.GetString(Convert.FromBase64String(content));
                var parsed = ParseKrcLyric(rawKrc);
                if (string.IsNullOrWhiteSpace(parsed.lrc)) return "";

                return new JObject
                {
                    ["lrc"] = parsed.lrc,
                    ["tlyric"] = parsed.tlyric ?? "",
                    ["rlyric"] = ""
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kugou] KRC lyric failed: {Id}", id);
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
                var json = await GetStringAsync("https://gateway.kugou.com/pubsongs/v2/get_other_list_file_nofilt", values, session, cancellationToken);
                LogJsonSummary("Playlist endpoint public_playlist", json);
                if (string.IsNullOrWhiteSpace(json)) return new List<KugouSongInfo>();
                var songs = ParseSongs(JObject.Parse(json));
                KugouImportDebugLog.Write($"Playlist endpoint result public_playlist id='{globalCollectionId}', page={Math.Max(1, page)}, songs={songs.Count}");
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
                    KugouImportDebugLog.Write($"Playlist endpoint skip mobile_shared id='{globalCollectionId}', reason=missing_share_params");
                    return new List<KugouSongInfo>();
                }

                query["listid"] = "2";
                query["type"] = "0";
                query["global_collection_id"] = globalCollectionId;
                query["page"] = Math.Max(1, page).ToString();

                var url = "https://m3ws.kugou.com/zlist/list?" + ToQueryString(query);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 Mobile Safari/537.36");
                using var response = await _client.SendAsync(request, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                response.EnsureSuccessStatusCode();
                LogJsonSummary("Playlist endpoint mobile_shared", json);
                if (string.IsNullOrWhiteSpace(json)) return new List<KugouSongInfo>();
                var songs = ParseSongs(JObject.Parse(json));
                KugouImportDebugLog.Write($"Playlist endpoint result mobile_shared id='{globalCollectionId}', page={Math.Max(1, page)}, songs={songs.Count}");
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
                        ["album_audio_id"] = song.AlbumAudioId.ToString(),
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
                LogJsonSummary("PlayUrl endpoint priv_url", json);
                var root = JObject.Parse(json);
                var url = FindFirstUrl(root);
                if (string.IsNullOrWhiteSpace(url))
                {
                    var reason = DescribePrivUrlNoUrl(root);
                    KugouImportDebugLog.WriteWarning($"PlayUrl priv_url no-url hash='{MaskHash(song.Hash)}', reason='{reason}', vipType='{session.VipType ?? "0"}', hasVipToken={!string.IsNullOrWhiteSpace(session.VipToken)}");
                    return null;
                }

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
                KugouImportDebugLog.Write($"PlayUrl priv_url failed hash='{MaskHash(song.Hash)}'", ex);
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

        private async Task<byte[]> PostRawBytesAsync(string baseUrl, Dictionary<string, string> values, string data, KugouSession session, CancellationToken cancellationToken, Dictionary<string, string> extraHeaders = null)
        {
            var url = baseUrl + "?" + ToQueryString(values);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(data ?? ""));
            ApplyAndroidHeaders(request, values, session, extraHeaders);
            using var response = await _client.SendAsync(request, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return bytes;
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
            if (string.IsNullOrWhiteSpace(session.DeviceId))
                session.DeviceId = RandomString(10);
            if (string.IsNullOrWhiteSpace(session.Mac))
                session.Mac = "02:00:00:00:00:00";
            if (string.IsNullOrWhiteSpace(session.Dfid))
                session.Dfid = "-";
            if (string.IsNullOrWhiteSpace(session.Mid) || session.Mid == "0" || LooksLikeMd5Hex(session.Mid))
                session.Mid = CalculateMid(session.Guid);
            if (string.IsNullOrWhiteSpace(session.UserId))
                session.UserId = "0";
            return session;
        }

        private static bool HasRegisteredDfid(KugouSession session)
        {
            return session != null
                   && session.DfidRegisteredAt > 0
                   && !string.IsNullOrWhiteSpace(session.Dfid)
                   && session.Dfid != "-";
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
                $"KUGOU_API_GUID={session.Guid}",
                $"KUGOU_API_DEV={session.DeviceId}",
                $"KUGOU_API_MAC={session.Mac}",
                $"vip_type={session.VipType ?? "0"}"
            };
            if (!string.IsNullOrWhiteSpace(session.VipToken))
                parts.Add($"vip_token={session.VipToken}");
            if (!string.IsNullOrWhiteSpace(session.T1))
                parts.Add($"t1={session.T1}");
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
            var trackerUrl = FindFirstNamedUrl(token, "tracker_url");
            if (!string.IsNullOrWhiteSpace(trackerUrl)) return trackerUrl;
            return FindFirstNamedUrl(token, "url");
        }

        private static string DescribePrivUrlNoUrl(JObject root)
        {
            var item = root?["data"] is JArray dataArray ? dataArray.FirstOrDefault() as JObject : null;
            if (item == null) return "empty_data";

            var status = item["status"]?.ToString() ?? "";
            var failProcess = item["fail_process"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            var privilege = item["privilege"]?.ToString() ?? "";
            var payType = item["pay_type"]?.ToString() ?? "";
            var payBlock = item.SelectToken("trans_param.pay_block_tpl")?.ToString() ?? "";
            var trackerStatus = item.SelectToken("info.tracker_status")?.ToString() ?? "";
            var trackerType = item.SelectToken("info.tracker_type")?.ToString() ?? "";
            var message = item["_msg"]?.ToString() ?? "";

            if (failProcess.Equals("20", StringComparison.OrdinalIgnoreCase) ||
                payType == "2" ||
                !string.IsNullOrWhiteSpace(payBlock))
            {
                return $"restricted_buy status={status}, fail_process={failProcess}, privilege={privilege}, pay_type={payType}, pay_block_tpl={payBlock}, tracker_status={trackerStatus}, tracker_type={trackerType}";
            }

            if (!string.IsNullOrWhiteSpace(message))
                return $"no_tracker_url status={status}, message={KugouImportDebugLog.Truncate(message, 80)}, tracker_status={trackerStatus}, tracker_type={trackerType}";

            return $"no_tracker_url status={status}, fail_process={failProcess}, privilege={privilege}, tracker_status={trackerStatus}, tracker_type={trackerType}";
        }

        private static string FindFirstNamedUrl(JToken token, string name)
        {
            if (token == null) return null;
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value is JArray arr)
                        {
                            var first = arr.Select(item => item?.ToString())
                                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
                                                         value.StartsWith("http", StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrWhiteSpace(first)) return first;
                        }
                        var value = prop.Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            return value;
                    }
                    var nested = FindFirstNamedUrl(prop.Value, name);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            if (token is JArray array)
            {
                foreach (var child in array)
                {
                    var nested = FindFirstNamedUrl(child, name);
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
                var errorCode = PickStringDeep(root, "error_code", "errcode", "code") ?? "";
                var message = PickStringDeep(root, "error", "errmsg", "error_msg", "msg", "message") ?? "";
                var arrays = FindArrays(root, "songs", "list", "info", "files", "lists")
                    .Select(a => a.Count)
                    .Take(5)
                    .ToList();
                KugouImportDebugLog.Write($"{label} status='{status}', code='{errorCode}', message='{KugouImportDebugLog.Truncate(message, 160)}', arrays=[{string.Join(",", arrays)}]");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Kugou] {Label} returned non-JSON or unparsable response: preview={Preview}", label, TruncateForLog(json, 500));
                KugouImportDebugLog.Write($"{label} non-json length={json?.Length ?? 0}", ex);
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

        private static string MaskHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return "";
            return hash.Length <= 10 ? hash : hash.Substring(0, 6) + "..." + hash.Substring(hash.Length - 4);
        }

        private static string MaskDfid(string dfid)
        {
            if (string.IsNullOrWhiteSpace(dfid) || dfid.Length <= 8) return dfid ?? "";
            return dfid.Substring(0, 4) + "..." + dfid.Substring(dfid.Length - 4);
        }

        private static string DecodeKrc(string content)
        {
            var bytes = Convert.FromBase64String(content);
            if (bytes.Length <= 4) return "";

            var encrypted = bytes.Skip(4).ToArray();
            for (int i = 0; i < encrypted.Length; i++)
                encrypted[i] = (byte)(encrypted[i] ^ KrcXorKey[i % KrcXorKey.Length]);

            using var input = new MemoryStream(encrypted);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }

        private static (string lrc, string tlyric) ParseKrcLyric(string rawKrc)
        {
            if (string.IsNullOrWhiteSpace(rawKrc)) return ("", "");

            var translations = ExtractKrcTranslations(rawKrc);
            var lrcLines = new List<string>();
            var tlyricLines = new List<string>();
            var lyricIndex = 0;

            foreach (var rawLine in rawKrc.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine.Trim('\uFEFF', ' ', '\t');
                var match = Regex.Match(line, @"^\[(\d+),(\d+)\](.*)$");
                if (!match.Success) continue;

                var startMs = int.TryParse(match.Groups[1].Value, out var parsedStart) ? parsedStart : 0;
                var text = StripKrcWordTags(match.Groups[3].Value).Trim();
                var time = FormatLrcTime(startMs);
                if (!string.IsNullOrWhiteSpace(text))
                    lrcLines.Add($"[{time}]{text}");

                if (lyricIndex < translations.Count)
                {
                    var translation = NormalizeLyricText(translations[lyricIndex]);
                    if (!string.IsNullOrWhiteSpace(translation))
                        tlyricLines.Add($"[{time}]{translation}");
                }
                lyricIndex++;
            }

            return (string.Join("\n", lrcLines), string.Join("\n", tlyricLines));
        }

        private static List<string> ExtractKrcTranslations(string rawKrc)
        {
            var match = Regex.Match(rawKrc, @"\[language:([^\]]+)\]");
            if (!match.Success) return new List<string>();

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value));
                var root = JObject.Parse(json);
                var content = root["content"] as JArray;
                if (content == null) return new List<string>();

                var block = content
                    .OfType<JObject>()
                    .FirstOrDefault(x => x["type"]?.ToObject<int?>() == 1)
                    ?? content.OfType<JObject>().FirstOrDefault();
                var lyricContent = block?["lyricContent"] as JArray;
                if (lyricContent == null) return new List<string>();

                var result = new List<string>();
                foreach (var row in lyricContent)
                {
                    if (row is JArray parts)
                        result.Add(string.Join("", parts.Select(x => x?.ToString() ?? "")));
                    else
                        result.Add(row?.ToString() ?? "");
                }
                return result;
            }
            catch
            {
                return new List<string>();
            }
        }

        private static string StripKrcWordTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return NormalizeLyricText(Regex.Replace(value, @"<\d+,\d+,\d+>", ""));
        }

        private static string NormalizeLyricText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return value.Replace("\u00A0", " ").Trim();
        }

        private static string FormatLrcTime(int milliseconds)
        {
            if (milliseconds < 0) milliseconds = 0;
            var totalSeconds = milliseconds / 1000d;
            var minutes = (int)(totalSeconds / 60);
            var seconds = totalSeconds - minutes * 60;
            return $"{minutes:00}:{seconds:00.00}";
        }

        private static (string Hex, string Key) AesEncryptWithRandomKey(Dictionary<string, object> data)
        {
            var tempKey = RandomString(16).ToLowerInvariant();
            var key = Md5Hex(tempKey).Substring(0, 32);
            var iv = key.Substring(key.Length - 16);
            return (AesEncryptHex(ToJson(data), key, iv), tempKey);
        }

        private static string AesEncryptHex(string plainText, string key, string iv)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.IV = Encoding.UTF8.GetBytes(iv);
            using var encryptor = aes.CreateEncryptor();
            var bytes = Encoding.UTF8.GetBytes(plainText ?? "");
            return Convert.ToHexString(encryptor.TransformFinalBlock(bytes, 0, bytes.Length)).ToLowerInvariant();
        }

        private static string AesDecryptHex(string hex, string tempKey)
        {
            if (string.IsNullOrWhiteSpace(hex) || string.IsNullOrWhiteSpace(tempKey)) return "";
            var key = Md5Hex(tempKey).Substring(0, 32);
            var iv = key.Substring(key.Length - 16);
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.IV = Encoding.UTF8.GetBytes(iv);
            using var decryptor = aes.CreateDecryptor();
            var encrypted = Convert.FromHexString(hex);
            var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return Encoding.UTF8.GetString(decrypted);
        }

        private static (string Base64, string Key) PlaylistAesEncrypt(Dictionary<string, object> data)
        {
            var tempKey = RandomString(6).ToLowerInvariant();
            var digest = Md5Hex(tempKey);
            var key = digest.Substring(0, 16);
            var iv = digest.Substring(16, 16);
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.IV = Encoding.UTF8.GetBytes(iv);
            using var encryptor = aes.CreateEncryptor();
            var bytes = Encoding.UTF8.GetBytes(ToJson(data));
            return (Convert.ToBase64String(encryptor.TransformFinalBlock(bytes, 0, bytes.Length)), tempKey);
        }

        private static string PlaylistAesDecrypt(byte[] encrypted, string tempKey)
        {
            if (encrypted == null || encrypted.Length == 0 || string.IsNullOrWhiteSpace(tempKey)) return "";
            var digest = Md5Hex(tempKey);
            var key = digest.Substring(0, 16);
            var iv = digest.Substring(16, 16);
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.IV = Encoding.UTF8.GetBytes(iv);
            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return Encoding.UTF8.GetString(decrypted);
        }

        private static string RsaRawEncryptHex(string plainText)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(RsaPublicKeyPem);
            var parameters = rsa.ExportParameters(false);
            var keyLength = parameters.Modulus.Length;
            var message = Encoding.UTF8.GetBytes(plainText ?? "");
            if (message.Length > keyLength)
                throw new InvalidOperationException("Kugou RSA payload exceeds key size");

            var padded = new byte[keyLength];
            Buffer.BlockCopy(message, 0, padded, 0, message.Length);

            var modulus = new BigInteger(parameters.Modulus, isUnsigned: true, isBigEndian: true);
            var exponent = new BigInteger(parameters.Exponent, isUnsigned: true, isBigEndian: true);
            var value = new BigInteger(padded, isUnsigned: true, isBigEndian: true);
            var encrypted = BigInteger.ModPow(value, exponent, modulus);
            var output = encrypted.ToByteArray(isUnsigned: true, isBigEndian: true);

            if (output.Length < keyLength)
            {
                var fixedOutput = new byte[keyLength];
                Buffer.BlockCopy(output, 0, fixedOutput, keyLength - output.Length, output.Length);
                output = fixedOutput;
            }

            return Convert.ToHexString(output).ToLowerInvariant();
        }

        private static string RsaPkcs1EncryptHex(string plainText)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(RsaPublicKeyPem);
            var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(plainText ?? ""), RSAEncryptionPadding.Pkcs1);
            return Convert.ToHexString(encrypted).ToLowerInvariant();
        }

        private static string CalculateMid(string value)
        {
            var digest = Md5Hex(value ?? "");
            var number = BigInteger.Zero;
            foreach (var ch in digest)
            {
                number *= 16;
                number += Convert.ToInt32(ch.ToString(), 16);
            }
            return number.ToString();
        }

        private static bool LooksLikeMd5Hex(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && value.Length == 32
                   && value.All(Uri.IsHexDigit);
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
