using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    public static class Fh6DjClipSelector
    {
        public static Fh6DjPreparedClip Select(
            Fh6DjCacheManifest manifest,
            Fh6DjInsertionContent content,
            string selectionKey,
            string avoidSoundName = null)
        {
            foreach (var kind in GetPreferredKinds(content))
            {
                try
                {
                    return Select(manifest, kind, selectionKey, avoidSoundName);
                }
                catch (InvalidOperationException)
                {
                    // Fall through to the next acceptable clip category.
                }
            }

            throw new InvalidOperationException("The prepared host has no clip matching the selected DJ content.");
        }

        /// <summary>
        /// Deterministic selection keeps retry/seek behavior stable for a track while
        /// still distributing clips across a queue. Pass the track UUID as selectionKey.
        /// </summary>
        public static Fh6DjPreparedClip Select(
            Fh6DjCacheManifest manifest,
            Fh6DjClipKind kind,
            string selectionKey,
            string avoidSoundName = null)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            if (string.IsNullOrWhiteSpace(selectionKey))
                throw new ArgumentException("A stable selection key is required.", nameof(selectionKey));

            var candidates = manifest.Clips
                .Where(clip => clip.Kind == kind)
                .OrderBy(clip => clip.SubsongIndex)
                .ToArray();
            if (candidates.Length == 0)
                throw new InvalidOperationException($"The prepared host has no {kind} clips.");

            var seed = string.Join("|",
                manifest.SourceBankSha256,
                manifest.HostNumber,
                kind,
                selectionKey);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            var index = (int)(BitConverter.ToUInt32(hash, 0) % (uint)candidates.Length);

            if (candidates.Length > 1 &&
                string.Equals(candidates[index].SoundName, avoidSoundName, StringComparison.Ordinal))
                index = (index + 1) % candidates.Length;

            return candidates[index];
        }

        private static Fh6DjClipKind[] GetPreferredKinds(Fh6DjInsertionContent content)
        {
            return content switch
            {
                Fh6DjInsertionContent.Chatter => [Fh6DjClipKind.IdleChatter],
                Fh6DjInsertionContent.TransitionIn => [Fh6DjClipKind.GeneralTransitionIn],
                Fh6DjInsertionContent.TransitionOut => [Fh6DjClipKind.GeneralTransitionOut],
                _ =>
                [
                    Fh6DjClipKind.GeneralTransitionIn,
                    Fh6DjClipKind.IdleChatter,
                    Fh6DjClipKind.GeneralTransitionOut
                ]
            };
        }
    }
}
