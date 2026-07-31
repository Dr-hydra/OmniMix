using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.Backend.Audio;
using OmniMixPlayer.Backend.Audio.Dj;
using OmniMixPlayer.Backend.Http;
using OmniMixPlayer.Backend.Logging;
using OmniMixPlayer.Backend.ModuleSystem;
using OmniMixPlayer.Backend.ModuleSystem.Registry;
using OmniMixPlayer.Backend.ModuleSystem.Services;
using OmniMixPlayer.Backend.ModuleSystem.Services.Streaming;
using OmniMixPlayer.Backend.Services;
using OmniMixPlayer.Backend.Storage;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Caching;
using ProtoEvents = OmniMixPlayer.SDK.Protos.Events;

namespace OmniMixPlayer.Backend
{
    public class Program
    {
        /// <summary>
        /// The actual TCP port the backend is listening on for IPC.
        /// May differ from configured port if the desired port was occupied.
        /// </summary>
        public static int IpcPort { get; private set; } = 17890;

        /// <summary>
        /// Unix Domain Socket path as fallback IPC.
        /// Windows: %PUBLIC%/OmniMixPlayer/omnimix.sock | Others: /tmp/omnimix.sock
        /// </summary>
        public static string SocketPath { get; private set; }

        /// <summary>
        /// Directories where omni_port.txt is written so clients can discover the port.
        /// </summary>
        private static readonly List<string> PortFileDirs = new();

        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var applicationDirectory = RuntimePaths.ApplicationDirectory;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = applicationDirectory,
                WebRootPath = Path.Combine(applicationDirectory, "wwwroot")
            });

            var logOptions = new OmniMixFileLoggerOptions
            {
                LogRoot = RuntimePaths.LogsDirectory,
                BackendMinimumLevel = LogLevel.Information,
                ModuleMinimumLevel = LogLevel.Warning,
                MaxFileBytes = 10L * 1024 * 1024,
                RetainedFileCount = 5,
                WriteSessionLog = true
            };
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddProvider(new OmniMixFileLoggerProvider(logOptions));
            // Suppress per-request logs (health checks poll every few seconds)
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            builder.Logging.AddFilter((category, level) =>
                category?.StartsWith("Module:", StringComparison.OrdinalIgnoreCase) != true || level >= LogLevel.Warning);

            // Enable running as Windows Service / Linux systemd service
            builder.Host.UseWindowsService();
            builder.Host.UseSystemd();

            var pluginPath = applicationDirectory;
            var modulesPath = Path.Combine(pluginPath, "modules");
            var configDir = Path.Combine(pluginPath, "config");

            // ── Parse --port-file-dir CLI args (GUI passes its own dir) ──
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--port-file-dir=", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = args[i].Substring("--port-file-dir=".Length).Trim('"');
                    if (Directory.Exists(dir) || !string.IsNullOrWhiteSpace(dir))
                        PortFileDirs.Add(dir);
                }
            }

            // ── Read global_config.json for ipc_port and port_file_dirs ──
            var configuredPort = 17890;
            try
            {
                var configPath = Path.Combine(configDir, "global_config.json");
                if (File.Exists(configPath))
                {
                    if (!StorageVersion.JsonHasCurrentVersion(configPath))
                    {
                        File.Delete(configPath);
                        throw new InvalidDataException("Global config has no current storage version.");
                    }

                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ipc_port", out var portProp) && portProp.TryGetInt32(out var cp))
                        configuredPort = cp;
                    if (doc.RootElement.TryGetProperty("port_file_dirs", out var dirsProp) && dirsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in dirsProp.EnumerateArray())
                        {
                            var dir = Environment.ExpandEnvironmentVariables(d.GetString() ?? "");
                            if (!string.IsNullOrWhiteSpace(dir) && !PortFileDirs.Contains(dir))
                                PortFileDirs.Add(dir);
                        }
                    }
                }
            }
            catch { /* use defaults */ }

            // ── Default fallback: PUBLIC/OmniMixPlayer ──
            var publicDir = Path.Combine(
                Environment.GetEnvironmentVariable("PUBLIC") ?? Path.GetTempPath(),
                "OmniMixPlayer");
            if (!PortFileDirs.Contains(publicDir))
                PortFileDirs.Add(publicDir);

            // ── Unified Unix socket path (fallback IPC) ──
            // Windows: PUBLIC/OmniMixPlayer/omnimix.sock  (shared between admin/non-admin)
            // Others:  /tmp/omnimix.sock
            if (OperatingSystem.IsWindows())
            {
                SocketPath = Path.Combine(publicDir, "omnimix.sock");
            }
            else
            {
                SocketPath = "/tmp/omnimix.sock";
            }
            Directory.CreateDirectory(Path.GetDirectoryName(SocketPath)!);
            DeleteStaleSocket(SocketPath);

            // ── Find a free TCP port (auto-retry if configured port is occupied) ──
            IpcPort = FindFreePort(configuredPort);

            // ── Configure Kestrel: TCP primary + Unix socket fallback ──
            builder.WebHost.ConfigureKestrel(options =>
            {
                // IPC — localhost TCP (primary)
                if (IpcPort == 17890)
                {
                    options.Listen(IPAddress.Any, IpcPort);
                }
                else
                {
                    options.Listen(IPAddress.Loopback, IpcPort);
                    options.Listen(IPAddress.Any, 17890);
                }

                // Unix Domain Socket (fallback for filesystem-based discovery)
                options.ListenUnixSocket(SocketPath);
            });

            // ── Write port file to all configured directories ──
            WritePortFiles();

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
            });

            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            using var loggerFactory = LoggerFactory.Create(logging =>
            {
                logging.AddConsole();
                logging.AddProvider(new OmniMixFileLoggerProvider(logOptions));
                logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
                logging.AddFilter((category, level) =>
                    category?.StartsWith("Module:", StringComparison.OrdinalIgnoreCase) != true || level >= LogLevel.Warning);
            });
            var logger = loggerFactory.CreateLogger("OmniMixPlayer");

            logger.LogInformation("OmniMixPlayer v{Version} starting...", SDK.SDKInfo.SDK_VERSION);
            logger.LogInformation("Plugin path: {Path}", pluginPath);
            logger.LogInformation("Modules path: {Path}", modulesPath);
            logger.LogInformation("IPC port: {Port} (configured: {ConfiguredPort})", IpcPort, configuredPort);
            logger.LogInformation("Port files written to: {Dirs}", string.Join(", ", PortFileDirs));

            // 1. Initialize Library Storage + Registry
            var libraryStorage = new LibraryStorage(configDir, loggerFactory.CreateLogger("LibraryStorage"));
            var libraryRegistry = new LibraryRegistry(libraryStorage, loggerFactory.CreateLogger("LibraryRegistry"));

            // 2. Initialize EventBus
            EventBus.Initialize(loggerFactory.CreateLogger("EventBus"));

            // 3. Initialize DefaultCoverProvider
            DefaultCoverProvider.Initialize();

            // 4. Initialize DependencyLoader
            var dependencyLoader = new DependencyLoader(pluginPath, loggerFactory.CreateLogger("DependencyLoader"));

            // 4b. Initialize Native Decoder Engine
            DecoderEngine.Initialize(loggerFactory.CreateLogger("DecoderEngine"), pluginPath);

            // 5. Initialize StreamingService
            var streamingService = new CoreStreamingService(loggerFactory.CreateLogger("CoreStreaming"));

            // 6. Initialize ModuleManager config
            var moduleConfigManager = new ModuleConfigManager("modules", configDir);

            // 7. Initialize GlobalConfigManager
            var globalConfig = new GlobalConfigManager(configDir);
            ApplyCacheConfiguration(globalConfig, logger);
            globalConfig.OnConfigSaved = () =>
            {
                WritePortFiles();
                ApplyCacheConfiguration(globalConfig, logger);
            };

            // 8. Initialize ModuleLoader with new LibraryRegistry
            var contextFactory = new ModuleContextFactory(
                pluginPath,
                configDir,
                loggerFactory,
                libraryRegistry,
                EventBus.Instance,
                DefaultCoverProvider.Instance,
                dependencyLoader,
                streamingService);
            ModuleLoader.Initialize(modulesPath, contextFactory, loggerFactory.CreateLogger("ModuleLoader"), moduleConfigManager);

            // 9. Instance registry + playback session manager
            var profileStore = new InstanceProfileStore(configDir, loggerFactory.CreateLogger("InstanceProfileStore"));
            var instanceRegistry = new InstanceRegistry(profileStore, loggerFactory.CreateLogger("InstanceRegistry"));
            var timelineStore = new PlaybackTimelineStore(instanceRegistry, libraryRegistry, EventBus.Instance);
            var djAssets = new Fh6DjAssetPreparationCoordinator(
                globalConfig,
                loggerFactory.CreateLogger("Fh6DjAssets"));
            var sessionManager = new PlaybackSessionManager(
                loggerFactory,
                EventBus.Instance,
                libraryRegistry,
                streamingService,
                instanceRegistry,
                timelineStore,
                djAssets);
            globalConfig.OnConfigSaved += djAssets.NotifyConfigurationChanged;
            globalConfig.OnConfigSaved += sessionManager.NotifyGlobalConfigurationChanged;
            djAssets.NotifyConfigurationChanged();

            // 10. Create ApiServer (WS only)
            var apiServer = new ApiServer(instanceRegistry, sessionManager, libraryRegistry, timelineStore, loggerFactory.CreateLogger("ApiServer"));

            var moduleUIHandler = new ModuleUIHandler(ModuleLoader.Instance, apiServer,
                loggerFactory.CreateLogger("ModuleUIHandler"));
            apiServer.SetModuleUIHandler(moduleUIHandler);
            apiServer.SetGlobalConfig(globalConfig);

            new EventBridge(apiServer);

            // 11. Register gRPC services
            builder.Services.AddGrpc();
            builder.Services.AddSingleton<ILibraryRegistry>(libraryRegistry);
            builder.Services.AddSingleton(instanceRegistry);
            builder.Services.AddSingleton(timelineStore);
            builder.Services.AddSingleton(sessionManager);
            builder.Services.AddSingleton(globalConfig);
            builder.Services.AddSingleton(apiServer);

            var app = builder.Build();
            app.Lifetime.ApplicationStopping.Register(djAssets.Dispose);

            // 12. Configure routes
            app.UseCors();
            app.UseWebSockets();
            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
            app.UseDefaultFiles();
            app.UseStaticFiles();
            apiServer.Configure(app);

            // Map gRPC services
            app.MapGrpcService<LibraryServiceImpl>();
            app.MapGrpcService<PlaybackServiceImpl>();
            app.MapGrpcService<InstanceServiceImpl>();
            app.MapGrpcService<LyricServiceImpl>();
            app.MapGrpcService<ConfigServiceImpl>();
            app.MapGrpcService<BackendServiceImpl>();
            app.MapGrpcService<ModuleServiceImpl>();

            // 13. Load modules in background
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("Loading modules...");
                    await ModuleLoader.Instance.LoadAllModulesAsync();

                    var tracks = libraryRegistry.QueryTracks(new SDK.Protos.Models.TrackQuery { Limit = 0 });
                    logger.LogInformation("Loaded {Count} tracks from {ModuleCount} modules",
                        tracks.Count, ModuleLoader.Instance.LoadedModules.Count);

                    await apiServer.BroadcastProtoEvent(new ProtoEvents.WsEvent
                    {
                        Type = "playlist.updated",
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        PlaylistUpdated = new ProtoEvents.PlaylistUpdatedEvent { SongCount = tracks.Count }
                    });

                    foreach (var loaded in ModuleLoader.Instance.LoadedModules)
                    {
                        await apiServer.BroadcastProtoEvent(new ProtoEvents.WsEvent
                        {
                            Type = "module.loaded",
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            ModuleChanged = new ProtoEvents.ModuleChangedEvent { ModuleId = loaded.Module.ModuleId, Enabled = true, DisplayName = loaded.Module.DisplayName }
                        });

                        if (loaded.Module is IModuleUIProvider uiProvider)
                        {
                            moduleUIHandler.RegisterPushUICallback(loaded.Module.ModuleId, uiProvider);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load modules");
                }
            });

            // 14. Start the server
            logger.LogInformation("OmniMixPlayer API: tcp://127.0.0.1:{IpcPort} (REST/WS/gRPC-Web), unix://{SocketPath} (fallback), http://0.0.0.0:17890 (remote)",
                IpcPort, SocketPath);
            await app.RunAsync();
        }

        private static void DeleteStaleSocket(string path)
        {
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch { /* ignore */ }
            }
        }

        private static void ApplyCacheConfiguration(GlobalConfigManager config, ILogger logger)
        {
            try
            {
                var configuredRoot = config.GetValue<string>("cache_root", null);
                var configuredMaximum = config.GetValue<long>("cache_max_bytes", CachePaths.MaximumBytes);
                CachePaths.Configure(configuredRoot, configuredMaximum);
                CachePaths.EnsureRootDirectory();

                var cleanup = CacheQuotaManager.Default.EnforceQuota();
                logger.LogInformation(
                    "Cache root: {Root}; usage after cleanup: {Usage}/{Maximum} bytes",
                    CachePaths.RootDirectory,
                    cleanup.AfterBytes,
                    CachePaths.MaximumBytes);
                if (!cleanup.QuotaSatisfied)
                {
                    logger.LogWarning(
                        "Cache quota remains exceeded because {Locked} files are active or locked",
                        cleanup.SkippedLockedFileCount);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to apply global cache configuration");
            }
        }

        /// <summary>
        /// Find a free TCP port starting from <paramref name="startPort"/>.
        /// Tries up to 100 ports. Returns 0 (OS-assigned) if none free.
        /// </summary>
        private static int FindFreePort(int startPort)
        {
            const int maxAttempts = 100;
            for (int port = startPort; port < startPort + maxAttempts; port++)
            {
                try
                {
                    using var socket = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.InterNetwork,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Tcp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                    return port;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    Console.WriteLine($"[OmniMix] Port {port} occupied, trying {port + 1}...");
                }
            }
            Console.WriteLine($"[OmniMix] WARNING: All ports {startPort}-{startPort + maxAttempts - 1} occupied, will use OS-assigned port");
            return 0;
        }

        /// <summary>
        /// Write omni_port.txt (containing just the port number) to all configured directories.
        /// </summary>
        private static void WritePortFiles()
        {
            var portStr = IpcPort.ToString();
            foreach (var dir in PortFileDirs)
            {
                try
                {
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var filePath = Path.Combine(dir, "omnimix_port.txt");
                    File.WriteAllText(filePath, portStr);
                    Console.WriteLine($"[OmniMix] Port file written: {filePath} → {portStr}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OmniMix] WARNING: Failed to write port file to {dir}: {ex.Message}");
                }
            }
        }
    }

}
