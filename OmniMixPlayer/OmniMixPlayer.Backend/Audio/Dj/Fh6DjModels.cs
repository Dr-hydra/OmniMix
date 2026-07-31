using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    public static class Fh6DjConfigurationKeys
    {
        public const string Enabled = "fh6_dj_enabled";
        public const string Host = "fh6_dj_host";
        public const string GameRoot = "fh6_game_root";
        public const string Scope = "fh6_dj_scope";
        public const string Content = "fh6_dj_content";
        public const string Frequency = "fh6_dj_frequency";
    }

    public enum Fh6DjInsertionContent
    {
        Smart,
        Chatter,
        TransitionIn,
        TransitionOut
    }

    public enum Fh6DjPlaybackScope
    {
        Fh6,
        Desktop
    }

    public static class Fh6DjSettings
    {
        public const string Fh6InstancesScope = "fh6_instances";
        public const string DesktopInstancesScope = "desktop_instances";
        public const string AllFh6InstancesScope = "all_fh6_instances";

        public static string NormalizeScope(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope) ||
                string.Equals(scope, AllFh6InstancesScope, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scope, Fh6InstancesScope, StringComparison.OrdinalIgnoreCase))
                return Fh6InstancesScope;

            if (string.Equals(scope, DesktopInstancesScope, StringComparison.OrdinalIgnoreCase))
                return DesktopInstancesScope;

            return scope.Trim();
        }

        public static bool ScopeIncludes(
            string scope,
            string instanceId,
            Fh6DjPlaybackScope playbackScope)
        {
            scope = NormalizeScope(scope);
            if (string.Equals(scope, Fh6InstancesScope, StringComparison.OrdinalIgnoreCase))
                return playbackScope == Fh6DjPlaybackScope.Fh6;
            if (string.Equals(scope, DesktopInstancesScope, StringComparison.OrdinalIgnoreCase))
                return playbackScope == Fh6DjPlaybackScope.Desktop;
            return !string.IsNullOrWhiteSpace(instanceId) &&
                string.Equals(scope, instanceId, StringComparison.OrdinalIgnoreCase);
        }

        public static Fh6DjInsertionContent ParseContent(string content)
        {
            return content?.Trim().ToLowerInvariant() switch
            {
                "chatter" => Fh6DjInsertionContent.Chatter,
                "transition_in" => Fh6DjInsertionContent.TransitionIn,
                "transition_out" => Fh6DjInsertionContent.TransitionOut,
                _ => Fh6DjInsertionContent.Smart
            };
        }

        public static string ToConfigValue(Fh6DjInsertionContent content)
        {
            return content switch
            {
                Fh6DjInsertionContent.Chatter => "chatter",
                Fh6DjInsertionContent.TransitionIn => "transition_in",
                Fh6DjInsertionContent.TransitionOut => "transition_out",
                _ => "smart"
            };
        }

        public static int NormalizeFrequency(int frequency)
        {
            return frequency is 1 or 2 or 3 or 5 ? frequency : 1;
        }

        public static bool ShouldInsertAtOrdinal(int frequency, int trackOrdinal)
        {
            frequency = NormalizeFrequency(frequency);
            return trackOrdinal > 0 && (trackOrdinal - 1) % frequency == 0;
        }
    }

    public sealed record Fh6DjHost(
        int HostNumber,
        int DjCharacterId,
        string StationName,
        string CharacterName,
        string VoiceBankStem);

    public static class Fh6DjHosts
    {
        private static readonly Fh6DjHost[] Hosts =
        [
            new(1, 14, "Horizon Pulse", "DJPulse", "VO_DJ_01"),
            new(2, 15, "Horizon Bass Arena", "DJBassArena", "VO_DJ_02"),
            new(3, 16, "Horizon Block Party", "DJBlockParty", "VO_DJ_03"),
            new(4, 17, "Horizon XS", "DJXS", "VO_DJ_04"),
            new(5, 18, "Hospital Records", "DJHospital", "VO_DJ_05"),
            new(6, 19, "Gacha City Radio", "DJGachaCity", "VO_DJ_06"),
            new(7, 20, "Sub Pop Records", "DJSubPop", "VO_DJ_07"),
            new(8, 21, "Horizon Wave", "DJWave", "VO_DJ_08"),
            new(9, 22, "Horizon Opus", "DJClassical", "VO_DJ_09")
        ];

        public static IReadOnlyList<Fh6DjHost> All => Hosts;

        public static Fh6DjHost GetByHostNumber(int hostNumber)
        {
            if (hostNumber < 1 || hostNumber > Hosts.Length)
                throw new ArgumentOutOfRangeException(nameof(hostNumber), "FH6 DJ host must be between 1 and 9.");

            return Hosts[hostNumber - 1];
        }

        public static Fh6DjHost GetByCharacterId(int characterId)
        {
            foreach (var host in Hosts)
            {
                if (host.DjCharacterId == characterId)
                    return host;
            }

            throw new ArgumentOutOfRangeException(nameof(characterId), "FH6 DJ character must be between 14 and 22.");
        }
    }

    public enum Fh6DjClipKind
    {
        IdleChatter,
        GeneralTransitionIn,
        GeneralTransitionOut
    }

    public sealed record Fh6DjClipDefinition(
        int SubsongIndex,
        string SoundName,
        string GameEvent,
        uint FmodSubsoundId,
        long SampleLength,
        int SampleRate,
        Fh6DjClipKind Kind,
        string DeveloperTranscript);

    public sealed record Fh6DjMetadataSet(
        int RadioDjSampleCount,
        IReadOnlyList<Fh6DjClipDefinition> EligibleClips);

    public sealed record Fh6DjGameAssets(
        string GameRoot,
        string RuntimeRoot,
        string MediaAudioDirectory,
        string ExecutablePath,
        string GameVersion,
        string DialogueDjsPath,
        string RadioInfoPath,
        string SourceBankPath,
        Fh6DjHost Host);

    public sealed record Fh6Fsb5BankInfo(
        long Fsb5Offset,
        uint Version,
        uint SubsongCount,
        uint SampleHeaderBytes,
        uint NameTableBytes,
        uint AudioDataBytes,
        uint Mode);

    public sealed record Fh6DjSourceIdentity(
        string GameVersion,
        string SourceBankSha256,
        Fh6DjHost Host,
        string CacheDirectory);

    public sealed class Fh6DjPreparedClip
    {
        public int SubsongIndex { get; init; }
        public string SoundName { get; init; }
        public string GameEvent { get; init; }
        public Fh6DjClipKind Kind { get; init; }
        public long SampleLength { get; init; }
        public int SampleRate { get; init; }
        public string FileName { get; init; }
        [JsonIgnore]
        public string FilePath { get; set; }
    }

    public sealed class Fh6DjCacheManifest
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; init; } = CurrentSchemaVersion;
        public DateTimeOffset CreatedUtc { get; init; }
        public string GameVersion { get; init; }
        public int HostNumber { get; init; }
        public int DjCharacterId { get; init; }
        public string VoiceBankStem { get; init; }
        public string SourceBankSha256 { get; init; }
        public long SourceBankLength { get; init; }
        public string ExtractorIdentity { get; init; }
        public List<Fh6DjPreparedClip> Clips { get; init; } = [];
    }

    public sealed record Fh6DjPreparationResult(
        Fh6DjSourceIdentity Identity,
        Fh6DjCacheManifest Manifest,
        bool WasAlreadyPrepared);

    public sealed record Fh6DjPreparationProgress(
        int CompletedClips,
        int TotalClips,
        string CurrentGameEvent);
}
