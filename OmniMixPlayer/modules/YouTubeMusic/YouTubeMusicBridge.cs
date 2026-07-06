using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.SDK.Interfaces;

namespace OmniMixPlayer.Module.YouTubeMusic
{
    public sealed class YouTubeMusicBridge
    {
        private const string YtDlpWindowsDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const int ImportTimeoutMs = 120000;
        private const int ResolveTimeoutMs = 60000;
        private static readonly HttpClient ToolClient = new();
        private readonly ILogger _logger;
        private readonly string _moduleNativePath;
        private readonly string _toolDirectory;

        public YouTubeMusicBridge(ILogger logger, string moduleNativePath, string moduleDataPath)
        {
            _logger = logger;
            _moduleNativePath = moduleNativePath ?? "";
            _toolDirectory = Path.Combine(moduleDataPath ?? "", "bin");
        }

        public async Task<(bool ok, string version, string message)> CheckToolAsync(
            string configuredPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await RunYtDlpAsync(
                    configuredPath,
                    Array.Empty<string>(),
                    new[] { "--version" },
                    ResolveTimeoutMs,
                    cancellationToken).ConfigureAwait(false);

                var version = result.StdOut.Trim().Split(new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                return (true, version, string.IsNullOrWhiteSpace(version) ? "yt-dlp 可用" : $"yt-dlp {version}");
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        public async Task<string> DownloadYtDlpAsync(CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("当前自动下载只支持 Windows 版 yt-dlp.exe。");

            Directory.CreateDirectory(_toolDirectory);
            var targetPath = Path.Combine(_toolDirectory, "yt-dlp.exe");
            var tempPath = targetPath + ".download";

            using var request = new HttpRequestMessage(HttpMethod.Get, YtDlpWindowsDownloadUrl);
            request.Headers.UserAgent.ParseAdd("OmniMix/YouTubeMusic");
            using var response = await ToolClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(tempPath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            _logger?.LogInformation("[YouTubeMusic] yt-dlp downloaded to {Path}", targetPath);
            return targetPath;
        }

        public async Task<YouTubeMusicPlaylistImport> ImportAsync(
            string url,
            string configuredPath,
            string cookiesPath,
            int maxItems,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("YouTube URL is empty.", nameof(url));

            var args = new List<string>
            {
                "--dump-json",
                "--flat-playlist",
                "--skip-download",
                "--no-warnings",
                "--encoding",
                "UTF-8"
            };

            if (maxItems > 0)
            {
                args.Add("--playlist-end");
                args.Add(Math.Clamp(maxItems, 1, 500).ToString());
            }

            AddCookies(args, cookiesPath);
            args.Add("--");
            args.Add(url.Trim());

            var result = await RunYtDlpAsync(
                configuredPath,
                Array.Empty<string>(),
                args,
                ImportTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            var entries = ParseEntries(result.StdOut, url);
            var playlistId = "ytm_playlist_" + HashId(url);
            var playlistName = GuessPlaylistName(result.StdOut) ?? $"YouTube Music {ShortenUrl(url)}";

            return new YouTubeMusicPlaylistImport
            {
                Id = playlistId,
                Name = playlistName,
                Url = url.Trim(),
                Entries = entries
            };
        }

        public async Task<YouTubeMusicPlayable> ResolvePlayableAsync(
            string url,
            string configuredPath,
            string cookiesPath,
            string formatSelector,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("YouTube URL is empty.", nameof(url));

            var selector = string.IsNullOrWhiteSpace(formatSelector)
                ? "bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio/best"
                : formatSelector.Trim();

            var args = new List<string>
            {
                "--dump-json",
                "--no-playlist",
                "--skip-download",
                "--no-warnings",
                "--encoding",
                "UTF-8",
                "-f",
                selector
            };
            AddCookies(args, cookiesPath);
            args.Add("--");
            args.Add(url.Trim());

            var result = await RunYtDlpAsync(
                configuredPath,
                Array.Empty<string>(),
                args,
                ResolveTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            var playable = ParsePlayable(result.StdOut);
            if (string.IsNullOrWhiteSpace(playable.Url))
                throw new InvalidOperationException("yt-dlp did not return a playable audio URL.");

            return playable;
        }

        private async Task<ProcessResult> RunYtDlpAsync(
            string configuredPath,
            IEnumerable<string> prefixArgs,
            IEnumerable<string> args,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            var exe = ResolveYtDlpPath(configuredPath);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var arg in prefixArgs ?? Array.Empty<string>())
                process.StartInfo.ArgumentList.Add(arg);
            foreach (var arg in args ?? Array.Empty<string>())
                process.StartInfo.ArgumentList.Add(arg);

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法启动 yt-dlp：{exe}。请安装 yt-dlp 或在模块中填写 yt-dlp.exe 路径。{ex.Message}", ex);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw new TimeoutException($"yt-dlp 执行超时 ({timeoutMs / 1000}s)。");
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException($"yt-dlp 执行失败 ({process.ExitCode})：{TrimForStatus(message)}");
            }

            return new ProcessResult(stdout, stderr);
        }

        private string ResolveYtDlpPath(string configuredPath)
        {
            var value = (configuredPath ?? "").Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(value))
                return Environment.ExpandEnvironmentVariables(value);

            var downloadedExe = Path.Combine(_toolDirectory, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
            if (File.Exists(downloadedExe))
                return downloadedExe;

            var nativeExe = Path.Combine(_moduleNativePath, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
            if (File.Exists(nativeExe))
                return nativeExe;

            return OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
        }

        private static void AddCookies(List<string> args, string cookiesPath)
        {
            var value = (cookiesPath ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                return;

            value = Environment.ExpandEnvironmentVariables(value);
            if (!File.Exists(value))
                return;

            args.Add("--cookies");
            args.Add(value);
        }

        private static List<YouTubeMusicEntry> ParseEntries(string output, string sourceUrl)
        {
            var entries = new List<YouTubeMusicEntry>();
            foreach (var element in EnumerateJsonElements(output))
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                if (element.TryGetProperty("entries", out var nested) &&
                    nested.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in nested.EnumerateArray())
                    {
                        var entry = ParseEntry(item, sourceUrl);
                        if (entry != null) entries.Add(entry);
                    }
                    continue;
                }

                var direct = ParseEntry(element, sourceUrl);
                if (direct != null)
                    entries.Add(direct);
            }

            return entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Url))
                .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static YouTubeMusicEntry ParseEntry(JsonElement item, string sourceUrl)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            var id = GetString(item, "id");
            var rawUrl = GetString(item, "webpage_url");
            if (string.IsNullOrWhiteSpace(rawUrl))
                rawUrl = GetString(item, "url");

            if (!string.IsNullOrWhiteSpace(rawUrl) &&
                !rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                LooksLikeYouTubeId(rawUrl))
            {
                id = string.IsNullOrWhiteSpace(id) ? rawUrl : id;
                rawUrl = WatchUrl(rawUrl);
            }

            if (string.IsNullOrWhiteSpace(rawUrl) && LooksLikeYouTubeId(id))
                rawUrl = WatchUrl(id);

            if (string.IsNullOrWhiteSpace(id))
                id = ExtractVideoId(rawUrl) ?? HashId(rawUrl);

            var title = FirstNonEmpty(
                GetString(item, "title"),
                GetString(item, "fulltitle"),
                id);
            var artist = FirstNonEmpty(
                GetString(item, "artist"),
                GetString(item, "uploader"),
                GetString(item, "channel"),
                GetString(item, "creator"),
                "YouTube Music");

            return new YouTubeMusicEntry
            {
                Id = id,
                Title = title,
                Artist = artist,
                Album = FirstNonEmpty(GetString(item, "album"), GetString(item, "playlist_title")),
                Url = rawUrl,
                CoverUrl = FirstNonEmpty(GetString(item, "thumbnail"), GetLastThumbnail(item)),
                Duration = (float)GetDouble(item, "duration")
            };
        }

        private static YouTubeMusicPlayable ParsePlayable(string output)
        {
            var root = EnumerateJsonElements(output).FirstOrDefault(e => e.ValueKind == JsonValueKind.Object);
            if (root.ValueKind != JsonValueKind.Object)
                return new YouTubeMusicPlayable();

            var source = FindPlayableObject(root);
            var url = GetString(source, "url");
            var ext = FirstNonEmpty(GetString(source, "ext"), GetString(root, "ext"), GuessExtension(url));
            var playable = new YouTubeMusicPlayable
            {
                Url = url,
                Extension = NormalizeExtension(ext),
                Format = MapFormat(ext),
                FileSize = GetLong(source, "filesize") ?? GetLong(source, "filesize_approx")
                    ?? GetLong(root, "filesize") ?? GetLong(root, "filesize_approx"),
                ExpiresAt = ReadExpiry(root)
            };

            var headers = ReadHeaders(source);
            if (headers.Count == 0)
                headers = ReadHeaders(root);
            foreach (var pair in headers)
                playable.Headers[pair.Key] = pair.Value;

            if (!playable.Headers.ContainsKey("User-Agent"))
                playable.Headers["User-Agent"] = "Mozilla/5.0";
            if (!playable.Headers.ContainsKey("Referer"))
                playable.Headers["Referer"] = "https://www.youtube.com/";

            return playable;
        }

        private static JsonElement FindPlayableObject(JsonElement root)
        {
            if (!string.IsNullOrWhiteSpace(GetString(root, "url")))
                return root;

            if (root.TryGetProperty("requested_downloads", out var requestedDownloads) &&
                requestedDownloads.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requestedDownloads.EnumerateArray())
                {
                    if (!string.IsNullOrWhiteSpace(GetString(item, "url")))
                        return item;
                }
            }

            if (root.TryGetProperty("requested_formats", out var requestedFormats) &&
                requestedFormats.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requestedFormats.EnumerateArray())
                {
                    if (!string.IsNullOrWhiteSpace(GetString(item, "url")) &&
                        !string.Equals(GetString(item, "vcodec"), "none", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(GetString(item, "url")))
                        return item;
                }
            }

            if (root.TryGetProperty("formats", out var formats) &&
                formats.ValueKind == JsonValueKind.Array)
            {
                JsonElement? fallback = null;
                foreach (var item in formats.EnumerateArray())
                {
                    if (string.IsNullOrWhiteSpace(GetString(item, "url")))
                        continue;
                    if (!string.Equals(GetString(item, "vcodec"), "none", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var ext = GetString(item, "ext");
                    if (string.Equals(ext, "m4a", StringComparison.OrdinalIgnoreCase))
                        return item;
                    fallback ??= item;
                }
                if (fallback.HasValue)
                    return fallback.Value;
            }

            return root;
        }

        private static IEnumerable<JsonElement> EnumerateJsonElements(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                yield break;

            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch
                {
                    continue;
                }

                using (doc)
                {
                    yield return doc.RootElement.Clone();
                }
            }
        }

        private static string GuessPlaylistName(string output)
        {
            foreach (var element in EnumerateJsonElements(output))
            {
                var name = FirstNonEmpty(
                    GetString(element, "playlist_title"),
                    GetString(element, "playlist"),
                    GetString(element, "title"));
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            return null;
        }

        private static Dictionary<string, string> ReadHeaders(JsonElement element)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("http_headers", out var obj) ||
                obj.ValueKind != JsonValueKind.Object)
            {
                return headers;
            }

            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    headers[prop.Name] = prop.Value.GetString() ?? "";
            }
            return headers;
        }

