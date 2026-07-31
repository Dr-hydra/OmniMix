using OmniMixPlayer.Backend.Audio.Dj;
using Xunit;

namespace OmniMixPlayer.Backend.Tests.Audio.Dj
{
    public sealed class Fh6DjSettingsTests
    {
        [Theory]
        [InlineData(null, "fh6-a", Fh6DjPlaybackScope.Fh6, true)]
        [InlineData(Fh6DjSettings.AllFh6InstancesScope, "fh6-a", Fh6DjPlaybackScope.Fh6, true)]
        [InlineData(Fh6DjSettings.Fh6InstancesScope, "desktop", Fh6DjPlaybackScope.Desktop, false)]
        [InlineData(Fh6DjSettings.DesktopInstancesScope, "desktop", Fh6DjPlaybackScope.Desktop, true)]
        [InlineData(Fh6DjSettings.DesktopInstancesScope, "fh6-a", Fh6DjPlaybackScope.Fh6, false)]
        [InlineData("fh6-a", "fh6-a", Fh6DjPlaybackScope.Fh6, true)]
        [InlineData("fh6-a", "fh6-b", Fh6DjPlaybackScope.Fh6, false)]
        public void ScopeIncludes_OnlyMatchesConfiguredPlaybackScope(
            string scope,
            string instanceId,
            Fh6DjPlaybackScope playbackScope,
            bool expected)
        {
            Assert.Equal(expected, Fh6DjSettings.ScopeIncludes(scope, instanceId, playbackScope));
        }

        [Theory]
        [InlineData(1, 1, true)]
        [InlineData(2, 1, true)]
        [InlineData(2, 2, false)]
        [InlineData(2, 3, true)]
        [InlineData(3, 4, true)]
        [InlineData(5, 5, false)]
        [InlineData(5, 6, true)]
        public void FrequencyPolicy_InsertsAtConfiguredIntervals(int frequency, int ordinal, bool expected)
        {
            Assert.Equal(expected, Fh6DjSettings.ShouldInsertAtOrdinal(frequency, ordinal));
        }

        [Theory]
        [InlineData(Fh6DjInsertionContent.Smart, Fh6DjClipKind.GeneralTransitionIn)]
        [InlineData(Fh6DjInsertionContent.Chatter, Fh6DjClipKind.IdleChatter)]
        [InlineData(Fh6DjInsertionContent.TransitionIn, Fh6DjClipKind.GeneralTransitionIn)]
        [InlineData(Fh6DjInsertionContent.TransitionOut, Fh6DjClipKind.GeneralTransitionOut)]
        public void ClipSelector_RespectsConfiguredContent(
            Fh6DjInsertionContent content,
            Fh6DjClipKind expectedKind)
        {
            var manifest = new Fh6DjCacheManifest
            {
                HostNumber = 1,
                SourceBankSha256 = "ABC",
                Clips =
                [
                    new Fh6DjPreparedClip { SubsongIndex = 1, SoundName = "in", Kind = Fh6DjClipKind.GeneralTransitionIn },
                    new Fh6DjPreparedClip { SubsongIndex = 2, SoundName = "chat", Kind = Fh6DjClipKind.IdleChatter },
                    new Fh6DjPreparedClip { SubsongIndex = 3, SoundName = "out", Kind = Fh6DjClipKind.GeneralTransitionOut }
                ]
            };

            var selected = Fh6DjClipSelector.Select(manifest, content, "track-uuid");

            Assert.Equal(expectedKind, selected.Kind);
        }
    }
}
