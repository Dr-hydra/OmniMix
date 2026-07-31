using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    public sealed class Fh6DjPreparationService
    {
        private const string ManifestFileName = "manifest.json";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> PreparationLocks =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions ManifestJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _cacheRoot;
        private readonly IFh6DjClipExtractor _extractor;
        private readonly Fh6DjGameAssetLocator _assetLocator;
        private readonly Fh6DjMetadataCatalog _metadataCatalog;

        public Fh6DjPreparationService(
            string cacheRoot,
            IFh6DjClipExtractor extractor,
            Fh6DjGameAssetLocator assetLocator = null,
            Fh6DjMetadataCatalog metadataCatalog = null)
        {
            if (string.IsNullOrWhiteSpace(cacheRoot))
                throw new ArgumentException("A cache root is required.", nameof(cacheRoot));

            _cacheRoot = Path.GetFullPath(cacheRoot);
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _assetLocator = assetLocator ?? new Fh6DjGameAssetLocator();
            _metadataCatalog = metadataCatalog ?? new Fh6DjMetadataCatalog();
        }

        /// <summary>
        /// Prepares one selected host in the background. This method hashes and
        /// extracts files and must never be called from a real-time audio callback.
        /// </summary>
        public async Task<Fh6DjPreparationResult> PrepareAsync(
            string gameRoot,
            int hostNumber,
            IProgress<Fh6DjPreparationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var assets = _assetLocator.Locate(gameRoot, hostNumber);
            var bankInfo = Fh6Fsb5BankInspector.Inspect(assets.SourceBankPath);
            var metadata = _metadataCatalog.Load(assets);
            if (bankInfo.SubsongCount != metadata.RadioDjSampleCount)
            {
                throw new InvalidDataException(
                    $"The bank has {bankInfo.SubsongCount} subsounds but RadioInfo defines " +
                    $"{metadata.RadioDjSampleCount} DJ samples. Extraction order is not safe.");
            }

            var sourceHash = await Fh6DjGameAssetLocator
                .ComputeSha256Async(assets.SourceBankPath, cancellationToken)
                .ConfigureAwait(false);
            var cacheDirectory = BuildCacheDirectory(assets.GameVersion, assets.Host, sourceHash);
            var identity = new Fh6DjSourceIdentity(
                assets.GameVersion,
                sourceHash,
                assets.Host,
                cacheDirectory);

            var preparationLock = PreparationLocks.GetOrAdd(
                cacheDirectory,
                _ => new SemaphoreSlim(1, 1));
            await preparationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var cached = await TryReadValidManifestAsync(identity, cancellationToken).ConfigureAwait(false);
                if (cached != null)
                    return new Fh6DjPreparationResult(identity, cached, WasAlreadyPrepared: true);

                return await ExtractToCacheAsync(
                        assets,
                        identity,
                        metadata.EligibleClips,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                preparationLock.Release();
            }
        }

        private async Task<Fh6DjPreparationResult> ExtractToCacheAsync(
            Fh6DjGameAssets assets,
            Fh6DjSourceIdentity identity,
            IReadOnlyList<Fh6DjClipDefinition> clips,
            IProgress<Fh6DjPreparationProgress> progress,
            CancellationToken cancellationToken)
        {
            var parentDirectory = Path.GetDirectoryName(identity.CacheDirectory)
                ?? throw new InvalidOperationException("The DJ cache directory has no parent.");
            Directory.CreateDirectory(parentDirectory);

            // Extraction tools may not be long-path aware. Stage near the category
            // root and atomically move the completed directory under the hash key.
            var stagingDirectory = Path.Combine(
                _cacheRoot,
                "FH6DJ",
                ".work",
                Guid.NewGuid().ToString("N")[..12]);
            EnsureOwnedCachePath(stagingDirectory);
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                var prepared = new List<Fh6DjPreparedClip>(clips.Count);
                for (var index = 0; index < clips.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var clip = clips[index];
                    progress?.Report(new Fh6DjPreparationProgress(index, clips.Count, clip.GameEvent));

                    var fileName = BuildClipFileName(clip);
                    var outputPath = Path.Combine(stagingDirectory, fileName);
                    await _extractor.ExtractAsync(
                            assets.SourceBankPath,
                            clip.SubsongIndex,
                            outputPath,
                            cancellationToken)
                        .ConfigureAwait(false);

                    prepared.Add(new Fh6DjPreparedClip
                    {
                        SubsongIndex = clip.SubsongIndex,
                        SoundName = clip.SoundName,
                        GameEvent = clip.GameEvent,
                        Kind = clip.Kind,
                        SampleLength = clip.SampleLength,
                        SampleRate = clip.SampleRate,
                        FileName = fileName,
                        FilePath = outputPath
                    });
                }

                var manifest = new Fh6DjCacheManifest
                {
                    CreatedUtc = DateTimeOffset.UtcNow,
                    GameVersion = identity.GameVersion,
                    HostNumber = identity.Host.HostNumber,
                    DjCharacterId = identity.Host.DjCharacterId,
                    VoiceBankStem = identity.Host.VoiceBankStem,
                    SourceBankSha256 = identity.SourceBankSha256,
                    SourceBankLength = new FileInfo(assets.SourceBankPath).Length,
                    ExtractorIdentity = _extractor.Identity,
                    Clips = prepared
                };

                await WriteManifestAsync(stagingDirectory, manifest, cancellationToken).ConfigureAwait(false);

                if (Directory.Exists(identity.CacheDirectory))
                {
                    EnsureOwnedCachePath(identity.CacheDirectory);
                    Directory.Delete(identity.CacheDirectory, recursive: true);
                }

                Directory.Move(stagingDirectory, identity.CacheDirectory);
                HydratePaths(identity.CacheDirectory, manifest);
                progress?.Report(new Fh6DjPreparationProgress(clips.Count, clips.Count, string.Empty));
                return new Fh6DjPreparationResult(identity, manifest, WasAlreadyPrepared: false);
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                {
                    EnsureOwnedCachePath(stagingDirectory);
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
        }

        private async Task<Fh6DjCacheManifest> TryReadValidManifestAsync(
            Fh6DjSourceIdentity identity,
            CancellationToken cancellationToken)
        {
            var manifestPath = Path.Combine(identity.CacheDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                await using var stream = new FileStream(
                    manifestPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var manifest = await JsonSerializer
                    .DeserializeAsync<Fh6DjCacheManifest>(stream, ManifestJsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (manifest == null ||
                    manifest.SchemaVersion != Fh6DjCacheManifest.CurrentSchemaVersion ||
                    manifest.HostNumber != identity.Host.HostNumber ||
                    manifest.DjCharacterId != identity.Host.DjCharacterId ||
                    !string.Equals(manifest.GameVersion, identity.GameVersion, StringComparison.Ordinal) ||
                    !string.Equals(manifest.SourceBankSha256, identity.SourceBankSha256, StringComparison.OrdinalIgnoreCase) ||
                    manifest.Clips.Count == 0)
                    return null;

                foreach (var clip in manifest.Clips)
                {
                    if (!IsSimpleFileName(clip.FileName))
                        return null;
                    var path = Path.Combine(identity.CacheDirectory, clip.FileName);
                    if (!File.Exists(path) || new FileInfo(path).Length < 44)
                        return null;
                }

                HydratePaths(identity.CacheDirectory, manifest);
                return manifest;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static async Task WriteManifestAsync(
            string directory,
            Fh6DjCacheManifest manifest,
            CancellationToken cancellationToken)
        {
            var path = Path.Combine(directory, ManifestFileName);
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await JsonSerializer.SerializeAsync(stream, manifest, ManifestJsonOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private string BuildCacheDirectory(string gameVersion, Fh6DjHost host, string sourceHash)
        {
            var result = Path.Combine(
                _cacheRoot,
                "FH6DJ",
                SanitizePathSegment(gameVersion),
                $"host-{host.HostNumber:00}",
                sourceHash.ToUpperInvariant());
            EnsureOwnedCachePath(result);
            return result;
        }

        private void EnsureOwnedCachePath(string path)
        {
            var fullRoot = _cacheRoot
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The DJ cache path escaped the configured cache root.");
        }

        private static void HydratePaths(string directory, Fh6DjCacheManifest manifest)
        {
            foreach (var clip in manifest.Clips)
                clip.FilePath = Path.Combine(directory, clip.FileName);
        }

        private static string BuildClipFileName(Fh6DjClipDefinition clip)
        {
            return $"{clip.SubsongIndex:0000}-{SanitizePathSegment(clip.GameEvent)}-{clip.Kind}.wav";
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var invalid = Path.GetInvalidFileNameChars();
            var characters = value
                .Trim()
                .Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray();
            var result = new string(characters).Trim('.', ' ');
            return string.IsNullOrEmpty(result) ? "unknown" : result;
        }

        private static bool IsSimpleFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) &&
                string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) &&
                fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        }
    }
}
