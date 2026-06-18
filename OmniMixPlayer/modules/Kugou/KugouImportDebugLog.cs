using System;
using System.IO;
using System.Text;

namespace OmniMixPlayer.Module.Kugou
{
    internal static class KugouImportDebugLog
    {
        private static readonly object Sync = new();
        private static string _path;

        public static string Path => _path ?? "";

        public static void Initialize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (Sync)
            {
                _path = ResolvePath(path);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path) ?? ".");
                File.AppendAllText(_path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} ===== Kugou import debug log initialized ====={Environment.NewLine}", Encoding.UTF8);
            }
        }

        public static void Write(string message)
        {
            if (string.IsNullOrWhiteSpace(_path)) return;
            lock (Sync)
            {
                File.AppendAllText(_path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }

        public static void Write(string message, Exception ex)
        {
            Write(message + Environment.NewLine + ex);
        }

        public static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("\r", "\\r").Replace("\n", "\\n");
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private static string ResolvePath(string fallbackPath)
        {
            var localPath = System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "kugou_import_debug.log");
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
