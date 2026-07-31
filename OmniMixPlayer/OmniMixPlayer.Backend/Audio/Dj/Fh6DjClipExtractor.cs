using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OmniMixPlayer.Backend.Audio.Dj
{
    public interface IFh6DjClipExtractor
    {
        string Identity { get; }

        Task ExtractAsync(
            string sourceBankPath,
            int subsongIndex,
            string outputWavePath,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Offline FSB5 extractor. No process is started by the premix reader or an
    /// audio callback; callers run this adapter while preparing a selected host.
    /// </summary>
    public sealed class VgmstreamCliDjClipExtractor : IFh6DjClipExtractor
    {
        public const string PathEnvironmentVariable = "OMNIMIX_VGMSTREAM_CLI";

        private readonly string _executablePath;

        public string Identity { get; }

        public VgmstreamCliDjClipExtractor(string executablePath = null)
        {
            _executablePath = ResolveExecutable(executablePath);
            Identity = BuildIdentity(_executablePath);
        }

        public async Task ExtractAsync(
            string sourceBankPath,
            int subsongIndex,
            string outputWavePath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(sourceBankPath))
                throw new FileNotFoundException("The FH6 DJ source bank was not found.", sourceBankPath);
            if (subsongIndex < 1)
                throw new ArgumentOutOfRangeException(nameof(subsongIndex));
            if (string.IsNullOrWhiteSpace(outputWavePath))
                throw new ArgumentException("An output wave path is required.", nameof(outputWavePath));

            var fullOutputPath = Path.GetFullPath(outputWavePath);
            var outputDirectory = Path.GetDirectoryName(fullOutputPath)
                ?? throw new ArgumentException("The output wave directory could not be resolved.", nameof(outputWavePath));
            Directory.CreateDirectory(outputDirectory);

            // vgmstream uses the traditional Win32 file APIs in current builds.
            // Keep its transient filename short even when the final cache key is deep.
            var temporaryPath = Path.Combine(
                outputDirectory,
                $".x-{Guid.NewGuid():N}"[..12] + ".wav");
            try
            {
                using var process = new Process
                {
                    StartInfo = CreateStartInfo(sourceBankPath, subsongIndex, temporaryPath)
                };

                if (!process.Start())
                    throw new InvalidOperationException("vgmstream-cli could not be started.");

                var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
                try
                {
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    throw;
                }

                var outputText = await standardOutput.ConfigureAwait(false);
                var errorText = await standardError.ConfigureAwait(false);
                if (process.ExitCode != 0 || !LooksLikeWaveFile(temporaryPath))
                {
                    var diagnostics = string.Join(Environment.NewLine,
                        new[] { errorText, outputText }
                            .Where(text => !string.IsNullOrWhiteSpace(text)))
                        .Trim();
                    if (diagnostics.Length > 2048)
                        diagnostics = diagnostics[..2048];
                    throw new InvalidDataException(
                        $"vgmstream-cli failed to extract FSB subsound {subsongIndex}. {diagnostics}");
                }

                File.Move(temporaryPath, fullOutputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        public static bool TryResolveExecutable(out string executablePath)
        {
            try
            {
                executablePath = ResolveExecutable(null);
                return true;
            }
            catch (FileNotFoundException)
            {
                executablePath = null;
                return false;
            }
        }

        private ProcessStartInfo CreateStartInfo(string sourceBankPath, int subsongIndex, string outputPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add(subsongIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputPath);
            startInfo.ArgumentList.Add(Path.GetFullPath(sourceBankPath));
            return startInfo;
        }

        private static string ResolveExecutable(string explicitPath)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(explicitPath))
                candidates.Add(explicitPath);

            var environmentPath = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentPath))
                candidates.Add(environmentPath);

            candidates.Add(Path.Combine(AppContext.BaseDirectory, "tools", "vgmstream", "vgmstream-cli.exe"));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "vgmstream", "vgmstream-cli.exe"));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "vgmstream-cli.exe"));

            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathValue))
            {
                foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    candidates.Add(Path.Combine(directory.Trim(), "vgmstream-cli.exe"));
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(candidate.Trim().Trim('"'));
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    // Ignore malformed PATH entries and continue with the next candidate.
                }
            }

            throw new FileNotFoundException(
                "vgmstream-cli.exe is required to prepare FH6 DJ voice clips. " +
                $"Set {PathEnvironmentVariable} or place it under tools\\vgmstream next to the backend.");
        }

        private static string BuildIdentity(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = SHA256.HashData(stream);
            return $"vgmstream-cli:{Convert.ToHexString(hash)}";
        }

        private static bool LooksLikeWaveFile(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 44)
                return false;

            Span<byte> header = stackalloc byte[12];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Read(header) == header.Length &&
                header[..4].SequenceEqual("RIFF"u8) &&
                header[8..12].SequenceEqual("WAVE"u8);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Cancellation remains the primary error.
            }
        }
    }
}
