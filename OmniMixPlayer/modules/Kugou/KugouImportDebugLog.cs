using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OmniMixPlayer.Module.Kugou
{
    internal static class KugouImportDebugLog
    {
        private static readonly object Sync = new();
        private static string _path;
        private static ILogger _logger;

        public static string Path => _path ?? "";

        public static void Initialize(ILogger logger, string path)
        {
            _logger = logger;
            Initialize(path);
        }

        public static void Initialize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (Sync)
            {
                _path = ResolvePath(path);
                if (_logger == null)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path) ?? ".");
                    File.AppendAllText(_path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} ===== Kugou debug log initialized ====={Environment.NewLine}", Encoding.UTF8);
                }
            }
        }

        public static void Write(string message)
        {
            if (_logger != null)
            {
                _logger.LogDebug("{Message}", message);
                return;
            }

            WriteFile(message);
        }

        public static void WriteWarning(string message)
        {
            if (_logger != null)
                _logger.LogWarning("{Message}", message);

            WriteFile("WARN " + message);
        }

        private static void WriteFile(string message)
        {
            if (string.IsNullOrWhiteSpace(_path)) return;
            lock (Sync)
            {
                File.AppendAllText(_path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }

        public static void Write(string message, Exception ex)
        {
            if (ex == null)
            {
                WriteWarning(message);
                return;
            }

            _logger?.LogWarning(ex, "{Message}", message);
            WriteFile($"WARN {message}: {ex.GetType().Name}: {ex.Message}");
        }

        public static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("\r", "\\r").Replace("\n", "\\n");
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private static string ResolvePath(string fallbackPath)
        {
            var localPath = System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "kugou_debug.log");
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localPath) ?? ".");
                return localPath;
            }
            catch
            {
                return fallbackPath;
            }
        }
    }
}
