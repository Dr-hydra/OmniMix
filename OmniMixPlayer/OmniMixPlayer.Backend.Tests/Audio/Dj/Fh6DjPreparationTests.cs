using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmniMixPlayer.Backend.Audio.Dj;
using Xunit;

namespace OmniMixPlayer.Backend.Tests.Audio.Dj
{
    public sealed class Fh6DjPreparationTests
    {
        [Fact]
        public async Task PrepareAsync_ExtractsEligibleClipsAndThenHitsHashCache()
        {
            using var fixture = new Fh6DjCatalogTests.DjAssetFixture(sampleCount: 3);
            var extractor = new FakeExtractor();
            var service = new Fh6DjPreparationService(
                fixture.CachePath,
                extractor,
                metadataCatalog: new Fh6DjMetadataCatalog(
                    subsongResolver: new Fh6DjCatalogTests.TestSubsongResolver(
                        new System.Collections.Generic.Dictionary<string, int>
                        {
                            ["HZ6_VO_DJPulse_DJForteIENew1_1_IE_EN"] = 2,
                            ["HZ6_VO_DJPulse_DJMascot1_1_Mascot_EN"] = 1
                        })));

            var first = await service.PrepareAsync(fixture.GameRoot, hostNumber: 1);
            var second = await service.PrepareAsync(fixture.GameRoot, hostNumber: 1);

            Assert.False(first.WasAlreadyPrepared);
            Assert.True(second.WasAlreadyPrepared);
            Assert.Equal(2, extractor.CallCount);
            Assert.Equal(2, first.Manifest.Clips.Count);
            Assert.All(first.Manifest.Clips, clip =>
            {
                Assert.True(File.Exists(clip.FilePath));
                Assert.StartsWith(first.Identity.CacheDirectory, clip.FilePath, StringComparison.OrdinalIgnoreCase);
            });
            Assert.Contains(Path.Combine("FH6DJ", first.Identity.GameVersion, "host-01"),
                first.Identity.CacheDirectory,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(first.Identity.SourceBankSha256, first.Identity.CacheDirectory, StringComparison.OrdinalIgnoreCase);

            var manifestJson = await File.ReadAllTextAsync(
                Path.Combine(first.Identity.CacheDirectory, "manifest.json"));
            Assert.DoesNotContain("transcript", manifestJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fixture.BankPath, manifestJson, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RealGamePreparation_ExtractsMappedHost_WhenConfigured()
        {
            var gameRoot = Environment.GetEnvironmentVariable("OMNIMIX_FH6_TEST_ROOT");
            var vgmstreamPath = Environment.GetEnvironmentVariable(VgmstreamCliDjClipExtractor.PathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(vgmstreamPath) ||
                !Directory.Exists(gameRoot) || !File.Exists(vgmstreamPath))
            {
                return;
            }

            var cacheRoot = Path.Combine(Path.GetTempPath(), "omnimix-real-dj-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var service = new Fh6DjPreparationService(
                    cacheRoot,
                    new VgmstreamCliDjClipExtractor(vgmstreamPath));
                var result = await service.PrepareAsync(gameRoot, hostNumber: 1);

                Assert.NotEmpty(result.Manifest.Clips);
                Assert.All(result.Manifest.Clips, clip =>
                {
                    Assert.True(File.Exists(clip.FilePath));
                    Assert.True(new FileInfo(clip.FilePath).Length > 44);
                });
                Assert.Contains(result.Manifest.Clips, clip =>
                    clip.Kind == Fh6DjClipKind.GeneralTransitionIn || clip.Kind == Fh6DjClipKind.IdleChatter);
                Assert.Equal(result.Manifest.Clips.Count, result.Manifest.Clips.Select(clip => clip.SubsongIndex).Distinct().Count());
            }
            finally
            {
                if (Directory.Exists(cacheRoot))
                    Directory.Delete(cacheRoot, recursive: true);
            }
        }

        private sealed class FakeExtractor : IFh6DjClipExtractor
        {
            public string Identity => "test-extractor";
            public int CallCount { get; private set; }

            public Task ExtractAsync(
                string sourceBankPath,
                int subsongIndex,
                string outputWavePath,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                WritePcm16Wave(outputWavePath, sampleRate: 48_000, channels: 1, frames: 32);
                return Task.CompletedTask;
            }

            private static void WritePcm16Wave(string path, int sampleRate, short channels, int frames)
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
                var dataBytes = frames * channels * sizeof(short);
                writer.Write("RIFF"u8);
                writer.Write(36 + dataBytes);
                writer.Write("WAVE"u8);
                writer.Write("fmt "u8);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * sizeof(short));
                writer.Write((short)(channels * sizeof(short)));
                writer.Write((short)16);
                writer.Write("data"u8);
                writer.Write(dataBytes);
                for (var index = 0; index < frames * channels; index++)
                    writer.Write((short)1024);
            }
        }
    }
}
