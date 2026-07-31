using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    public sealed class Fh6DjGameAssetLocator
    {
        // DJ playback is fixed to the game's canonical radio authoring set. This is
        // intentionally not selected from the UI or display-language configuration.
        public const string CanonicalRadioInfoFileName = "RadioInfo_EN.xml";

        public Fh6DjGameAssets Locate(string gameRoot, int hostNumber)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
                throw new ArgumentException("An FH6 game root is required.", nameof(gameRoot));

            var host = Fh6DjHosts.GetByHostNumber(hostNumber);
            var suppliedPath = Path.GetFullPath(gameRoot.Trim());
            if (File.Exists(suppliedPath))
                suppliedPath = Path.GetDirectoryName(suppliedPath)
                    ?? throw new DirectoryNotFoundException("The FH6 executable directory could not be resolved.");

            var runtimeRoot = ResolveRuntimeRoot(suppliedPath);
            var executablePath = Path.Combine(runtimeRoot, "forzahorizon6.exe");
            var audioDirectory = Path.Combine(runtimeRoot, "media", "Audio");
            var dialogueDjsPath = Path.Combine(audioDirectory, "Dialogue_DJs.xml");
            var radioInfoPath = Path.Combine(audioDirectory, CanonicalRadioInfoFileName);

            RequireFile(executablePath, "FH6 executable");
            RequireFile(dialogueDjsPath, "FH6 DJ dialogue metadata");
            RequireFile(radioInfoPath, "FH6 canonical radio metadata");

            var bankName = ReadCanonicalBankName(radioInfoPath, host);
            var sourceBankPath = Path.Combine(audioDirectory, "FMODBanks", bankName + ".assets.bank");
            RequireFile(sourceBankPath, $"FH6 voice bank {bankName}");
            EnsureWithinDirectory(sourceBankPath, audioDirectory);

            return new Fh6DjGameAssets(
                suppliedPath,
                runtimeRoot,
                audioDirectory,
                executablePath,
                ReadGameVersion(executablePath),
                dialogueDjsPath,
                radioInfoPath,
                sourceBankPath,
                host);
        }

        public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }

        private static string ResolveRuntimeRoot(string gameRoot)
        {
            var directExe = Path.Combine(gameRoot, "forzahorizon6.exe");
            if (File.Exists(directExe))
                return gameRoot;

            var contentRoot = Path.Combine(gameRoot, "Content");
            if (File.Exists(Path.Combine(contentRoot, "forzahorizon6.exe")))
                return contentRoot;

            throw new FileNotFoundException(
                "Could not find forzahorizon6.exe in the selected folder or its Content subfolder.",
                directExe);
        }

        private static string ReadCanonicalBankName(string radioInfoPath, Fh6DjHost host)
        {
            var document = XDocument.Load(radioInfoPath, LoadOptions.None);
            var station = document
                .Descendants("RadioStation")
                .SingleOrDefault(element =>
                    string.Equals((string)element.Attribute("DJCharID"),
                        host.DjCharacterId.ToString(CultureInfo.InvariantCulture),
                        StringComparison.Ordinal));

            if (station == null)
                throw new InvalidDataException(
                    $"{Path.GetFileName(radioInfoPath)} does not define DJCharID {host.DjCharacterId}.");

            var candidates = station
                .Element("Banks")?
                .Elements("Bank")
                .Select(element => (string)element.Attribute("Name"))
                .Where(name => !string.IsNullOrWhiteSpace(name) &&
                    name.StartsWith(host.VoiceBankStem + "_", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];

            if (candidates.Length != 1)
                throw new InvalidDataException(
                    $"Expected exactly one canonical {host.VoiceBankStem} bank for DJCharID {host.DjCharacterId}, " +
                    $"but found {candidates.Length}.");

            return candidates[0];
        }

        private static string ReadGameVersion(string executablePath)
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var version = info.ProductVersion;
            if (string.IsNullOrWhiteSpace(version))
                version = info.FileVersion;
            if (!string.IsNullOrWhiteSpace(version))
                return version.Trim();

            var file = new FileInfo(executablePath);
            return $"unknown-{file.Length:x}-{file.LastWriteTimeUtc.Ticks:x}";
        }

        private static void RequireFile(string path, string description)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"{description} was not found.", path);
        }

        internal static void EnsureWithinDirectory(string path, string directory)
        {
            var fullPath = Path.GetFullPath(path);
            var fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The DJ asset path escaped the selected FH6 media directory.");
        }
    }

    public static class Fh6Fsb5BankInspector
    {
        private static readonly byte[] Signature = "FSB5"u8.ToArray();

        public static Fh6Fsb5BankInfo Inspect(string bankPath)
        {
            using var stream = new FileStream(bankPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var offset = FindSignature(stream, maxBytesToScan: 4 * 1024 * 1024);
            if (offset < 0)
                throw new InvalidDataException("The FH6 voice bank does not contain an FSB5 payload.");

            if (offset + 28 > stream.Length)
                throw new InvalidDataException("The FSB5 header is truncated.");

            stream.Position = offset + 4;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            var info = new Fh6Fsb5BankInfo(
                offset,
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32());

            if (info.SubsongCount == 0)
                throw new InvalidDataException("The FSB5 voice bank contains no subsounds.");
            if (info.AudioDataBytes == 0)
                throw new InvalidDataException("The FSB5 voice bank contains no audio data.");

            return info;
        }

        private static long FindSignature(Stream stream, int maxBytesToScan)
        {
            stream.Position = 0;
            var buffer = new byte[64 * 1024];
            long totalRead = 0;
            var matched = 0;

            while (totalRead < maxBytesToScan)
            {
                var requested = (int)Math.Min(buffer.Length, maxBytesToScan - totalRead);
                var read = stream.Read(buffer, 0, requested);
                if (read == 0)
                    break;

                for (var index = 0; index < read; index++)
                {
                    if (buffer[index] == Signature[matched])
                    {
                        matched++;
                        if (matched == Signature.Length)
                            return totalRead + index - Signature.Length + 1;
                    }
                    else
                    {
                        matched = buffer[index] == Signature[0] ? 1 : 0;
                    }
                }

                totalRead += read;
            }

            return -1;
        }
    }
}
