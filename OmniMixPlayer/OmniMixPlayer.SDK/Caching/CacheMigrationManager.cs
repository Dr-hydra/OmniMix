using System;
using System.Collections.Generic;
using System.IO;

namespace OmniMixPlayer.SDK.Caching
{
    /// <summary>
    /// A durable directory-to-directory cache relocation operation. Both paths are
    /// absolute when persisted by the host.
    /// </summary>
    public sealed record CacheMigrationOperation(string SourceDirectory, string DestinationDirectory);

    /// <summary>
    /// Describes the best-effort result of relocating cache files. Source files are
    /// removed only after their destination is available.
    /// </summary>
    public sealed record CacheMigrationResult(
        string SourceDirectory,
        string DestinationDirectory,
        long ScannedBytes,
        long CopiedBytes,
        long DeletedSourceBytes,
        int ScannedFileCount,
        int CopiedFileCount,
        int DeletedSourceFileCount,
        int SkippedLockedFileCount,
        int FailedFileCount,
        int SkippedReparsePointCount,
        bool SourceHasRemainingEntries)
    {
        public int RemainingFileCount => Math.Max(0, ScannedFileCount - DeletedSourceFileCount);
    }

    /// <summary>
    /// Moves cache directory contents without following reparse points. It copies a
    /// file first and removes its source only after the target exists, so a failed
    /// relocation never invalidates a usable cache entry.
    /// </summary>
    public sealed class CacheMigrationManager
    {
        private readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        /// <summary>
        /// Relocates the contents of <paramref name="sourceDirectory"/> into
        /// <paramref name="destinationDirectory"/>. Existing target cache files
        /// are retained and allow the matching source file to be removed.
        /// </summary>
        public CacheMigrationResult MoveDirectoryContents(string sourceDirectory, string destinationDirectory)
        {
            var sourceRoot = NormalizeDirectory(sourceDirectory, nameof(sourceDirectory));
            var destinationRoot = NormalizeDirectory(destinationDirectory, nameof(destinationDirectory));

            if (IsSameOrNestedPath(sourceRoot, destinationRoot) ||
                IsSameOrNestedPath(destinationRoot, sourceRoot))
            {
                throw new ArgumentException("The source and destination cache directories must not overlap.");
            }

            if (!Directory.Exists(sourceRoot))
            {
                return new CacheMigrationResult(
                    sourceRoot,
                    destinationRoot,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    false);
            }

            long scannedBytes = 0;
            long copiedBytes = 0;
            long deletedSourceBytes = 0;
            var scannedFileCount = 0;
            var copiedFileCount = 0;
            var deletedSourceFileCount = 0;
            var skippedLockedFileCount = 0;
            var failedFileCount = 0;
            var skippedReparsePointCount = 0;

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(sourceRoot);
            while (pendingDirectories.Count > 0)
            {
                var directory = pendingDirectories.Pop();
                string[] files;
                string[] directories;
                try
                {
                    files = Directory.GetFiles(directory);
                    directories = Directory.GetDirectories(directory);
                }
                catch (IOException)
                {
                    failedFileCount++;
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    failedFileCount++;
                    continue;
                }

                foreach (var file in files)
                {
                    FileAttributes attributes;
                    FileInfo info;
                    try
                    {
                        attributes = File.GetAttributes(file);
                        info = new FileInfo(file);
                    }
                    catch (IOException)
                    {
                        failedFileCount++;
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        failedFileCount++;
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedReparsePointCount++;
                        continue;
                    }

                    scannedFileCount++;
                    scannedBytes += info.Length;

                    string destinationPath;
                    try
                    {
                        var relativePath = Path.GetRelativePath(sourceRoot, file);
                        if (!IsSafeRelativePath(relativePath))
                        {
                            failedFileCount++;
                            continue;
                        }

                        destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
                        if (!IsSameOrNestedPath(destinationPath, destinationRoot))
                        {
                            failedFileCount++;
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException ||
                                               ex is NotSupportedException ||
                                               ex is PathTooLongException)
                    {
                        failedFileCount++;
                        continue;
                    }

                    var outcome = MoveFile(
                        file,
                        destinationPath,
                        info.Length,
                        info.LastWriteTimeUtc,
                        info.LastAccessTimeUtc);
                    if (outcome.Copied)
                    {
                        copiedFileCount++;
                        copiedBytes += info.Length;
                    }

                    switch (outcome.SourceDeletion)
                    {
                        case SourceDeletion.Deleted:
                        case SourceDeletion.AlreadyMissing:
                            deletedSourceFileCount++;
                            deletedSourceBytes += info.Length;
                            break;
                        case SourceDeletion.Locked:
                            skippedLockedFileCount++;
                            break;
                        default:
                            failedFileCount++;
                            break;
                    }
                }

                foreach (var childDirectory in directories)
                {
                    try
                    {
                        if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                        {
                            skippedReparsePointCount++;
                            continue;
                        }

                        pendingDirectories.Push(childDirectory);
                    }
                    catch (IOException)
                    {
                        failedFileCount++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        failedFileCount++;
                    }
                }
            }

            RemoveEmptyDirectories(sourceRoot);
            return new CacheMigrationResult(
                sourceRoot,
                destinationRoot,
                scannedBytes,
                copiedBytes,
                deletedSourceBytes,
                scannedFileCount,
                copiedFileCount,
                deletedSourceFileCount,
                skippedLockedFileCount,
                failedFileCount,
                skippedReparsePointCount,
                HasRemainingEntries(sourceRoot));
        }

        private FileMoveOutcome MoveFile(
            string sourcePath,
            string destinationPath,
            long sourceLength,
            DateTime lastWriteTimeUtc,
            DateTime lastAccessTimeUtc)
        {
            FileStream sourceStream;
            try
            {
                sourceStream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    bufferSize: 1024 * 64,
                    FileOptions.SequentialScan);
            }
            catch (FileNotFoundException)
            {
                return new FileMoveOutcome(false, SourceDeletion.AlreadyMissing);
            }
            catch (DirectoryNotFoundException)
            {
                return new FileMoveOutcome(false, SourceDeletion.AlreadyMissing);
            }
            catch (IOException)
            {
                return new FileMoveOutcome(false, SourceDeletion.Locked);
            }
            catch (UnauthorizedAccessException)
            {
                return new FileMoveOutcome(false, SourceDeletion.Failed);
            }

            var copied = false;
            try
            {
                using (sourceStream)
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (string.IsNullOrWhiteSpace(destinationDirectory))
                        return new FileMoveOutcome(false, SourceDeletion.Failed);

                    Directory.CreateDirectory(destinationDirectory);
                    var replaceDestination = false;
                    if (File.Exists(destinationPath))
                    {
                        var destinationInfo = new FileInfo(destinationPath);
                        if (destinationInfo.LastWriteTimeUtc >= lastWriteTimeUtc)
                        {
                            // Cache names are stable keys. When the destination was updated at
                            // least as recently, it is the authoritative entry and the old copy
                            // can be reclaimed without replacing it.
                            return new FileMoveOutcome(false, TryDeleteSourceFile(sourcePath, sourceLength));
                        }

                        replaceDestination = true;
                    }

                    var temporaryPath = destinationPath + ".omnimix-migration-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var destinationStream = new FileStream(
                                   temporaryPath,
                                   FileMode.CreateNew,
                                   FileAccess.Write,
                                   FileShare.None,
                                   bufferSize: 1024 * 64,
                                   FileOptions.SequentialScan))
                        {
                            sourceStream.CopyTo(destinationStream);
                            destinationStream.Flush(flushToDisk: true);
                        }

