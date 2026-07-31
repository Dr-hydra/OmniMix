using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    /// <summary>
    /// Explicit allow-list for lines whose meaning is independent of a particular
    /// licensed track or campaign checkpoint. Unknown events are denied by default.
    /// </summary>
    public sealed class Fh6DjClipPolicy
    {
        private static readonly HashSet<string> ConservativeEvents = BuildConservativeEvents();

        public static Fh6DjClipPolicy Conservative { get; } = new();

        public bool IsEligibleEvent(string gameEvent) =>
            !string.IsNullOrWhiteSpace(gameEvent) && ConservativeEvents.Contains(gameEvent);

        public bool TryClassify(string gameEvent, string developerTranscript, out Fh6DjClipKind kind)
        {
            if (!IsEligibleEvent(gameEvent))
            {
                kind = default;
                return false;
            }

            var text = developerTranscript ?? string.Empty;
            if (ContainsAny(text,
                    "that was", "you just heard", "the last track", "the last song"))
            {
                kind = Fh6DjClipKind.GeneralTransitionOut;
            }
            else if (ContainsAny(text,
                         "here's a", "here is a", "this next", "next track", "next song",
                         "next one", "let's hear", "let us hear", "back to the music", "play you"))
            {
                kind = Fh6DjClipKind.GeneralTransitionIn;
            }
            else
            {
                kind = Fh6DjClipKind.IdleChatter;
            }

            return true;
        }

        private static bool ContainsAny(string text, params string[] phrases)
        {
            return phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        }

        private static HashSet<string> BuildConservativeEvents()
        {
            var result = new HashSet<string>(StringComparer.Ordinal)
            {
                "DJRadioIntro",
                "DJMuralsChat",
                "DJMascotFirst",
                "DJForteIE13"
            };
            for (var index = 1; index <= 4; index++) result.Add($"DJAmbassador{index}");
            for (var index = 1; index <= 6; index++) result.Add($"DJForteIENew{index}");
            for (var index = 1; index <= 9; index++) result.Add($"DJRegion{index}");
            for (var index = 1; index <= 10; index++) result.Add($"DJMascot{index}");
            return result;
        }
    }

    public sealed class Fh6DjMetadataCatalog
    {
        private readonly Fh6DjClipPolicy _policy;
        private readonly IFh6DjSubsongResolver _subsongResolver;

        public Fh6DjMetadataCatalog(
            Fh6DjClipPolicy policy = null,
            IFh6DjSubsongResolver subsongResolver = null)
        {
            _policy = policy ?? Fh6DjClipPolicy.Conservative;
            _subsongResolver = subsongResolver ?? new Fh6FmodSoundTableSubsongResolver();
        }

        public Fh6DjMetadataSet Load(Fh6DjGameAssets assets)
        {
            ArgumentNullException.ThrowIfNull(assets);

            var dialogue = LoadDialogueEvents(assets.DialogueDjsPath, assets.Host.DjCharacterId);
            var radioDocument = XDocument.Load(assets.RadioInfoPath, LoadOptions.None);
            var station = radioDocument
                .Descendants("RadioStation")
                .SingleOrDefault(element =>
                    string.Equals((string)element.Attribute("DJCharID"),
                        assets.Host.DjCharacterId.ToString(CultureInfo.InvariantCulture),
                        StringComparison.Ordinal));

            if (station == null)
                throw new InvalidDataException(
                    $"Radio metadata does not define DJCharID {assets.Host.DjCharacterId}.");

            var djList = station
                .Elements("SampleList")
                .SingleOrDefault(element =>
                    string.Equals((string)element.Attribute("Type"), "DJ", StringComparison.Ordinal));
            if (djList == null)
                throw new InvalidDataException(
                    $"Radio metadata does not define the DJ sample list for {assets.Host.StationName}.");

            var samples = djList.Elements("Sample").ToArray();
            var eligible = new List<Fh6DjClipDefinition>();

            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                var gameEvent = RequiredAttribute(sample, "GameEvent");
                if (!_policy.IsEligibleEvent(gameEvent))
                    continue;
                var soundName = RequiredAttribute(sample, "SoundName");
                var eventInfo = FindDialogueEvent(dialogue, gameEvent, soundName);
                if (!_policy.TryClassify(gameEvent, eventInfo.DeveloperTranscript, out var kind))
                    continue;
                eligible.Add(new Fh6DjClipDefinition(
                    _subsongResolver.ResolveSubsongIndex(assets.SourceBankPath, soundName),
                    soundName,
                    gameEvent,
                    eventInfo.FmodSubsoundId,
                    ParseInt64(sample, "SampleLength"),
                    checked((int)ParseInt64(sample, "SampleRate")),
                    kind,
                    eventInfo.DeveloperTranscript));
            }

            if (eligible.Count == 0)
                throw new InvalidDataException(
                    $"No conservatively classified DJ lines were found for {assets.Host.StationName}.");

            return new Fh6DjMetadataSet(samples.Length, eligible);
        }

        private static Dictionary<string, List<DialogueEvent>> LoadDialogueEvents(
            string dialoguePath,
            int characterId)
        {
            var document = XDocument.Load(dialoguePath, LoadOptions.PreserveWhitespace);
            var result = new Dictionary<string, List<DialogueEvent>>(StringComparer.Ordinal);

            foreach (var trigger in document.Descendants("Trigger"))
            {
                var gameEvent = (string)trigger.Attribute("id");
                if (string.IsNullOrWhiteSpace(gameEvent))
                    continue;

                foreach (var eventElement in trigger.Elements("Event"))
                {
                    if (!int.TryParse((string)eventElement.Attribute("char"),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var eventCharacterId) || eventCharacterId != characterId)
                        continue;

                    var fullName = RequiredAttribute(eventElement, "name");
                    var leafName = fullName[(fullName.LastIndexOf('/') + 1)..];
                    if (!uint.TryParse((string)eventElement.Attribute("sub"),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var subsoundId))
                        throw new InvalidDataException($"Dialogue event {fullName} has an invalid FMOD subsound id.");

                    var comment = eventElement
                        .NodesBeforeSelf()
                        .OfType<XComment>()
                        .LastOrDefault();
                    var transcript = ReadDeveloperTranscript(comment?.Value);

                    if (!result.TryGetValue(gameEvent, out var events))
                    {
                        events = [];
                        result.Add(gameEvent, events);
                    }

                    events.Add(new DialogueEvent(leafName, subsoundId, transcript));
                }
            }

            return result;
        }

        private static DialogueEvent FindDialogueEvent(
            IReadOnlyDictionary<string, List<DialogueEvent>> dialogue,
            string gameEvent,
            string localizedSoundName)
        {
            if (!dialogue.TryGetValue(gameEvent, out var events))
                throw new InvalidDataException($"Dialogue_DJs.xml does not define {gameEvent}.");

            var matches = events
                .Where(item => localizedSoundName.Equals(item.SoundName, StringComparison.Ordinal) ||
                    localizedSoundName.StartsWith(item.SoundName + "_", StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(
                    $"Could not uniquely map radio sample {localizedSoundName} to Dialogue_DJs.xml.");

            return matches[0];
        }

        private static string ReadDeveloperTranscript(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment))
                return string.Empty;

            const string marker = "Subtitle  : \"";
            var start = comment.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            start += marker.Length;
            var end = comment.LastIndexOf('"');
            if (end <= start)
                return string.Empty;

            // Authoring/debug metadata only. It is deliberately not copied to the
            // prepared-cache manifest and is never exposed as a runtime subtitle.
            return comment[start..end].Trim();
        }

        private static string RequiredAttribute(XElement element, string name)
        {
            var value = (string)element.Attribute(name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"Element {element.Name} is missing attribute {name}.");
            return value;
        }

        private static long ParseInt64(XElement element, string name)
        {
            var value = RequiredAttribute(element, name);
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new InvalidDataException($"Element {element.Name} has an invalid {name} value.");
            return parsed;
        }

        private sealed record DialogueEvent(
            string SoundName,
            uint FmodSubsoundId,
            string DeveloperTranscript);
    }
}
