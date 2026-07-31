using System;
using System.IO;
using System.Linq;
using OmniMixPlayer.SDK.Caching;
using Xunit;

namespace OmniMixPlayer.SDK.Tests.Caching
{
    public sealed class CacheQuotaManagerTests
    {
        [Fact]
        public void CachePathsHonorEnvironmentVariables()
        {
            var originalRoot = Environment.GetEnvironmentVariable(CachePaths.RootEnvironmentVariable);
            var originalMaximum = Environment.GetEnvironmentVariable(CachePaths.MaximumBytesEnvironmentVariable);
            var root = CreateTemporaryRoot();

            try
            {
                Environment.SetEnvironmentVariable(CachePaths.RootEnvironmentVariable, root);
                Environment.SetEnvironmentVariable(CachePaths.MaximumBytesEnvironmentVariable, "123456");

                Assert.Equal(Path.GetFullPath(root), CachePaths.RootDirectory);
                Assert.Equal(123456, CachePaths.MaximumBytes);
                Assert.Equal(Path.Combine(root, CachePaths.StreamingCategory), CachePaths.StreamingDirectory);
            }
            finally
            {
                Environment.SetEnvironmentVariable(CachePaths.RootEnvironmentVariable, originalRoot);
                Environment.SetEnvironmentVariable(CachePaths.MaximumBytesEnvironmentVariable, originalMaximum);
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void StatisticsIncludeKnownCategoriesAndNestedModuleFiles()
        {
            var root = CreateTemporaryRoot();
            try
            {
                WriteFile(Path.Combine(root, CachePaths.StreamingCategory, "stream.bin"), 3);
                WriteFile(Path.Combine(root, CachePaths.ModulesCategory, "Netease", "song.bin"), 5);

                var statistics = new CacheQuotaManager(root, 100).GetStatistics();

                Assert.Equal(8, statistics.TotalBytes);
                Assert.Equal(2, statistics.FileCount);
                Assert.Equal(3, statistics.Categories.Single(item => item.Name == CachePaths.StreamingCategory).SizeBytes);
                Assert.Equal(5, statistics.Categories.Single(item => item.Name == CachePaths.ModulesCategory).SizeBytes);
                Assert.Contains(statistics.Categories, item => item.Name == CachePaths.DjCategory && item.SizeBytes == 0);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void RuntimeConfigurationRebindsDefaultManager()
        {
            var firstRoot = CreateTemporaryRoot();
            var secondRoot = CreateTemporaryRoot();
            try
            {
                CachePaths.Configure(firstRoot, 100);
                var first = CacheQuotaManager.Default;

                CachePaths.Configure(secondRoot, 200);
                var second = CacheQuotaManager.Default;

                Assert.NotSame(first, second);
                Assert.Equal(Path.GetFullPath(secondRoot), second.RootDirectory);
                Assert.Equal(200, second.MaximumBytes);
            }
            finally
            {
                CachePaths.Configure(null, null);
                DeleteDirectory(firstRoot);
                DeleteDirectory(secondRoot);
            }
        }

        [Fact]
        public void EnforceQuotaDeletesLeastRecentlyUsedFileFirst()
        {
            var root = CreateTemporaryRoot();
            try
            {
                var oldPath = WriteFile(Path.Combine(root, CachePaths.StreamingCategory, "old.bin"), 4);
                var newPath = WriteFile(Path.Combine(root, CachePaths.StreamingCategory, "new.bin"), 4);
                SetLastUsed(oldPath, DateTime.UtcNow.AddHours(-2));
                SetLastUsed(newPath, DateTime.UtcNow.AddHours(-1));

                var result = new CacheQuotaManager(root, 4).EnforceQuota();

                Assert.True(result.QuotaSatisfied);
                Assert.Equal(1, result.DeletedFileCount);
                Assert.False(File.Exists(oldPath));
                Assert.True(File.Exists(newPath));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void EnforceQuotaSkipsLockedFiles()
        {
            var root = CreateTemporaryRoot();
            try
            {
                var lockedPath = WriteFile(Path.Combine(root, CachePaths.StreamingCategory, "locked.bin"), 8);
                var removablePath = WriteFile(Path.Combine(root, CachePaths.StreamingCategory, "removable.bin"), 2);
                SetLastUsed(lockedPath, DateTime.UtcNow.AddHours(-2));
                SetLastUsed(removablePath, DateTime.UtcNow.AddHours(-1));

                CacheCleanupResult result;
                using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    result = new CacheQuotaManager(root, 1).EnforceQuota();
                }

                Assert.False(result.QuotaSatisfied);
                Assert.Equal(1, result.SkippedLockedFileCount);
                Assert.True(File.Exists(lockedPath));
                Assert.False(File.Exists(removablePath));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void ClearCategoryDoesNotDeleteOtherCategories()
        {
            var root = CreateTemporaryRoot();
            try
            {
                var streamingPath = WriteFile(Path.Combine(root, CachePaths.StreamingCategory, "stream.bin"), 3);
                var djPath = WriteFile(Path.Combine(root, CachePaths.DjCategory, "voice.bin"), 5);

                var result = new CacheQuotaManager(root, 100).ClearCategory(CachePaths.StreamingCategory);

                Assert.True(result.QuotaSatisfied);
                Assert.False(File.Exists(streamingPath));
                Assert.True(File.Exists(djPath));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static string CreateTemporaryRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "OmniMixCacheTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string WriteFile(string path, int length)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[length]);
            return path;
        }

        private static void SetLastUsed(string path, DateTime value)
        {
            File.SetLastWriteTimeUtc(path, value);
            File.SetLastAccessTimeUtc(path, value);
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
