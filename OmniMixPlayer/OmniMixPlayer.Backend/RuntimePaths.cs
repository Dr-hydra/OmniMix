using System;
using System.IO;

namespace OmniMixPlayer.Backend
{
    internal static class RuntimePaths
    {
        public static string ApplicationDirectory => ResolveApplicationDirectory();

        public static string ConfigDirectory => Path.Combine(ApplicationDirectory, "config");

        public static string LogsDirectory => Path.Combine(ApplicationDirectory, "logs");

        private static string ResolveApplicationDirectory()
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var processName = Path.GetFileNameWithoutExtension(processPath);
                if (!string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    var processDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
                    if (!string.IsNullOrWhiteSpace(processDirectory))
                        return processDirectory;
                }
            }

            return AppContext.BaseDirectory;
        }
    }
}
