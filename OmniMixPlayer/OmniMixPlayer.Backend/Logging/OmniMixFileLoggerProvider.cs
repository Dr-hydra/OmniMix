using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace OmniMixPlayer.Backend.Logging
{
    public sealed class OmniMixFileLoggerProvider : ILoggerProvider
    {
        private readonly OmniMixFileLoggerOptions _options;
        private readonly ConcurrentDictionary<string, LogFileWriter> _writers = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _sessionId = Guid.NewGuid().ToString("N")[..8];

        public OmniMixFileLoggerProvider(OmniMixFileLoggerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            Directory.CreateDirectory(_options.LogRoot);
            Directory.CreateDirectory(Path.Combine(_options.LogRoot, "modules"));
            Directory.CreateDirectory(Path.Combine(_options.LogRoot, "diagnostics"));
        }

        public ILogger CreateLogger(string categoryName)
            => new OmniMixFileLogger(this, categoryName ?? "Default");

        public void Dispose()
        {
            foreach (var writer in _writers.Values)
                writer.Dispose();
            _writers.Clear();
        }

        private bool IsEnabled(string category, LogLevel logLevel)
        {
            if (logLevel == LogLevel.None) return false;
            var minimumLevel = TryGetModuleId(category, out _)
                ? _options.ModuleMinimumLevel
                : _options.BackendMinimumLevel;
            return logLevel >= minimumLevel;
        }

        private void Write<TState>(string category, LogLevel level, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(category, level)) return;

            var message = formatter?.Invoke(state, exception) ?? state?.ToString() ?? "";
            message = Redact(message);
            var exceptionText = exception == null ? "" : Environment.NewLine + Redact(exception.ToString());
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
            var line = $"{timestamp} [{level}] [{_sessionId}] {category}: {message}{exceptionText}";

            GetWriter(GetTargetPath(category)).WriteLine(line);
            if (_options.WriteSessionLog)
                GetWriter(Path.Combine(_options.LogRoot, "diagnostics", "latest-session.log")).WriteLine(line);
        }

        private LogFileWriter GetWriter(string path)
            => _writers.GetOrAdd(path, p => new LogFileWriter(p, _options.MaxFileBytes, _options.RetainedFileCount));

        private string GetTargetPath(string category)
        {
            if (TryGetModuleId(category, out var moduleId))
            {
                var fileName = GetModuleFileName(moduleId);
                return Path.Combine(_options.LogRoot, "modules", fileName);
            }

            return Path.Combine(_options.LogRoot, "omnimix_backend.log");
        }

        private static bool TryGetModuleId(string category, out string moduleId)
        {
            const string prefix = "Module:";
            if (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                moduleId = category[prefix.Length..].Trim();
                return !string.IsNullOrWhiteSpace(moduleId);
            }

            moduleId = "";
            return false;
        }

        private static string GetModuleFileName(string moduleId)
        {
            var normalized = moduleId.StartsWith("com.chillpatcher.", StringComparison.OrdinalIgnoreCase)
                ? moduleId["com.chillpatcher.".Length..]
                : moduleId;
            normalized = SanitizeFileName(normalized);
            return string.IsNullOrWhiteSpace(normalized) ? "module.log" : $"{normalized}.log";
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            return new string((value ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            var redacted = value;
            foreach (var key in SensitiveKeys)
            {
                redacted = System.Text.RegularExpressions.Regex.Replace(
                    redacted,
                    $"({key}\\s*[=:]\\s*)([^,;\\s\\]}}]+)",
                    "$1***",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            return redacted;
        }

        private static readonly string[] SensitiveKeys =
        {
            "token", "viptoken", "vip_token", "cookie", "qrcode", "qr_key", "password", "credential"
        };

        private sealed class OmniMixFileLogger : ILogger
        {
            private readonly OmniMixFileLoggerProvider _provider;
            private readonly string _category;

            public OmniMixFileLogger(OmniMixFileLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(_category, logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => _provider.Write(_category, logLevel, eventId, state, exception, formatter);
        }

        private sealed class LogFileWriter : IDisposable
        {
            private readonly object _lock = new();
            private readonly string _path;
            private readonly long _maxFileBytes;
            private readonly int _retainedFileCount;
            private StreamWriter _writer;

            public LogFileWriter(string path, long maxFileBytes, int retainedFileCount)
            {
                _path = path;
                _maxFileBytes = Math.Max(1024 * 1024, maxFileBytes);
                _retainedFileCount = Math.Max(1, retainedFileCount);
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
                OpenWriter();
            }

            public void WriteLine(string line)
            {
                lock (_lock)
                {
                    RotateIfNeeded();
                    _writer.WriteLine(line);
                }
            }

            public void Dispose()
            {
                lock (_lock)
                    _writer?.Dispose();
            }

            private void OpenWriter()
            {
                var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream) { AutoFlush = true };
            }

            private void RotateIfNeeded()
            {
                if (_writer?.BaseStream.Length < _maxFileBytes) return;

                _writer.Dispose();
                for (var i = _retainedFileCount - 1; i >= 1; i--)
                {
                    var older = $"{_path}.{i}";
                    var newer = $"{_path}.{i + 1}";
                    if (File.Exists(newer)) File.Delete(newer);
                    if (File.Exists(older)) File.Move(older, newer);
                }

                var first = $"{_path}.1";
                if (File.Exists(first)) File.Delete(first);
                if (File.Exists(_path)) File.Move(_path, first);
                OpenWriter();
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    public sealed class OmniMixFileLoggerOptions
    {
        public string LogRoot { get; init; } = RuntimePaths.LogsDirectory;
        public LogLevel BackendMinimumLevel { get; init; } = LogLevel.Information;
        public LogLevel ModuleMinimumLevel { get; init; } = LogLevel.Warning;
        public long MaxFileBytes { get; init; } = 10L * 1024 * 1024;
        public int RetainedFileCount { get; init; } = 5;
        public bool WriteSessionLog { get; init; } = true;
    }
}
