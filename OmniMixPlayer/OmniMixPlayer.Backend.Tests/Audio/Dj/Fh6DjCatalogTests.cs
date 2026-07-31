using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OmniMixPlayer.Backend.Audio.Dj;
using Xunit;

namespace OmniMixPlayer.Backend.Tests.Audio.Dj
{
    public sealed class Fh6DjCatalogTests
    {
        [Fact]
        public void Hosts_MapOneThroughNineToCharactersAndBanks()
        {
            Assert.Equal(9, Fh6DjHosts.All.Count);
            for (var index = 0; index < Fh6DjHosts.All.Count; index++)
            {
                var host = Fh6DjHosts.All[index];
                Assert.Equal(index + 1, host.HostNumber);
                Assert.Equal(index + 14, host.DjCharacterId);
                Assert.Equal($"VO_DJ_{index + 1:00}", host.VoiceBankStem);
                Assert.Same(host, Fh6DjHosts.GetByHostNumber(index + 1));
                Assert.Same(host, Fh6DjHosts.GetByCharacterId(index + 14));
            }
        }

        [Fact]
        public void MetadataCatalog_UsesSoundTableMappingAndRejectsUnknownEvents()
        {
            using var fixture = new DjAssetFixture(sampleCount: 3);
            var assets = fixture.CreateAssets();

            var metadata = new Fh6DjMetadataCatalog(
                subsongResolver: new TestSubsongResolver(new Dictionary<string, int>
                {
                    ["HZ6_VO_DJPulse_DJForteIENew1_1_IE_EN"] = 2,
                    ["HZ6_VO_DJPulse_DJMascot1_1_Mascot_EN"] = 1
                })).Load(assets);

            Assert.Equal(3, metadata.RadioDjSampleCount);
            Assert.Equal(2, metadata.EligibleClips.Count);
            Assert.Collection(metadata.EligibleClips,
                clip =>
                {
                    Assert.Equal(2, clip.SubsongIndex);
                    Assert.Equal("DJForteIENew1", clip.GameEvent);
                    Assert.Equal(Fh6DjClipKind.GeneralTransitionIn, clip.Kind);
                    Assert.Equal((uint)101, clip.FmodSubsoundId);
                    Assert.Equal("First internal transcript. Here's a track.", clip.DeveloperTranscript);
                },
                clip =>
                {
                    Assert.Equal(1, clip.SubsongIndex);
                    Assert.Equal("DJMascot1", clip.GameEvent);
                    Assert.Equal(Fh6DjClipKind.IdleChatter, clip.Kind);
                });
        }

        [Fact]
        public void FsbInspector_ReadsEmbeddedFsbHeader()
        {
            using var fixture = new DjAssetFixture(sampleCount: 3);

            var info = Fh6Fsb5BankInspector.Inspect(fixture.BankPath);

            Assert.Equal((uint)3, info.SubsongCount);
            Assert.Equal((uint)15, info.Mode);
            Assert.Equal((uint)4096, info.AudioDataBytes);
        }

        [Fact]
        public void RealGameMetadata_AllHostsHaveConservativeChatter_WhenFixtureIsConfigured()
        {
            var gameRoot = Environment.GetEnvironmentVariable("OMNIMIX_FH6_TEST_ROOT");
            if (string.IsNullOrWhiteSpace(gameRoot))
                return;

            var locator = new Fh6DjGameAssetLocator();
            var catalog = new Fh6DjMetadataCatalog();
            foreach (var host in Fh6DjHosts.All)
            {
                var metadata = catalog.Load(locator.Locate(gameRoot, host.HostNumber));
                Assert.NotEmpty(metadata.EligibleClips);
                Assert.DoesNotContain(metadata.EligibleClips,
                    clip => clip.GameEvent.Contains("SkillSong", StringComparison.OrdinalIgnoreCase));
                Assert.All(metadata.EligibleClips,
                    clip => Assert.True(
                        clip.Kind == Fh6DjClipKind.IdleChatter ||
                        clip.Kind == Fh6DjClipKind.GeneralTransitionIn ||
                        clip.Kind == Fh6DjClipKind.GeneralTransitionOut));
            }
        }

        [Fact]
        public void ClipSelector_IsStableAndCanAvoidImmediateRepeat()
        {
            var manifest = new Fh6DjCacheManifest
            {
                HostNumber = 1,
                SourceBankSha256 = "ABC",
                Clips =
                [
                    new Fh6DjPreparedClip
                    {
                        SubsongIndex = 1,
                        SoundName = "one",
                        Kind = Fh6DjClipKind.GeneralTransitionIn
                    },
                    new Fh6DjPreparedClip
                    {
                        SubsongIndex = 2,
                        SoundName = "two",
                        Kind = Fh6DjClipKind.GeneralTransitionIn
                    }
                ]
            };

            var first = Fh6DjClipSelector.Select(
                manifest,
                Fh6DjClipKind.GeneralTransitionIn,
                "track-uuid");
            var repeated = Fh6DjClipSelector.Select(
                manifest,
                Fh6DjClipKind.GeneralTransitionIn,
                "track-uuid");
            var avoided = Fh6DjClipSelector.Select(
                manifest,
                Fh6DjClipKind.GeneralTransitionIn,
                "track-uuid",
                first.SoundName);

            Assert.Same(first, repeated);
            Assert.NotEqual(first.SoundName, avoided.SoundName);
        }