                        try
                        {
                            File.Move(temporaryPath, destinationPath, overwrite: replaceDestination);
                            copied = true;
                            TryRestoreTimestamps(destinationPath, lastWriteTimeUtc, lastAccessTimeUtc);
                        }
                        catch (IOException) when (File.Exists(destinationPath))
                        {
                            // A concurrent cache writer won the race. Its entry is usable,
                            // so the source can still be reclaimed.
                        }
                    }
                    finally
                    {
                        TryDeleteTemporaryFile(temporaryPath);
                    }
                }
            }
            catch (IOException)
            {
                return new FileMoveOutcome(false, SourceDeletion.Failed);
            }
            catch (UnauthorizedAccessException)
            {
                return new FileMoveOutcome(false, SourceDeletion.Failed);
            }

            return new FileMoveOutcome(copied, TryDeleteSourceFile(sourcePath, sourceLength));
        }

        private static void TryRestoreTimestamps(string path, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc)
        {
            try
            {
                File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
                File.SetLastAccessTimeUtc(path, lastAccessTimeUtc);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static SourceDeletion TryDeleteSourceFile(string path, long sourceLength)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                }
            }
            catch (FileNotFoundException)
            {
                return SourceDeletion.AlreadyMissing;
            }
            catch (DirectoryNotFoundException)
            {
                return SourceDeletion.AlreadyMissing;
            }
            catch (IOException)
            {
                return SourceDeletion.Locked;
            }
            catch (UnauthorizedAccessException)
            {
                return SourceDeletion.Failed;
            }

            try
            {
                File.Delete(path);
                return SourceDeletion.Deleted;
            }
            catch (FileNotFoundException)
            {
                return SourceDeletion.AlreadyMissing;
            }
            catch (DirectoryNotFoundException)
            {
                return SourceDeletion.AlreadyMissing;
            }
            catch (IOException)
            {
                return SourceDeletion.Locked;
            }
            catch (UnauthorizedAccessException)
            {
                return SourceDeletion.Failed;
            }
        }

        private void RemoveEmptyDirectories(string rootDirectory)
        {
            if (!Directory.Exists(rootDirectory))
                return;

            var directories = new List<string>();
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(rootDirectory);
            while (pendingDirectories.Count > 0)
            {
                string[] children;
                try
                {
                    children = Directory.GetDirectories(pendingDirectories.Pop());
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var childDirectory in children)
                {
                    try
                    {
                        if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                            continue;

                        directories.Add(childDirectory);
                        pendingDirectories.Push(childDirectory);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            directories.Sort((left, right) => right.Length.CompareTo(left.Length));
            foreach (var directory in directories)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).GetEnumerator().MoveNext())
                        Directory.Delete(directory);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static bool HasRemainingEntries(string rootDirectory)
        {
            if (!Directory.Exists(rootDirectory))
                return false;

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(rootDirectory);
            while (pendingDirectories.Count > 0)
            {
                string[] files;
                string[] directories;
                try
                {
                    var directory = pendingDirectories.Pop();
                    files = Directory.GetFiles(directory);
                    directories = Directory.GetDirectories(directory);
                }
                catch (IOException)
                {
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }

                if (files.Length > 0)
                    return true;

                foreach (var childDirectory in directories)
                {
                    try
                    {
                        if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                            return true;

                        pendingDirectories.Push(childDirectory);
                    }
                    catch (IOException)
                    {
                        return true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeDirectory(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A cache directory is required.", parameterName);

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        private bool IsSameOrNestedPath(string candidatePath, string parentPath)
        {
            if (string.Equals(candidatePath, parentPath, _pathComparison))
                return true;

            var parentWithSeparator = Path.EndsInDirectorySeparator(parentPath)
                ? parentPath
                : parentPath + Path.DirectorySeparatorChar;
            return candidatePath.StartsWith(parentWithSeparator, _pathComparison);
        }

        private static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                return false;

            return path != ".." &&
                   !path.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                   !path.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }

        private readonly record struct FileMoveOutcome(bool Copied, SourceDeletion SourceDeletion);

        private enum SourceDeletion
        {
            Deleted,
            AlreadyMissing,
            Locked,
            Failed
        }
    }
}
