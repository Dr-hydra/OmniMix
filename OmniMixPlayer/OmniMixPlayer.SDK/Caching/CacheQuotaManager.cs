using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmniMixPlayer.SDK.Caching
{
    public sealed record CacheCategoryStatistics(string Name, long SizeBytes, int FileCount);

    public sealed record CacheStatistics(
        string RootDirectory,
        long MaximumBytes,
        long TotalBytes,
        int FileCount,
        IReadOnlyList<CacheCategoryStatistics> Categories);

    public sealed record CacheCleanupResult(
        long BeforeBytes,
        long AfterBytes,
        long DeletedBytes,
        int DeletedFileCount,
        int SkippedLockedFileCount,
        int FailedFileCount,
        bool QuotaSatisfied);

    /// <summary>
    /// Collects cache usage and enforces a shared least-recently-used quota.
    /// Files which cannot be opened exclusively, or which are explicitly protected,
    /// are left in place.
    /// </summary>
    public sealed class CacheQuotaManager
    {
        private const string OtherCategory = "Other";
        private static readonly object DefaultSync = new();
        private static CacheQuotaManager _default;
        private readonly object _sync = new();
        private readonly StringComparer _pathComparer;

        public static CacheQuotaManager Default
        {
            get
            {
                var root = CachePaths.RootDirectory;
                var maximum = CachePaths.MaximumBytes;
                var comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                lock (DefaultSync)
                {
                    if (_default == null ||
                        !string.Equals(_default.RootDirectory, root, comparison) ||
                        _default.MaximumBytes != maximum)
                    {
                        _default = new CacheQuotaManager(root, maximum);
                    }
                    return _default;
                }
            }
        }

        public CacheQuotaManager(string rootDirectory = null, long? maximumBytes = null)
        {
            var resolvedRoot = string.IsNullOrWhiteSpace(rootDirectory)
                ? CachePaths.RootDirectory
                : rootDirectory;

            RootDirectory = Path.GetFullPath(resolvedRoot);
            MaximumBytes = maximumBytes ?? CachePaths.MaximumBytes;
            if (MaximumBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));

            _pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        public string RootDirectory { get; }

        public long MaximumBytes { get; }

        public CacheStatistics GetStatistics()
        {
            lock (_sync)
            {
                var categorySizes = new Dictionary<string, (long Size, int Count)>(StringComparer.OrdinalIgnoreCase);
                foreach (var category in CachePaths.KnownCategories)
                    categorySizes[category] = (0, 0);

                long totalBytes = 0;
                var fileCount = 0;
                foreach (var file in EnumerateCacheFiles(RootDirectory))
                {
                    totalBytes += file.SizeBytes;
                    fileCount++;

                    var category = GetCategory(file.Path);
                    (long Size, int Count) current = categorySizes.TryGetValue(category, out var value)
                        ? value
                        : (0L, 0);
                    categorySizes[category] = (current.Size + file.SizeBytes, current.Count + 1);
                }

                var categories = categorySizes
                    .Select(pair => new CacheCategoryStatistics(pair.Key, pair.Value.Size, pair.Value.Count))
                    .OrderBy(category => CategorySortIndex(category.Name))
                    .ThenBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new CacheStatistics(
                    RootDirectory,
                    MaximumBytes,
                    totalBytes,
                    fileCount,
                    categories);
            }
        }

        /// <summary>
        /// Deletes the least recently used files until the configured quota is met.
        /// </summary>
        public CacheCleanupResult EnforceQuota(IEnumerable<string> protectedPaths = null)
        {
            lock (_sync)
            {
                var files = EnumerateCacheFiles(RootDirectory).ToArray();
                var beforeBytes = files.Sum(file => file.SizeBytes);
                if (beforeBytes <= MaximumBytes)
                    return new CacheCleanupResult(beforeBytes, beforeBytes, 0, 0, 0, 0, true);

                return DeleteCandidates(
                    files.OrderBy(file => file.LastUsedUtc).ThenBy(file => file.Path, _pathComparer),
                    beforeBytes,
                    MaximumBytes,
                    protectedPaths);
            }
        }

        public CacheCleanupResult ClearAll(IEnumerable<string> protectedPaths = null)
        {
            lock (_sync)
            {
                var files = EnumerateCacheFiles(RootDirectory).ToArray();
                var beforeBytes = files.Sum(file => file.SizeBytes);
                return DeleteCandidates(files, beforeBytes, 0, protectedPaths);
            }
        }

        public CacheCleanupResult ClearCategory(string category, IEnumerable<string> protectedPaths = null)
        {
            var validCategory = CachePaths.ValidatePathSegment(category, nameof(category));
            var categoryDirectory = Path.Combine(RootDirectory, validCategory);

            lock (_sync)
            {
                var files = EnumerateCacheFiles(categoryDirectory).ToArray();
                var beforeBytes = files.Sum(file => file.SizeBytes);
                return DeleteCandidates(files, beforeBytes, 0, protectedPaths);
            }
        }

        /// <summary>
        /// Updates the LRU timestamp for a cache file. Paths outside this manager's root are ignored.
        /// </summary>
        public bool Touch(string path)
        {
            if (!TryNormalizeCachePath(path, out var fullPath) || !File.Exists(fullPath))
                return false;

            try
            {
                File.SetLastAccessTimeUtc(fullPath, DateTime.UtcNow);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private CacheCleanupResult DeleteCandidates(
            IEnumerable<CacheFileEntry> candidates,
            long beforeBytes,
            long targetBytes,
            IEnumerable<string> protectedPaths)
        {
            var protectedSet = BuildProtectedPathSet(protectedPaths);
            var remainingBytes = beforeBytes;
            long deletedBytes = 0;
            var deletedCount = 0;
            var lockedCount = 0;
            var failedCount = 0;

            foreach (var file in candidates)
            {
                if (remainingBytes <= targetBytes)
                    break;

                if (protectedSet.Contains(file.Path))
                {
                    lockedCount++;
                    continue;
                }

                var result = TryDelete(file.Path);
                switch (result)
                {
                    case DeleteResult.Deleted:
                    case DeleteResult.AlreadyMissing:
                        remainingBytes = Math.Max(0, remainingBytes - file.SizeBytes);
                        deletedBytes += file.SizeBytes;
                        deletedCount++;
                        break;
                    case DeleteResult.Locked:
                        lockedCount++;
                        break;
                    default:
                        failedCount++;
                        break;
                }
            }

            RemoveEmptyDirectories();
            return new CacheCleanupResult(
                beforeBytes,
                remainingBytes,
                deletedBytes,
                deletedCount,
                lockedCount,
                failedCount,
                remainingBytes <= targetBytes);
        }

        private HashSet<string> BuildProtectedPathSet(IEnumerable<string> protectedPaths)
        {
            var result = new HashSet<string>(_pathComparer);
            if (protectedPaths == null)
                return result;

            foreach (var path in protectedPaths)
            {
                if (TryNormalizeCachePath(path, out var fullPath))
                    result.Add(fullPath);
            }

            return result;
        }

        private DeleteResult TryDelete(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                }
            }
            catch (FileNotFoundException)
            {
                return DeleteResult.AlreadyMissing;
            }
            catch (DirectoryNotFoundException)
            {
                return DeleteResult.AlreadyMissing;
            }
            catch (IOException)
            {
                return DeleteResult.Locked;
            }
            catch (UnauthorizedAccessException)
            {
                return DeleteResult.Failed;
            }

            try
            {
                File.Delete(path);
                return DeleteResult.Deleted;
            }
            catch (FileNotFoundException)
            {
                return DeleteResult.AlreadyMissing;
            }
            catch (DirectoryNotFoundException)
            {
                return DeleteResult.AlreadyMissing;
            }
            catch (IOException)
            {
                return DeleteResult.Locked;
            }
            catch (UnauthorizedAccessException)
            {
                return DeleteResult.Failed;
            }
        }

        private IEnumerable<CacheFileEntry> EnumerateCacheFiles(string startDirectory)
        {
            if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
                yield break;

            var pending = new Stack<string>();
            pending.Push(startDirectory);

            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                string[] files;
                string[] subdirectories;

                try
                {
                    files = Directory.GetFiles(directory);
                    subdirectories = Directory.GetDirectories(directory);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var filePath in files)
                {
                    if (!TryNormalizeCachePath(filePath, out var fullPath))
                        continue;

                    CacheFileEntry entry = null;
                    try
                    {
                        var info = new FileInfo(fullPath);
                        var lastUsedUtc = info.LastAccessTimeUtc > info.LastWriteTimeUtc
                            ? info.LastAccessTimeUtc
                            : info.LastWriteTimeUtc;
                        entry = new CacheFileEntry(fullPath, info.Length, lastUsedUtc);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    if (entry != null)
                        yield return entry;
                }

                foreach (var subdirectory in subdirectories)
                {
                    try
                    {
                        if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(subdirectory);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        private string GetCategory(string path)
        {
            var relative = Path.GetRelativePath(RootDirectory, path);
            var separatorIndex = relative.IndexOfAny(new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            });
            var firstSegment = separatorIndex < 0 ? string.Empty : relative[..separatorIndex];

            foreach (var category in CachePaths.KnownCategories)
            {
                if (string.Equals(category, firstSegment, StringComparison.OrdinalIgnoreCase))
                    return category;
            }

            return OtherCategory;
        }

        private int CategorySortIndex(string category)
        {
            for (var index = 0; index < CachePaths.KnownCategories.Count; index++)
            {
                if (string.Equals(CachePaths.KnownCategories[index], category, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return int.MaxValue;
        }

        private bool TryNormalizeCachePath(string path, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                fullPath = Path.GetFullPath(path);
                var rootWithSeparator = Path.TrimEndingDirectorySeparator(RootDirectory) + Path.DirectorySeparatorChar;
                return fullPath.StartsWith(rootWithSeparator,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is ArgumentException ||
                                       ex is NotSupportedException ||
                                       ex is PathTooLongException)
            {
                fullPath = null;
                return false;
            }
        }

        private void RemoveEmptyDirectories()
        {
            if (!Directory.Exists(RootDirectory))
                return;

            var directories = new List<string>();
            var pending = new Stack<string>();
            pending.Push(RootDirectory);

            while (pending.Count > 0)
            {
                string[] children;
                try
                {
                    children = Directory.GetDirectories(pending.Pop());
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;

                        directories.Add(child);
                        pending.Push(child);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            foreach (var directory in directories.OrderByDescending(path => path.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private sealed record CacheFileEntry(string Path, long SizeBytes, DateTime LastUsedUtc);

        private enum DeleteResult
        {
            Deleted,
            AlreadyMissing,
            Locked,
            Failed
        }
    }
}
