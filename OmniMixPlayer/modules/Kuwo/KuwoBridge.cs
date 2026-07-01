using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace OmniMixPlayer.Module.Kuwo
{
    public class KuwoBridge
    {
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36 Edg/117.0.2045.47";

        private const string Token = "3E7JFQ7MRPL";
        private const string KuwoHomeUrl = "https://www.kuwo.cn/";
        private const string KuwoCookiePrefix = "Hm_Iuvt_";
        private readonly ILogger _logger;
        private readonly HttpClient _client;
        private readonly CookieContainer _cookies = new();
        private string _csrfToken = Token;
        private string _kuwoCookieName = "";
        private string _kuwoCookieValue = "";

        public KuwoBridge(ILogger logger)
        {
            _logger = logger;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = _cookies,
                UseCookies = true
            };
            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "http://www.kuwo.cn/");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("csrf", Token);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"kw_token={Token}");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        }

        public async Task<(KuwoPlaylistInfo playlist, List<KuwoSongInfo> songs)> GetPlaylistAsync(string rawIdOrUrl, int page, int limit, CancellationToken cancellationToken = default)
        {
            var id = ExtractPlaylistId(rawIdOrUrl);
            if (string.IsNullOrWhiteSpace(id))
                return (null, new List<KuwoSongInfo>());

            var result = await GetPlaylistFromWebApiAsync(id, page, limit, cancellationToken);
            if (result.songs.Count > 0 || result.playlist != null)
                return result;

            return await GetPlaylistFromNplAsync(id, page, limit, cancellationToken);
        }

        public async Task<KuwoSearchResult> SearchAsync(string keyword, int page, int limit, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return KuwoSearchResult.Ok(new List<KuwoSongInfo>());

            try
            {
                var songs = await SearchFromWebApiAsync(keyword, page, limit, cancellationToken);
                if (songs.Count == 0)
                    songs = await SearchFromLegacyApiAsync(keyword, page, limit, cancellationToken);
                return KuwoSearchResult.Ok(songs);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kuwo] Search failed: {Keyword}", keyword);
                return KuwoSearchResult.Failed(ex.Message);
            }
        }

        public async Task<string> GetPlayableUrlAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            var webUrl = await GetPlayableUrlFromWebApiAsync(id, cancellationToken);
            if (!string.IsNullOrWhiteSpace(webUrl)) return webUrl;

            try
            {
                var url = "http://antiserver.kuwo.cn/anti.s?" + ToQueryString(new Dictionary<string, string>
                {
                    ["format"] = "mp3",
                    ["rid"] = id,
                    ["response"] = "url",
                    ["type"] = "convert_url3"
                });

                var json = await _client.GetStringAsync(url, cancellationToken);
                var root = ParseJsonObject(json);
                if (root["code"]?.ToObject<int?>() == 200 && string.Equals(root["msg"]?.ToString(), "success", StringComparison.OrdinalIgnoreCase))
                    return root["url"]?.ToString();

                return root["url"]?.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Kuwo] Resolve URL failed: {Id}", id);
                return null;
            }
        }

        private async Task<List<KuwoSongInfo>> SearchFromWebApiAsync(string keyword, int page, int limit, CancellationToken cancellationToken)
        {
            try
            {
                var query = new Dictionary<string, string>
                {
                    ["key"] = keyword,
                    ["pn"] = Math.Max(1, page).ToString(CultureInfo.InvariantCulture),
                    ["rn"] = Math.Clamp(limit, 1, 100).ToString(CultureInfo.InvariantCulture),
                    ["httpsStatus"] = "1",
                    ["reqId"] = Guid.NewGuid().ToString()
                };
                var url = "https://www.kuwo.cn/api/www/search/searchMusicBykeyWord?" + ToQueryString(query);
                var json = await GetKuwoWebApiStringAsync(url, $"https://www.kuwo.cn/search/list?key={Uri.EscapeDataString(keyword)}", cancellationToken);
                var root = ParseJsonObject(json);
                var list = root["data"]?["list"] as JArray ?? root["list"] as JArray;
                return ParseSongs(list);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kuwo] Web search failed: {Keyword}", keyword);
                return new List<KuwoSongInfo>();
            }
        }

        private async Task<List<KuwoSongInfo>> SearchFromLegacyApiAsync(string keyword, int page, int limit, CancellationToken cancellationToken)
        {
            var query = new Dictionary<string, string>
            {
                ["all"] = keyword,
                ["pn"] = Math.Max(1, page).ToString(CultureInfo.InvariantCulture),
                ["rn"] = Math.Max(1, limit).ToString(CultureInfo.InvariantCulture),
                ["vipver"] = "1",
                ["client"] = "kt",
                ["ft"] = "music",
                ["cluster"] = "0",
                ["strategy"] = "2012",
                ["encoding"] = "utf8",
                ["rformat"] = "json",
                ["mobi"] = "1"
            };

            var url = "http://www.kuwo.cn/search/searchMusicBykeyWord?" + ToQueryString(query);
            var json = await _client.GetStringAsync(url, cancellationToken);
            var root = ParseJsonObject(json);
            var list = root["abslist"] as JArray ?? root["data"]?["list"] as JArray ?? root["list"] as JArray;
            return ParseSongs(list);
        }

        private async Task<string> GetPlayableUrlFromWebApiAsync(string id, CancellationToken cancellationToken)
        {
            try
            {
                var url = "https://www.kuwo.cn/api/v1/www/music/playUrl?" + ToQueryString(new Dictionary<string, string>
                {
                    ["mid"] = id,
                    ["type"] = "music",
                    ["httpsStatus"] = "1",
                    ["reqId"] = Guid.NewGuid().ToString()
                });
                var json = await GetKuwoWebApiStringAsync(url, $"https://www.kuwo.cn/play_detail/{id}", cancellationToken);
                var root = ParseJsonObject(json);
                if (root["code"]?.ToObject<int?>() == 200 || root["success"]?.ToObject<bool?>() == true)
                    return root["data"]?["url"]?.ToString() ?? root["url"]?.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kuwo] Web play URL failed: {Id}", id);
            }

            return null;
        }

        private async Task<(KuwoPlaylistInfo playlist, List<KuwoSongInfo> songs)> GetPlaylistFromWebApiAsync(string id, int page, int limit, CancellationToken cancellationToken)
        {
            try
            {
                var query = new Dictionary<string, string>
                {
                    ["pid"] = id,
                    ["pn"] = Math.Max(1, page).ToString(),
                    ["rn"] = Math.Clamp(limit, 1, 1000).ToString(),
                    ["httpsStatus"] = "1",
                    ["reqId"] = Guid.NewGuid().ToString()
                };
                var url = "https://www.kuwo.cn/api/www/playlist/playListInfo?" + ToQueryString(query);
                var json = await GetKuwoWebApiStringAsync(url, $"https://www.kuwo.cn/playlist_detail/{id}", cancellationToken);
                var root = ParseJsonObject(json);
                if (root["success"]?.ToObject<bool?>() == false)
                    return (null, new List<KuwoSongInfo>());

                var data = root["data"] as JObject;
                var playlist = new KuwoPlaylistInfo
                {
                    Id = id,
                    Name = HtmlDecode(data?["name"]?.ToString() ?? data?["title"]?.ToString() ?? $"Kuwo {id}"),
                    CoverUrl = NormalizeCover(data?["img"]?.ToString() ?? data?["pic"]?.ToString()),
                    Count = data?["total"]?.ToObject<int?>() ?? 0
                };
                var songs = ParseSongs(data?["musicList"] as JArray);
                return (playlist, songs);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kuwo] Web playlist failed: {Id}", id);
                return (null, new List<KuwoSongInfo>());
            }
        }

        private async Task<(KuwoPlaylistInfo playlist, List<KuwoSongInfo> songs)> GetPlaylistFromNplAsync(string id, int page, int limit, CancellationToken cancellationToken)
        {
            try
            {
                var zeroPage = Math.Max(0, page - 1);
                var url = "http://nplserver.kuwo.cn/pl.svc?" + ToQueryString(new Dictionary<string, string>
                {
                    ["op"] = "getlistinfo",
                    ["pid"] = id,
                    ["pn"] = zeroPage.ToString(),
                    ["rn"] = Math.Clamp(limit, 1, 1000).ToString(),
                    ["encode"] = "utf-8",
                    ["keyset"] = "pl2012",
                    ["identity"] = "kuwo"
                });
                var json = await _client.GetStringAsync(url, cancellationToken);
                var root = ParseJsonObject(json);
                var playlist = new KuwoPlaylistInfo
                {
                    Id = id,
                    Name = HtmlDecode(root["title"]?.ToString() ?? $"Kuwo {id}"),
                    CoverUrl = NormalizeCover(root["pic"]?.ToString()),
                    Count = root["total"]?.ToObject<int?>() ?? 0
                };
                var songs = ParseSongs(root["musiclist"] as JArray);
                return (playlist, songs);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kuwo] NPL playlist failed: {Id}", id);
                return (null, new List<KuwoSongInfo>());
            }
        }

        private async Task EnsureWebSessionAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_kuwoCookieName) && !string.IsNullOrWhiteSpace(_kuwoCookieValue))
                return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, KuwoHomeUrl);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                using var response = await _client.SendAsync(request, cancellationToken);
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    foreach (var cookie in cookies)
                    {
                        var match = Regex.Match(cookie, @"kw_token=([^;]+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            _csrfToken = match.Groups[1].Value;
                        }

                        var kuwoCookie = Regex.Match(cookie, @"(Hm_Iuvt_[^=;]+)=([^;]+)", RegexOptions.IgnoreCase);
                        if (kuwoCookie.Success)
                        {
                            _kuwoCookieName = kuwoCookie.Groups[1].Value;
                            _kuwoCookieValue = Uri.UnescapeDataString(kuwoCookie.Groups[2].Value);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(_kuwoCookieName) || string.IsNullOrWhiteSpace(_kuwoCookieValue))
                {
                    foreach (Cookie cookie in _cookies.GetCookies(new Uri(KuwoHomeUrl)))
                    {
                        if (cookie.Name.StartsWith(KuwoCookiePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            _kuwoCookieName = cookie.Name;
                            _kuwoCookieValue = Uri.UnescapeDataString(cookie.Value);
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private async Task<string> GetKuwoWebApiStringAsync(string url, string referer, CancellationToken cancellationToken)
        {
            await EnsureWebSessionAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddKuwoWebHeaders(request, referer);

            using var response = await _client.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return json;
        }

        private void AddKuwoWebHeaders(HttpRequestMessage request, string referer)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Referer", string.IsNullOrWhiteSpace(referer) ? "https://www.kuwo.cn/" : referer);
            request.Headers.TryAddWithoutValidation("csrf", _csrfToken);
            request.Headers.Host = "www.kuwo.cn";

            var cookieParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_kuwoCookieName) && !string.IsNullOrWhiteSpace(_kuwoCookieValue))
            {
                cookieParts.Add($"{_kuwoCookieName}={_kuwoCookieValue}");
                var secret = BuildKuwoSecret(_kuwoCookieValue, _kuwoCookieName);
                if (!string.IsNullOrWhiteSpace(secret))
                    request.Headers.TryAddWithoutValidation("Secret", secret);
            }
            if (!string.IsNullOrWhiteSpace(_csrfToken))
                cookieParts.Add($"kw_token={_csrfToken}");
            if (cookieParts.Count > 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookieParts));
        }

        public async Task<string> GetLyricAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";

            try
            {
                var url = "https://m.kuwo.cn/newh5/singles/songinfoandlrc?" + ToQueryString(new Dictionary<string, string>
                {
                    ["musicId"] = id,
                    ["httpsStatus"] = "1"
                });

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                request.Headers.TryAddWithoutValidation("Referer", "https://m.kuwo.cn/");
                using var lyricClient = new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                });
                using var response = await lyricClient.SendAsync(request, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                response.EnsureSuccessStatusCode();
                var data = ParseJsonObject(json)["data"] as JObject;
                var lrc = FormatLyricArray(data?["lrclist"] as JArray, "lineLyric", "lyric", "text");
                if (string.IsNullOrWhiteSpace(lrc)) return "";

                var tlyric = FormatLyricArray(data?["translist"] as JArray
                                              ?? data?["translate"] as JArray
                                              ?? data?["tlyric"] as JArray
                                              ?? data?["tlyriclist"] as JArray,
                    "lineLyric", "transLyric", "translation", "content", "text");

                if (string.IsNullOrWhiteSpace(tlyric))
                    tlyric = FormatLyricArray(data?["lrclist"] as JArray, "transLyric", "translation", "tran", "translate");

                return new JObject
                {
                    ["lrc"] = lrc,
                    ["tlyric"] = tlyric ?? "",
                    ["rlyric"] = ""
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Kuwo] Lyric failed: {Id}", id);
                return "";
            }
        }

        private static string ToQueryString(Dictionary<string, string> values)
        {
            return string.Join("&", values.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
        }

        public static string ExtractPlaylistId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = raw.Trim();
            var match = Regex.Match(raw, @"(?:playlist_detail/|pid=|id=)?(\d{3,})");
            return match.Success ? match.Groups[1].Value : raw;
        }

        private static string NormalizeId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            return raw.Replace("MUSIC_", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        private static string HtmlDecode(string value)
        {
            return WebUtility.HtmlDecode(value ?? "").Trim();
        }

        private static string NormalizeCover(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return raw.Replace("http://", "https://");
            if (raw.StartsWith("//")) return "https:" + raw;
            return "https://img1.kuwo.cn/star/albumcover/" + raw.TrimStart('/');
        }

        private static List<KuwoSongInfo> ParseSongs(JArray list)
        {
            var songs = new List<KuwoSongInfo>();
            foreach (var item in list ?? new JArray())
            {
                var id = NormalizeId(item["rid"]?.ToString()
                                     ?? item["musicrid"]?.ToString()
                                     ?? item["MUSICRID"]?.ToString()
                                     ?? item["id"]?.ToString());
                if (string.IsNullOrWhiteSpace(id)) continue;

                songs.Add(new KuwoSongInfo
                {
                    Id = id,
                    Title = HtmlDecode(item["name"]?.ToString() ?? item["NAME"]?.ToString() ?? item["songName"]?.ToString() ?? item["SONGNAME"]?.ToString() ?? ""),
                    Artist = HtmlDecode((item["artist"]?.ToString() ?? item["ARTIST"]?.ToString() ?? item["artistName"]?.ToString() ?? item["ARTISTNAME"]?.ToString() ?? "").Replace("&", ", ")),
                    Album = HtmlDecode(item["album"]?.ToString() ?? item["ALBUM"]?.ToString() ?? item["albumName"]?.ToString() ?? ""),
                    CoverUrl = NormalizeCover(item["pic"]?.ToString() ?? item["albumpic"]?.ToString() ?? item["web_albumpic_short"]?.ToString() ?? item["web_albumpic"]?.ToString()),
                    Duration = ParseDuration(item["duration"]?.ToString() ?? item["DURATION"]?.ToString())
                });
            }
            return songs;
        }

        private static float ParseDuration(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return 0;
            if (seconds > 10000) return seconds / 1000f;
            return seconds;
        }

        private static string FormatLrcTime(string value)
        {
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                return "00:00.00";

            var minutes = (int)(seconds / 60);
            var secs = seconds - minutes * 60;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00.00}", minutes, secs);
        }

        private static string FormatLyricArray(JArray list, params string[] textFields)
        {
            if (list == null || list.Count == 0) return "";

            var lines = new List<string>();
            foreach (var item in list)
            {
                var time = item["time"]?.ToString();
                if (string.IsNullOrWhiteSpace(time)) continue;

                var text = "";
                foreach (var field in textFields)
                {
                    text = item[field]?.ToString();
                    if (!string.IsNullOrWhiteSpace(text)) break;
                }
                if (string.IsNullOrWhiteSpace(text)) continue;
                lines.Add($"[{FormatLrcTime(time)}]{HtmlDecode(text)}");
            }

            return string.Join("\n", lines);
        }

        private static JObject ParseJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            json = json.Trim();
            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return JObject.Parse(json.Replace('\'', '"'));
            }
        }

        private static string BuildKuwoSecret(string cookieValue, string cookieName)
        {
            if (string.IsNullOrWhiteSpace(cookieValue) || string.IsNullOrWhiteSpace(cookieName))
                return "";

            var numberText = string.Concat(cookieName.Select(ch => ((int)ch).ToString(CultureInfo.InvariantCulture)));
            var offset = numberText.Length / 5;
            var factorText = string.Concat(new[]
            {
                CharAt(numberText, offset),
                CharAt(numberText, 2 * offset),
                CharAt(numberText, 3 * offset),
                CharAt(numberText, 4 * offset),
                CharAt(numberText, 5 * offset)
            });
            var factor = ParseJsInt(factorText);
            var increment = (int)Math.Ceiling(cookieName.Length / 2d);
            const double modulus = 2147483647d;
            if (factor < 2) return "";

            var random = RandomNumberGenerator.GetInt32(0, 100000000);
            numberText += random.ToString(CultureInfo.InvariantCulture);
            while (numberText.Length > 10)
            {
                var left = ParseJsNumber(numberText.Substring(0, 10));
                var right = ParseJsNumber(numberText.Substring(10));
                numberText = ToJsNumberString(left + right);
            }

            var seed = (factor * ParseJsNumber(numberText) + increment) % modulus;
            var builder = new StringBuilder();
            foreach (var ch in cookieValue)
            {
                var value = ((int)ch) ^ (int)Math.Floor(seed / modulus * 255d);
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                seed = (factor * seed + increment) % modulus;
            }

            builder.Append(random.ToString("x8", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static char CharAt(string value, int index)
        {
            return index >= 0 && index < value.Length ? value[index] : '\0';
        }

        private static int ParseJsInt(string value)
        {
            return (int)ParseJsNumber(value);
        }

        private static double ParseJsNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            var match = Regex.Match(value.TrimStart(), @"^[+-]?\d+");
            if (!match.Success) return 0;
            return double.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        private static string ToJsNumberString(double value)
        {
            if (Math.Abs(value) >= 1e21 || (Math.Abs(value) > 0 && Math.Abs(value) < 1e-6))
                return value.ToString("0.###############e+0", CultureInfo.InvariantCulture);
            return value.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
