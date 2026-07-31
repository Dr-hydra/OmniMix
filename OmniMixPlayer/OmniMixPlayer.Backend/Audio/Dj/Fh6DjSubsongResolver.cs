using System;
using System.Collections.Concurrent;
using System.IO;
using FModBankParser;
using FModBankParser.Utils;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    /// <summary>
    /// Maps a canonical FMOD sound name to vgmstream's one-based FSB subsong number.
    /// XML order is deliberately not used: it can diverge from the embedded FSB order.
    /// </summary>
    public interface IFh6DjSubsongResolver
    {
        int ResolveSubsongIndex(string sourceBankPath, string soundName);
    }

    public sealed class Fh6FmodSoundTableSubsongResolver : IFh6DjSubsongResolver
    {
        private readonly ConcurrentDictionary<BankKey, FModReader> _readers = new();

        public int ResolveSubsongIndex(string sourceBankPath, string soundName)
        {
            if (string.IsNullOrWhiteSpace(sourceBankPath))
                throw new ArgumentException("An FMOD source bank path is required.", nameof(sourceBankPath));
            if (string.IsNullOrWhiteSpace(soundName))
                throw new ArgumentException("A canonical FMOD sound name is required.", nameof(soundName));

            var path = Path.GetFullPath(sourceBankPath);
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException("The FH6 DJ source bank was not found.", path);

            var key = new BankKey(path, info.Length, info.LastWriteTimeUtc.Ticks);
            var reader = _readers.GetOrAdd(key, static bankKey =>
                FModBankParser.FModBankParser.LoadSoundBank(new FileInfo(bankKey.Path), null));
            var table = reader.SoundTable;
            if (table == null)
                throw new InvalidDataException($"FMOD bank {Path.GetFileName(path)} does not expose a sound table.");

            var zeroBasedIndex = table.Find(JenkinsHash.Hash64(soundName, 0, 0));
            if (zeroBasedIndex < 0)
            {
                throw new InvalidDataException(
                    $"FMOD bank {Path.GetFileName(path)} does not map sound {soundName} to an FSB subsound.");
            }

            return checked(zeroBasedIndex + 1);
        }

        private sealed record BankKey(string Path, long Length, long LastWriteTicks);
    }
}
