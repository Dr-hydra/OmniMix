using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        private readonly ILogger _logger;
        private readonly HttpClient _client;
        private string _csrfToken = Token;

        public KuwoBridge(ILogger logger)
        {
            _logger = logger;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
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
                var query = new Dictionary<string, string>
                {
                    ["all"] = keyword,
                    ["pn"] = Math.Max(1, page).ToString(),
                    ["rn"] = Math.Max(1, limit).ToString(),
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
                var root = JObject.Parse(json);
                var list = root["abslist"] as JArray ?? root["data"]?["list"] as JArray ?? root["list"] as JArray;
                var songs = new List<KuwoSongInfo>();

                foreach (var item in list ?? new JArray())
                {
                    var rawId = item["MUSICRID"]?.ToString() ?? item["musicrid"]?.ToString() ?? item["id"]?.ToString();
                    var id = NormalizeId(rawId);
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    songs.Add(new KuwoSongInfo
                    {
                        Id = id,
                        Title = HtmlDecode(item["NAME"]?.ToString() ?? item["name"]?.ToString() ?? ""),
                        Artist = HtmlDecode((item["ARTIST"]?.ToString() ?? item["artist"]?.ToString() ?? "").Replace("&", ", ")),
                        Album = HtmlDecode(item["ALBUM"]?.ToString() ?? item["album"]?.ToString() ?? ""),
                        CoverUrl = NormalizeCover(item["web_albumpic_short"]?.ToString() ?? item["pic"]?.ToString()),
                        Duration = ParseDuration(item["DURATION"]?.ToString() ?? item["duration"]?.ToString())
                    });
                }

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
                var root = JObject.Parse(json);
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

        private async Task<(KuwoPlaylistInfo playlist, List<KuwoSongInfo> songs)> GetPlaylistFromWebApiAsync(string id, int page, int limit, CancellationToken cancellationToken)
        {
            try
            {
                await EnsureWebTokenAsync(cancellationToken);
                var query = new Dictionary<string, string>
                {
                    ["pid"] = id,
                    ["pn"] = Math.Max(1, page).ToString(),
                    ["rn"] = Math.Clamp(limit, 1, 1000).ToString(),
                    ["httpsStatus"] = "1",
                    ["reqId"] = Guid.NewGuid().ToString()
                };
                var url = "https://www.kuwo.cn/api/www/playlist/playListInfo?" + ToQueryString(query);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                request.Headers.TryAddWithoutValidation("Referer", $"https://www.kuwo.cn/playlist_detail/{id}");
                request.Headers.TryAddWithoutValidation("csrf", _csrfToken);
                request.Headers.TryAddWithoutValidation("Cookie", $"kw_token={_csrfToken}");

                using var response = await _client.SendAsync(request, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                response.EnsureSuccessStatusCode();
                var root = JObject.Parse(json);
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
                var root = JObject.Parse(json);
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

        private async Task EnsureWebTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_csrfToken) && _csrfToken != Token) return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.kuwo.cn/");
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
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        public async Task<string> GetLyricAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";

            try
            {
                var url = "http://m.kuwo.cn/newh5/singles/songinfoandlrc?" + ToQueryString(new Dictionary<string, string>
                {
                    ["musicId"] = id,
                    ["httpsStatus"] = "1"
                });

                var json = await _client.GetStringAsync(url, cancellationToken);
                var list = JObject.Parse(json)["data"]?["lrclist"] as JArray;
                if (list == null || list.Count == 0) return "";

                var lines = new List<string>();
                foreach (var item in list)
                {
                    var time = item["time"]?.ToString();
                    var text = item["lineLyric"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(time)) continue;
                    lines.Add($"[{FormatLrcTime(time)}]{text}");
                }

                return string.Join("\n", lines);
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
            return raw.Replace("MUSIC_", "").Trim();
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
                    Title = HtmlDecode(item["name"]?.ToString() ?? item["NAME"]?.ToString() ?? item["songName"]?.ToString() ?? ""),
                    Artist = HtmlDecode((item["artist"]?.ToString() ?? item["ARTIST"]?.ToString() ?? item["artistName"]?.ToString() ?? "").Replace("&", ", ")),
                    Album = HtmlDecode(item["album"]?.ToString() ?? item["ALBUM"]?.ToString() ?? item["albumName"]?.ToString() ?? ""),
                    CoverUrl = NormalizeCover(item["pic"]?.ToString() ?? item["albumpic"]?.ToString() ?? item["web_albumpic_short"]?.ToString()),
                    Duration = ParseDuration(item["duration"]?.ToString() ?? item["DURATION"]?.ToString())
                });
            }
            return songs;
        }

        private static float ParseDuration(string value)
        {
            if (float.TryParse(value, out var seconds)) return seconds;
            return 0;
        }

        private static string FormatLrcTime(string value)
        {
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                return "00:00.00";

            var minutes = (int)(seconds / 60);
            var secs = seconds - minutes * 60;
            return $"{minutes:00}:{secs:00.00}";
        }
    }
}
