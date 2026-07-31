using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace OmniMixPlayer.SDK.Caching
{
    /// <summary>
    /// Resolves the shared OmniMix cache root and its stable category directories.
    /// </summary>
    public static class CachePaths
    {
        public const string RootEnvironmentVariable = "OMNIMIX_CACHE_ROOT";
        public const string MaximumBytesEnvironmentVariable = "OMNIMIX_CACHE_MAX_BYTES";
        public const long DefaultMaximumBytes = 2L * 1024 * 1024 * 1024;

        public const string StreamingCategory = "Streaming";
        public const string ModulesCategory = "Modules";
        public const string CoversCategory = "Covers";
        public const string DjCategory = "DJ";
        public const string FrontendCategory = "Frontend";
        public const string TempCategory = "Temp";

        private static readonly object ConfigurationSync = new();
        private static string _runtimeRootDirectory;
        private static long? _runtimeMaximumBytes;

        private static readonly ReadOnlyCollection<string> CategoryNames = Array.AsReadOnly(new[]
        {
            StreamingCategory,
            ModulesCategory,
            CoversCategory,
            DjCategory,
            FrontendCategory,
            TempCategory
        });

        public static IReadOnlyList<string> KnownCategories => CategoryNames;

        /// <summary>
        /// Gets the configured cache root without creating it.
        /// </summary>
        public static string RootDirectory => ResolveRootDirectory();

        /// <summary>
        /// Gets the configured total cache quota in bytes.
        /// </summary>
        public static long MaximumBytes => ResolveMaximumBytes();

        public static string StreamingDirectory => GetCategoryDirectory(StreamingCategory);

        public static string ModulesDirectory => GetCategoryDirectory(ModulesCategory);

        public static string CoversDirectory => GetCategoryDirectory(CoversCategory);

        public static string DjDirectory => GetCategoryDirectory(DjCategory);

        public static string FrontendDirectory => GetCategoryDirectory(FrontendCategory);

        public static string TempDirectory => GetCategoryDirectory(TempCategory);

        /// <summary>
        /// Applies cache settings received from the running OmniMix host. New cache
        /// operations use the new root immediately; existing open files remain valid.
        /// </summary>
        public static void Configure(string rootDirectory, long? maximumBytes = null)
        {
            string normalizedRoot = null;
            if (!string.IsNullOrWhiteSpace(rootDirectory))
            {
                var expanded = Environment.ExpandEnvironmentVariables(rootDirectory.Trim());
                normalizedRoot = Path.GetFullPath(expanded, AppContext.BaseDirectory);
            }

            if (maximumBytes.HasValue && maximumBytes.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));

            lock (ConfigurationSync)
            {
                _runtimeRootDirectory = normalizedRoot;
                _runtimeMaximumBytes = maximumBytes;
            }
        }

        /// <summary>
        /// Gets the pre-OmniMix shared audio cache location for lazy migration only.
        /// New files must not be written here.
        /// </summary>
        public static string LegacySharedAudioDirectory =>
            Path.Combine(Path.GetTempPath(), "chillpatcher_audio_cache");

        public static string EnsureRootDirectory()
        {
            var root = RootDirectory;
            Directory.CreateDirectory(root);
            return root;
        }

        public static string GetCategoryDirectory(string category)
        {
            var directory = Path.Combine(RootDirectory, ValidatePathSegment(category, nameof(category)));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public static string GetModuleDirectory(string moduleName)
        {
            var directory = Path.Combine(
                RootDirectory,
                ModulesCategory,
                ValidatePathSegment(moduleName, nameof(moduleName)));
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal static string ValidatePathSegment(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A cache path segment cannot be empty.", parameterName);

            if (value == "." || value == ".." ||
                value.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                value.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("A cache path segment must be a single valid directory name.", parameterName);
            }

            return value;
        }

        private static string ResolveRootDirectory()
        {
            lock (ConfigurationSync)
            {
                if (!string.IsNullOrWhiteSpace(_runtimeRootDirectory))
                    return _runtimeRootDirectory;
            }

            var configured = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    configured = Environment.ExpandEnvironmentVariables(configured.Trim());
                    return Path.GetFullPath(configured, AppContext.BaseDirectory);
                }
                catch (Exception ex) when (ex is ArgumentException ||
                                           ex is NotSupportedException ||
                                           ex is PathTooLongException)
                {
                    // Invalid external configuration falls back to the safe per-user default.
                }
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = Path.GetTempPath();

            return Path.GetFullPath(Path.Combine(localAppData, "OmniMix", "Cache"));
        }

        private static long ResolveMaximumBytes()
        {
            lock (ConfigurationSync)
            {
                if (_runtimeMaximumBytes.HasValue)
                    return _runtimeMaximumBytes.Value;
            }

            var configured = Environment.GetEnvironmentVariable(MaximumBytesEnvironmentVariable);
            return long.TryParse(configured, out var maximumBytes) && maximumBytes > 0
                ? maximumBytes
                : DefaultMaximumBytes;
        }
    }
}
