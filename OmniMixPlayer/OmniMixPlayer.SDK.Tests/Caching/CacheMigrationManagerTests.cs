using System;
using System.IO;
using OmniMixPlayer.SDK.Caching;
using Xunit;

namespace OmniMixPlayer.SDK.Tests.Caching
{
    public sealed class CacheMigrationManagerTests
    {
        [Fact]
        public void MoveDirectoryContentsCopiesThenRemovesSourceAndPreservesLayout()
        {
            var source = CreateTemporaryRoot();
            var destination = CreateTemporaryRoot();
            try
            {
                var sourceFile = WriteFile(Path.Combine(source, "DJ", "Host", "intro.wav"), 7);
                var sourceTime = DateTime.UtcNow.AddMinutes(-5);
                File.SetLastWriteTimeUtc(sourceFile, sourceTime);

                var result = new CacheMigrationManager().MoveDirectoryContents(source, destination);
                var targetFile = Path.Combine(destination, "DJ", "Host", "intro.wav");

                Assert.True(File.Exists(targetFile));
                Assert.False(File.Exists(sourceFile));
                Assert.Equal(7, result.CopiedBytes);
                Assert.Equal(7, result.DeletedSourceBytes);
                Assert.Equal(1, result.CopiedFileCount);
                Assert.Equal(1, result.DeletedSourceFileCount);
                Assert.False(result.SourceHasRemainingEntries);
                Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(targetFile));
            }
            finally
            {
                DeleteDirectory(source);
                DeleteDirectory(destination);
            }
        }

        [Fact]
        public void MoveDirectoryContentsKeepsLockedSourceForRetry()
        {
            var source = CreateTemporaryRoot();
            var destination = CreateTemporaryRoot();
            try
            {
                var sourceFile = WriteFile(Path.Combine(source, "Streaming", "active.bin"), 5);

                CacheMigrationResult result;
                using (new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    result = new CacheMigrationManager().MoveDirectoryContents(source, destination);
                }

                Assert.True(File.Exists(sourceFile));
                Assert.False(File.Exists(Path.Combine(destination, "Streaming", "active.bin")));
                Assert.Equal(1, result.SkippedLockedFileCount);
                Assert.True(result.SourceHasRemainingEntries);
            }
            finally
            {
                DeleteDirectory(source);
                DeleteDirectory(destination);
            }
        }

        [Fact]
        public void MoveDirectoryContentsRejectsOverlappingRoots()
        {
            var root = CreateTemporaryRoot();
            try
            {
                var nestedDestination = Path.Combine(root, "new-cache");

                Assert.Throws<ArgumentException>(() =>
                    new CacheMigrationManager().MoveDirectoryContents(root, nestedDestination));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public void MoveDirectoryContentsReplacesAnOlderDestinationEntry()
        {
            var source = CreateTemporaryRoot();
            var destination = CreateTemporaryRoot();
            try
            {
                var sourceFile = WriteFile(Path.Combine(source, "Streaming", "track.bin"), 7);
                var destinationFile = WriteFile(Path.Combine(destination, "Streaming", "track.bin"), 3);
                File.SetLastWriteTimeUtc(destinationFile, DateTime.UtcNow.AddMinutes(-10));
                File.SetLastWriteTimeUtc(sourceFile, DateTime.UtcNow);

                var result = new CacheMigrationManager().MoveDirectoryContents(source, destination);

                Assert.False(File.Exists(sourceFile));
                Assert.Equal(7, new FileInfo(destinationFile).Length);
                Assert.Equal(1, result.CopiedFileCount);
                Assert.Equal(1, result.DeletedSourceFileCount);
            }
            finally
            {
                DeleteDirectory(source);
                DeleteDirectory(destination);
            }
        }

        private static string CreateTemporaryRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "OmniMixCacheMigrationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string WriteFile(string path, int length)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[length]);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