        internal sealed class DjAssetFixture : IDisposable
        {
            private readonly string _root;
            private readonly string _audioDirectory;

            public string BankPath { get; }
            public string CachePath => Path.Combine(_root, "cache");
            public string GameRoot => _root;

            public DjAssetFixture(int sampleCount)
            {
                _root = Path.Combine(Path.GetTempPath(), "omnimix-dj-test-" + Guid.NewGuid().ToString("N"));
                _audioDirectory = Path.Combine(_root, "media", "Audio");
                var bankDirectory = Path.Combine(_audioDirectory, "FMODBanks");
                Directory.CreateDirectory(bankDirectory);
                File.WriteAllBytes(Path.Combine(_root, "forzahorizon6.exe"), "MZ"u8.ToArray());

                File.WriteAllText(Path.Combine(_audioDirectory, "RadioInfo_EN.xml"), RadioXml, Encoding.UTF8);
                File.WriteAllText(Path.Combine(_audioDirectory, "Dialogue_DJs.xml"), DialogueXml, Encoding.UTF8);
                BankPath = Path.Combine(bankDirectory, "VO_DJ_01_EN.assets.bank");
                WriteFsbBank(BankPath, sampleCount);
            }

            public Fh6DjGameAssets CreateAssets()
            {
                return new Fh6DjGameAssets(
                    _root,
                    _root,
                    _audioDirectory,
                    Path.Combine(_root, "forzahorizon6.exe"),
                    "test-version",
                    Path.Combine(_audioDirectory, "Dialogue_DJs.xml"),
                    Path.Combine(_audioDirectory, "RadioInfo_EN.xml"),
                    BankPath,
                    Fh6DjHosts.GetByHostNumber(1));
            }

            public void Dispose()
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }

            private static void WriteFsbBank(string path, int sampleCount)
            {
                var data = new byte[256];
                "RIFF"u8.CopyTo(data);
                var offset = 64;
                "FSB5"u8.CopyTo(data.AsSpan(offset));
                BitConverter.GetBytes((uint)1).CopyTo(data, offset + 4);
                BitConverter.GetBytes((uint)sampleCount).CopyTo(data, offset + 8);
                BitConverter.GetBytes((uint)(sampleCount * 8)).CopyTo(data, offset + 12);
                BitConverter.GetBytes((uint)0).CopyTo(data, offset + 16);
                BitConverter.GetBytes((uint)4096).CopyTo(data, offset + 20);
                BitConverter.GetBytes((uint)15).CopyTo(data, offset + 24);
                File.WriteAllBytes(path, data);
            }

            private const string RadioXml = """
                <?xml version="1.0" encoding="utf-8"?>
                <Radio><RadioStations>
                  <RadioStation Name="Horizon Pulse" DJCharID="14">
                    <Banks><Bank Name="VO_DJ_01_EN" /></Banks>
                    <SampleList Type="DJ" Event="/Master/Radio/DJ">
                      <Sample SoundName="HZ6_VO_DJPulse_DJForteIENew1_1_IE_EN" SampleLength="48000" SampleRate="48000" GameEvent="DJForteIENew1" />
                      <Sample SoundName="HZ6_VO_DJPulse_DJCampaignDiscover1_1_Campaign_EN" SampleLength="48000" SampleRate="48000" GameEvent="DJCampaignDiscover1" />
                      <Sample SoundName="HZ6_VO_DJPulse_DJMascot1_1_Mascot_EN" SampleLength="48000" SampleRate="48000" GameEvent="DJMascot1" />
                    </SampleList>
                  </RadioStation>
                </RadioStations></Radio>
                """;

            private const string DialogueXml = """
                <?xml version="1.0" encoding="utf-8"?>
                <DialogueScript>
                  <Trigger id="DJForteIENew1">
                    <!--
                    Character : "DJPulse" (#14)
                    WAV File  : "HZ6_VO_DJPulse_DJForteIENew1_1_IE.wav"
                    Subtitle  : "First internal transcript. Here's a track." -->
                    <Event name="Clean_Ducking/HZ6_VO_DJPulse_DJForteIENew1_1_IE" sub="101" char="14" />
                  </Trigger>
                  <Trigger id="DJCampaignDiscover1">
                    <!-- Subtitle  : "Must be rejected" -->
                    <Event name="Clean_Ducking/HZ6_VO_DJPulse_DJCampaignDiscover1_1_Campaign" sub="102" char="14" />
                  </Trigger>
                  <Trigger id="DJMascot1">
                    <!-- Subtitle  : "Last internal transcript" -->
                    <Event name="Clean_Ducking/HZ6_VO_DJPulse_DJMascot1_1_Mascot" sub="103" char="14" />
                  </Trigger>
                </DialogueScript>
                """;
        }

        internal sealed class TestSubsongResolver : IFh6DjSubsongResolver
        {
            private readonly IReadOnlyDictionary<string, int> _indices;

            public TestSubsongResolver(IReadOnlyDictionary<string, int> indices)
            {
                _indices = indices;
            }

            public int ResolveSubsongIndex(string sourceBankPath, string soundName)
            {
                return _indices.TryGetValue(soundName, out var index)
                    ? index
                    : throw new InvalidDataException($"No test subsong mapping exists for {soundName}.");
            }
        }
    }
}