        private static DateTime? ReadExpiry(JsonElement element)
        {
            var expiry = GetLong(element, "expires") ?? GetLong(element, "expiration");
            if (!expiry.HasValue)
                return DateTime.UtcNow.AddHours(5);

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(expiry.Value).UtcDateTime;
            }
            catch
            {
                return DateTime.UtcNow.AddHours(5);
            }
        }

        private static string GetString(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out var prop))
            {
                return "";
            }

            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString() ?? "",
                JsonValueKind.Number => prop.TryGetInt64(out var number) ? number.ToString() : prop.ToString(),
                _ => ""
            };
        }

        private static double GetDouble(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out var prop))
            {
                return 0;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var value))
                return value;
            if (prop.ValueKind == JsonValueKind.String &&
                double.TryParse(prop.GetString(), out value))
            {
                return value;
            }
            return 0;
        }

        private static long? GetLong(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out var prop))
            {
                return null;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var value))
                return value;
            if (prop.ValueKind == JsonValueKind.String &&
                long.TryParse(prop.GetString(), out value))
            {
                return value;
            }
            return null;
        }

        private static string GetLastThumbnail(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("thumbnails", out var thumbnails) ||
                thumbnails.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            var result = "";
            foreach (var thumb in thumbnails.EnumerateArray())
            {
                var url = GetString(thumb, "url");
                if (!string.IsNullOrWhiteSpace(url))
                    result = url;
            }
            return result;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private static string NormalizeExtension(string ext)
        {
            ext = (ext ?? "").Trim().TrimStart('.').ToLowerInvariant();
            return string.IsNullOrWhiteSpace(ext) ? "m4a" : ext;
        }

        private static AudioFormat MapFormat(string ext)
        {
            return NormalizeExtension(ext) switch
            {
                "mp3" => AudioFormat.Mp3,
                "m4a" or "mp4" or "aac" => AudioFormat.Aac,
                "ogg" or "opus" or "webm" => AudioFormat.Ogg,
                "flac" => AudioFormat.Flac,
                "wav" => AudioFormat.Wav,
                _ => AudioFormat.Unknown
            };
        }

        private static string GuessExtension(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";
            try
            {
                var uri = new Uri(url);
                return Path.GetExtension(uri.AbsolutePath).TrimStart('.');
            }
            catch
            {
                return "";
            }
        }

        private static string WatchUrl(string id) => $"https://www.youtube.com/watch?v={id}";

        private static bool LooksLikeYouTubeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 11)
                return false;
            return value.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
        }

        private static string ExtractVideoId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            try
            {
                var uri = new Uri(url);
                if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                    return uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault();

                var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in query)
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length == 2 && parts[0] == "v")
                        return Uri.UnescapeDataString(parts[1]);
                }
            }
            catch { }
            return null;
        }

        public static string HashId(string text)
        {
            using var md5 = MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();
        }

        public static string GenerateUuid(string videoIdOrUrl)
        {
            using var md5 = MD5.Create();
            var key = "youtube_music_" + (videoIdOrUrl ?? "");
            return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(key))).ToString("N");
        }

        private static string ShortenUrl(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length <= 48) return url;
            return url[..45] + "...";
        }

        private static string TrimForStatus(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length <= 600) return text;
            return text[..600] + "...";
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        }

        private readonly record struct ProcessResult(string StdOut, string StdErr);
    }
}
