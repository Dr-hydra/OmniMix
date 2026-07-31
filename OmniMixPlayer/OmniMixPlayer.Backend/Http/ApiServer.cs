using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.Backend.Audio;
using OmniMixPlayer.Backend.ModuleSystem;
using OmniMixPlayer.SDK.Interfaces;
using ProtoEvents = OmniMixPlayer.SDK.Protos.Events;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Backend.Http
{
    /// <summary>
    /// WebSocket + 模块 UI API 服务器
    /// RESTful CRUD 已迁移到 gRPC，这里只保留 WebSocket 事件推送和模块 UI
    /// </summary>
    public class ApiServer
    {
        private readonly InstanceRegistry _registry;
        private readonly PlaybackSessionManager _sessions;
        private readonly ILibraryRegistry _libraryRegistry;
        private readonly PlaybackTimelineStore _timeline;
        private readonly ILogger _logger;
        private readonly List<WebSocket> _wsClients = new();
        private readonly object _wsLock = new();
        private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _wsSendLocks = new();
        private ModuleUIHandler _moduleUIHandler;
        private GlobalConfigManager _globalConfig;

        public ApiServer(InstanceRegistry registry, PlaybackSessionManager sessions, ILibraryRegistry libraryRegistry, PlaybackTimelineStore timeline, ILogger logger)
        {
            _registry = registry;
            _sessions = sessions;
            _libraryRegistry = libraryRegistry;
            _timeline = timeline;
            _logger = logger;

            _sessions.OnTrackChanged += (id, track) =>
                _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
                {
                    Type = "track.changed",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    TrackChanged = new ProtoEvents.TrackChangedEvent { InstanceId = id, Uuid = track?.Uuid ?? "", Title = track?.Title ?? "", Artist = track?.Artist ?? "", AlbumId = track?.AlbumId ?? "", Duration = track?.Duration ?? 0, ModuleId = track?.ModuleId ?? "" }
                });
            _sessions.OnPlaybackMetadataChanged += (id, track, duration) =>
                _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
                {
                    Type = "track.changed",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    TrackChanged = new ProtoEvents.TrackChangedEvent
                    {
                        InstanceId = id,
                        Uuid = track?.Uuid ?? "",
                        Title = track?.Title ?? "",
                        Artist = track?.Artist ?? "",
                        AlbumId = track?.AlbumId ?? "",
                        Duration = duration,
                        ModuleId = track?.ModuleId ?? ""
                    }
                });
            _sessions.OnStateChanged += (id, ctrl) =>
                _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
                {
                    Type = "state.changed",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StateChanged = new ProtoEvents.StateChangedEvent { InstanceId = id, State = ctrl.IsPlaying ? 1 : (ctrl.CurrentTrack != null ? 2 : 0) }
                });
            _sessions.OnPositionChanged += (id, pos) =>
                _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
                {
                    Type = "position.changed",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    PositionChanged = new ProtoEvents.PositionChangedEvent { InstanceId = id, Position = pos }
                });
            _sessions.OnQueueChanged += (id) =>
                _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
                {
                    Type = "queue.changed",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    QueueChanged = new ProtoEvents.QueueChangedEvent { InstanceId = id }
                });

            void OnInstancesOrSessionsChanged()
            {
                var evt = new ProtoEvents.WsEvent { Type = "instances.changed", Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                evt.InstancesChanged = new ProtoEvents.InstancesChangedEvent();
                evt.InstancesChanged.Instances.AddRange(_registry.ListSummaries(_sessions));
                _ = BroadcastProtoEvent(evt);
            }
            _sessions.OnSessionsChanged += OnInstancesOrSessionsChanged;
            _registry.OnChanged += OnInstancesOrSessionsChanged;

            _registry.OnVolumeChanged += (id, vol) =>
                _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
                {
                    Type = "volume.changed",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    VolumeChanged = new ProtoEvents.VolumeChangedEvent { InstanceId = id, Volume = vol }
                });

            _registry.OnLatencyChanged += (id, lat) =>
                _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
                {
                    Type = "latency.changed",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    LatencyChanged = new ProtoEvents.LatencyChangedEvent { InstanceId = id, Latency = lat }
                });

            _registry.OnEqualizerChanged += (id, eq) =>
                _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
                {
                    Type = "eq.changed",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    EqChanged = new ProtoEvents.EqualizerChangedEvent { InstanceId = id, State = eq }
                });
        }

        public void SetModuleUIHandler(ModuleUIHandler handler) => _moduleUIHandler = handler;
        public void SetGlobalConfig(GlobalConfigManager config) => _globalConfig = config;

        public void Configure(IEndpointRouteBuilder endpoints)
        {
            // WebSocket
            endpoints.Map("/ws", async (HttpContext ctx) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                await HandleWebSocket(ws);
            });

            // Module UI event
            endpoints.MapPost("/api/ui/event", async (HttpContext ctx) =>
            {
                if (_moduleUIHandler == null) { ctx.Response.StatusCode = 503; return; }
                using var reader = new StreamReader(ctx.Request.Body);
                var msg = await reader.ReadToEndAsync();
                await _moduleUIHandler.HandleUiEvent(msg);
            });

            // Global config (keep simple REST)
            endpoints.MapGet("/api/config", () =>
            {
                var config = _globalConfig?.GetAll() ?? new Dictionary<string, object>();
                return Results.Json(config);
            });
            endpoints.MapPut("/api/config", async (HttpContext ctx) =>
            {
                try
                {
                    using var reader = new StreamReader(ctx.Request.Body);
                    var json = await reader.ReadToEndAsync();
                    var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        _globalConfig?.SetValue<object>(prop.Name, ConvertElement(prop.Value));
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to parse config update"); return Results.BadRequest(new { error = ex.Message }); }
                return Results.Ok();
            });
            endpoints.MapPost("/api/config/save", () => { _globalConfig?.Save(); return Results.Ok(new { message = "Config saved" }); });

            // Health
            endpoints.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));

            // One-shot playback failure notices for low-noise frontend hints.
            endpoints.MapGet("/api/playback/{instanceId}/failure-notice", (string instanceId, bool? consume) =>
            {
                var notice = _sessions.GetFailureNotice(instanceId, consume ?? true);
                return notice != null ? Results.Json(notice) : Results.NoContent();
            });

            // Version
            endpoints.MapGet("/api/version", () => Results.Json(new { version = SDK.SDKInfo.SDK_VERSION, name = SDK.SDKInfo.SDK_NAME }));

            // ── Playback / library REST facade for the embedded WebUI ──
            endpoints.MapGet("/api/instances", () => Safe(() =>
                Results.Json(_registry.ListSummaries(_sessions).Select(ToInstanceDto))));

            endpoints.MapGet("/api/instances/{id}/status", (string id) => Safe(() =>
                Results.Json(ToPlaybackStatusDto(GetPlaybackStatusOrFallback(id)))));

            endpoints.MapPost("/api/instances/{id}/playback", async (string id, HttpContext ctx) => await SafeAsync(async () =>
            {
                var body = await ReadJsonRootAsync(ctx);
                var command = GetString(body, "command").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(command)) return Results.BadRequest(new { error = "command is required" });
                RunPlaybackCommand(id, command, body);
                return Results.Ok(new { success = true });
            }));

            endpoints.MapGet("/api/instances/{id}/queue", (string id) => Safe(() =>
                Results.Json(new { queue = GetQueue(id) })));

            endpoints.MapPost("/api/instances/{id}/queue", async (string id, HttpContext ctx) => await SafeAsync(async () =>
            {
                var body = await ReadJsonRootAsync(ctx);
                var uuids = GetStringArray(body, "uuids");
                var uuid = GetString(body, "uuid");
                var replace = GetBool(body, "replace") ?? false;
                var index = GetInt(body, "index") ?? int.MaxValue;

                EnsureInstanceExists(id);
                if (replace)
                {
                    _timeline.SetQueue(id, uuids);
                }
                else if (uuids.Length > 0)
                {
                    _timeline.InsertIntoQueue(id, uuids, index);
                }
                else if (!string.IsNullOrWhiteSpace(uuid))
                {
                    _timeline.AddToQueue(id, uuid);
                }
                else
                {
                    return Results.BadRequest(new { error = "uuid or uuids is required" });
                }
                return Results.Ok(new { success = true, queue = GetQueue(id) });
            }));

            endpoints.MapDelete("/api/instances/{id}/queue", (string id, HttpContext ctx) => Safe(() =>
            {
                EnsureInstanceExists(id);
                if (ctx.Request.Query.TryGetValue("index", out var indexValue) && int.TryParse(indexValue, out var index))
                {
                    _timeline.RemoveFromQueue(id, index);
                }
                else if (ctx.Request.Query.TryGetValue("uuid", out var uuidValue) && !string.IsNullOrWhiteSpace(uuidValue))
                {
                    _timeline.RemoveFromQueue(id, uuidValue.ToString());
                }
                else
                {
                    _timeline.ClearQueue(id);
                }
                return Results.Ok(new { success = true, queue = GetQueue(id) });
            }));

            endpoints.MapPost("/api/instances/{id}/queue/move", async (string id, HttpContext ctx) => await SafeAsync(async () =>
            {
                var body = await ReadJsonRootAsync(ctx);
                var from = GetInt(body, "fromIndex");
                var to = GetInt(body, "toIndex");
                if (!from.HasValue || !to.HasValue) return Results.BadRequest(new { error = "fromIndex and toIndex are required" });
                EnsureInstanceExists(id);
                _timeline.MoveInQueue(id, from.Value, to.Value);
                return Results.Ok(new { success = true, queue = GetQueue(id) });
            }));

            endpoints.MapGet("/api/instances/{id}/history", (string id) => Safe(() =>
                Results.Json(new { history = GetHistory(id) })));

            endpoints.MapGet("/api/instances/{id}/playlist-sources", (string id) => Safe(() =>
                Results.Json(new { sources = _timeline.GetPlaylistSources(id).Select(ToPlaylistSourceDto) })));

            endpoints.MapPost("/api/instances/{id}/playlist-sources", async (string id, HttpContext ctx) => await SafeAsync(async () =>
            {
                var body = await ReadJsonRootAsync(ctx);
                var action = GetString(body, "action", "add").Trim().ToLowerInvariant();
                EnsureInstanceExists(id);
                var existing = _timeline.GetPlaylistSources(id).Select(s => new PlaylistSourceRequest
                {
                    id = s.Id,
                    name = s.Name,
                    kind = s.Kind,
                    refId = s.RefId
                }).ToList();

                if (action == "remove")
                {
                    var sourceId = GetString(body, "sourceId");
                    existing = existing.Where(s => !string.Equals(s.id, sourceId, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else if (action == "set")
                {
                    existing = GetPlaylistSourceRequests(body).ToList();
                }
                else
                {
                    var source = GetPlaylistSourceRequest(body);
                    if (string.IsNullOrWhiteSpace(source.id)) return Results.BadRequest(new { error = "source.id is required" });
                    existing.RemoveAll(s => string.Equals(s.id, source.id, StringComparison.OrdinalIgnoreCase));
                    existing.Add(source);
                }

                _timeline.SetPlaylistSources(id, existing);
                return Results.Ok(new { success = true, sources = _timeline.GetPlaylistSources(id).Select(ToPlaylistSourceDto) });
            }));

            endpoints.MapGet("/api/library/tracks", (HttpContext ctx) => Safe(() =>
            {
                var query = BuildTrackQuery(ctx);
                var tracks = _libraryRegistry.QueryTracks(query);
                return Results.Json(new
                {
                    tracks = tracks.Select(ToTrackDto),
                    pagination = new { offset = query.Offset, limit = query.Limit, total = _libraryRegistry.CountTracks(query) }
                });
            }));

            endpoints.MapGet("/api/library/playlists", (HttpContext ctx) => Safe(() =>
            {
                var query = BuildPlaylistQuery(ctx);
                var playlists = _libraryRegistry.QueryPlaylists(query);
                return Results.Json(new
                {
                    playlists = playlists.Select(ToPlaylistDto),
                    pagination = new { offset = query.Offset, limit = query.Limit, total = _libraryRegistry.CountPlaylists(query) }
                });
            }));

            endpoints.MapGet("/api/library/playlists/{id}", (string id) => Safe(() =>
            {
                var playlist = _libraryRegistry.GetPlaylistWithEntries(id);
                return playlist != null ? Results.Json(ToPlaylistWithEntriesDto(playlist)) : Results.NotFound(new { error = "Playlist not found" });
            }));

            // Backend stop
            endpoints.MapPost("/api/backend/stop", () =>
            {
                RequestBackendStop();
                return Results.Ok(new { message = "Shutting down" });
            });

            // ── Module REST endpoints (UI still JSON-based) ──

            // List modules
            endpoints.MapGet("/api/modules", () =>
            {
                var loader = _moduleUIHandler?.ModuleLoader;
                if (loader == null) return Results.Json(Array.Empty<object>());
                var modules = loader.LoadedModules.Select(m => new
                {
                    id = m.Module.ModuleId,
                    name = m.Module.DisplayName,
                    version = m.Module.Version,
                    priority = m.Module.Priority,
                    loadedAt = m.LoadedAt.ToString("o"),
                    enabled = loader.IsModuleEnabled(m.Module.ModuleId),
                    hasSettingsUI = (m.Module as IModuleUIProvider)?.HasSettingsUI ?? false,
                    hasQuickLinks = (m.Module as IModuleUIProvider)?.HasQuickLinks ?? false,
                    linkEntries = (m.Module as IModuleUIProvider)?.GetQuickLinks()
                        ?.Select(l => new
                        {
                            id = l.Id,
                            title = l.Title,
                            icon = l.Icon,
                            svg = l.Svg,
                            backgroundColor = l.BackgroundColor,
                            iconColor = l.IconColor
                        })
                        .Cast<object>()
                        ?? Enumerable.Empty<object>()
                });
                return Results.Json(modules);
            });

            // Get module UI
            endpoints.MapGet("/api/modules/{id}/ui", (string id) =>
            {
                var provider = _moduleUIHandler?.GetUIProvider(id);
                if (provider == null) return Results.NotFound();
                var tree = provider.BuildUI();
                tree?.FinalizeSources();
                return Results.Json(tree);
            });

            // Get module link UI
            endpoints.MapGet("/api/modules/{id}/link/{linkId}", (string id, string linkId) =>
            {
                var provider = _moduleUIHandler?.GetUIProvider(id);
                if (provider == null) return Results.NotFound();
                var tree = provider.BuildLinkUI(linkId);
                tree?.FinalizeSources();
                return tree != null ? Results.Json(tree) : Results.NotFound();
            });

            // Get module settings UI
            endpoints.MapGet("/api/modules/{id}/settings", (string id) =>
            {
                var provider = _moduleUIHandler?.GetUIProvider(id);
                if (provider == null) return Results.NotFound();
                var tree = provider.BuildSettingsUI();
                tree?.FinalizeSources();
                return tree != null ? Results.Json(tree) : Results.NotFound();
            });

            // Enable/disable module
            endpoints.MapPost("/api/modules/{id}", async (string id, HttpContext ctx) =>
            {
                var loader = _moduleUIHandler?.ModuleLoader;
                if (loader == null) return Results.Json(new { error = "Module loader not available" }, statusCode: 503);
                using var reader = new StreamReader(ctx.Request.Body);
                var json = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("enabled", out var enabledProp) && enabledProp.ValueKind == JsonValueKind.True || enabledProp.ValueKind == JsonValueKind.False)
                {
                    loader.SetModuleEnabled(id, enabledProp.GetBoolean());
                    return Results.Ok(new { success = true });
                }
                return Results.BadRequest(new { error = "Missing 'enabled' field" });
            });

            // Track cover proxy
            endpoints.MapGet("/api/track/cover", async (string uuid) =>
            {
                if (string.IsNullOrEmpty(uuid)) return Results.BadRequest();
                var (data, mimeType) = await GetCoverAsync(uuid);
                return data != null ? Results.Bytes(data, mimeType ?? "image/jpeg") : Results.NotFound();
            });

            // Module raw content — modules serve their own binary data (QR codes, etc.)
            endpoints.MapGet("/api/modules/{id}/content/{*path}", async (string id, string path) =>
            {
                var module = ModuleLoader.Instance?.GetModule(id);
                if (module is IModuleUIProvider uiProvider)
                {
                    var contentTask = uiProvider.ServeRawContent(path ?? "");
                    if (contentTask != null)
                    {
                        var content = await contentTask;
                        if (content != null)
                        {
                            var contentType = uiProvider.ServeRawContentType(path ?? "") ?? "application/octet-stream";
                            return Results.Bytes(content, contentType);
                        }
                    }
                }
                _logger.LogWarning("content: module {Id} path {Path} not found", id, path);
                return Results.NotFound();
            });

            // Image proxy — proxies base64-encoded image URLs
            endpoints.MapGet("/api/proxy/image", async (string url) =>
            {
                try
                {
                    if (string.IsNullOrEmpty(url)) return Results.BadRequest();
                    _logger.LogInformation("Image proxy: raw query url={RawUrl}", url);
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(Uri.UnescapeDataString(url)));
                    _logger.LogInformation("Image proxy: decoded url={DecodedUrl}", decoded);
                    using var http = new System.Net.Http.HttpClient();
                    var response = await http.GetAsync(decoded);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Image proxy: HTTP {StatusCode} for {Url}", response.StatusCode, decoded);
                        return Results.NotFound();
                    }
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var mime = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                    _logger.LogInformation("Image proxy: OK {Mime} {Length} bytes", mime, bytes.Length);
                    return Results.Bytes(bytes, mime);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Image proxy: Failed for {Url}", url);
                    return Results.NotFound();
                }
            });
        }

        private async Task HandleWebSocket(WebSocket ws)
        {
            lock (_wsLock) _wsClients.Add(ws);
            _wsSendLocks[ws] = new SemaphoreSlim(1, 1);

            try
            {
                var buffer = new byte[4096];
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        if (_moduleUIHandler != null)
                            await _moduleUIHandler.HandleUiEvent(msg);
                    }
                }
            }
            catch (WebSocketException) { }
            finally
            {
                lock (_wsLock) _wsClients.Remove(ws);
                if (_wsSendLocks.TryRemove(ws, out var sem)) sem.Dispose();
                if (ws.State == WebSocketState.Open)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                    catch { }
                }
                ws.Dispose();
            }
        }

        public async Task BroadcastProtoEvent(ProtoEvents.WsEvent evt)
        {
            var bytes = evt.ToByteArray();

            List<WebSocket> clients;
            lock (_wsLock) clients = _wsClients.ToList();

            foreach (var ws in clients)
            {
                if (ws.State != WebSocketState.Open) continue;
                if (_wsSendLocks.TryGetValue(ws, out var sem))
                {
                    await sem.WaitAsync();
                    try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, CancellationToken.None); }
                    catch { }
                    finally { sem.Release(); }
                }
            }
        }

        public void RequestBackendStop()
        {
            _ = BroadcastProtoEvent(new ProtoEvents.WsEvent
            {
                Type = "backend.state.changed",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                BackendState = new ProtoEvents.BackendStateEvent { Running = false }
            });
            _ = Task.Run(async () => { await Task.Delay(500); Environment.Exit(0); });
        }

        /// <summary>For UI push and other non-proto events, still use JSON text.</summary>
        public async Task BroadcastJsonEvent(string eventType, object data)
        {
            var payload = JsonSerializer.Serialize(new { type = eventType, data, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            var bytes = Encoding.UTF8.GetBytes(payload);

            List<WebSocket> clients;
            lock (_wsLock) clients = _wsClients.ToList();

            foreach (var ws in clients)
            {
                if (ws.State != WebSocketState.Open) continue;
                if (_wsSendLocks.TryGetValue(ws, out var sem))
                {
                    await sem.WaitAsync();
                    try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None); }
                    catch { }
                    finally { sem.Release(); }
                }
            }
        }

        private IResult Safe(Func<IResult> action)
        {
            try { return action(); }
            catch (RpcException ex) { return Results.Json(new { error = ex.Status.Detail }, statusCode: ToHttpStatus(ex.StatusCode)); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "REST facade request failed");
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }

        private async Task<IResult> SafeAsync(Func<Task<IResult>> action)
        {
            try { return await action(); }
            catch (RpcException ex) { return Results.Json(new { error = ex.Status.Detail }, statusCode: ToHttpStatus(ex.StatusCode)); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "REST facade request failed");
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }

        private static int ToHttpStatus(StatusCode code) => code switch
        {
            StatusCode.NotFound => 404,
            StatusCode.InvalidArgument => 400,
            StatusCode.FailedPrecondition => 409,
            StatusCode.Unavailable => 409,
            _ => 500
        };

        private PlaybackController GetControllerOrThrow(string id)
        {
            EnsureInstanceExists(id);
            var controller = _sessions.GetController(id);
            if (controller == null)
                throw new RpcException(new Status(StatusCode.Unavailable, "Instance not online"));
            return controller;
        }

        private void EnsureInstanceExists(string id)
        {
            if (_registry.Get(id) == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Instance not found"));
        }

        private InstanceCapabilities GetCapabilities(string id)
            => InstanceCapabilityPolicy.Get(_registry, id);

        private PlaybackStatus GetPlaybackStatusOrFallback(string id)
        {
            var status = _sessions.GetPlaybackStatus(id);
            if (status != null) return status;

            var profile = _registry.Get(id);
            if (profile == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Instance not found"));

            var timeline = _timeline.Get(id);
            var fallback = new PlaybackStatus
            {
                Shuffle = timeline.Shuffle,
                RepeatMode = timeline.RepeatMode,
                Volume = profile.Volume
            };
            if (!string.IsNullOrWhiteSpace(timeline.CurrentUuid))
            {
                var track = _libraryRegistry.GetTrack(timeline.CurrentUuid);
                fallback.TrackUuid = timeline.CurrentUuid;
                fallback.Title = track?.Title ?? "";
                fallback.Artist = track?.Artist ?? "";
                fallback.AlbumId = track?.AlbumId ?? "";
                fallback.Duration = track?.Duration ?? 0;
                fallback.Position = 0;
            }
            return fallback;
        }

        private void RunPlaybackCommand(string id, string command, JsonElement body)
        {
            var caps = GetCapabilities(id);
            var controller = GetControllerOrThrow(id);
            switch (command)
            {
                case "play":
                    InstanceCapabilityPolicy.RequireAudioPlayback(caps, "play");
                    controller.Play(GetString(body, "uuid"));
                    break;
                case "pause":
                    InstanceCapabilityPolicy.RequireServerPlayback(caps, "pause");
                    controller.Pause();
                    break;
                case "resume":
                    InstanceCapabilityPolicy.RequireServerPlayback(caps, "resume");
                    controller.Resume();
                    break;
                case "toggle":
                    InstanceCapabilityPolicy.RequireServerPlayback(caps, "toggle");
                    controller.Toggle();
                    break;
                case "next":
                    InstanceCapabilityPolicy.RequireServerPlayback(caps, "next");
                    controller.Next();
                    break;
                case "prev":
                case "previous":
                    InstanceCapabilityPolicy.RequireServerPlayback(caps, "previous");
                    controller.Prev();
                    break;
                case "seek":
                    InstanceCapabilityPolicy.RequireSeek(caps, "seek");
                    controller.Seek(GetFloat(body, "position") ?? 0);
                    break;
                case "stop":
                    InstanceCapabilityPolicy.RequireServerPlayback(caps, "stop");
                    controller.Stop();
                    break;
                case "volume":
                    InstanceCapabilityPolicy.RequireVolumeControl(caps, "setVolume");
                    _registry.SaveVolume(id, Math.Clamp(GetFloat(body, "volume") ?? controller.Volume, 0f, 1f));
                    break;
                case "shuffle":
                    InstanceCapabilityPolicy.RequireShuffle(caps, "setShuffle");
                    _timeline.SetShuffle(id, GetBool(body, "enabled") ?? !controller.Shuffle);
                    break;
                case "repeat":
                    InstanceCapabilityPolicy.RequireRepeat(caps, "setRepeatMode");
                    _timeline.SetRepeatMode(id, ParseRepeatMode(GetString(body, "mode")));
                    break;
                default:
                    throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unsupported playback command: {command}"));
            }
        }

        private IReadOnlyList<object> GetQueue(string id)
        {
            EnsureInstanceExists(id);
            return _timeline.Get(id).ManualQueueUuids.Select((uuid, index) => ToQueueTrackDto(uuid, index)).ToList();
        }

        private IReadOnlyList<object> GetHistory(string id)
        {
            EnsureInstanceExists(id);
            return _timeline.Get(id).HistoryUuids.Select((uuid, index) => ToQueueTrackDto(uuid, index)).ToList();
        }

        private object ToQueueTrackDto(string uuid, int index)
        {
            var track = _libraryRegistry.GetTrack(uuid);
            return new
            {
                index,
                uuid = uuid ?? "",
                title = track?.Title ?? "",
                artist = track?.Artist ?? "",
                albumId = track?.AlbumId ?? "",
                duration = track?.Duration ?? 0,
                moduleId = track?.ModuleId ?? "",
                coverUri = track?.CoverUri ?? ""
            };
        }

        private object ToInstanceDto(InstanceSummary summary)
        {
            var profile = _registry.Get(summary.Id);
            return new
            {
                id = summary.Id,
                displayName = summary.DisplayName,
                kind = summary.Kind.ToString(),
                isOnline = summary.IsOnline,
                currentTrackUuid = summary.CurrentTrackUuid,
                queueCount = summary.QueueCount,
                modId = summary.ModId,
                gameName = summary.GameName,
                connectedAt = ToTimestampDto(summary.ConnectedAt),
                capabilities = ToCapabilitiesDto(profile?.Capabilities),
                status = ToPlaybackStatusDto(GetPlaybackStatusOrFallback(summary.Id))
            };
        }

        private static object ToCapabilitiesDto(InstanceCapabilities caps) => caps == null ? null : new
        {
            serverControlledPlayback = caps.ServerControlledPlayback,
            queueManagement = caps.QueueManagement,
            playlistManagement = caps.PlaylistManagement,
            multiplePlaylists = caps.MultiplePlaylists,
            tagFiltering = caps.TagFiltering,
            albumFiltering = caps.AlbumFiltering,
            shuffle = caps.Shuffle,
            repeat = caps.Repeat,
            seek = caps.Seek,
            volumeControl = caps.VolumeControl,
            equalizer = caps.Equalizer,
            audioPlayback = caps.AudioPlayback
        };

        private static object ToPlaybackStatusDto(PlaybackStatus status) => new
        {
            trackUuid = status.TrackUuid,
            title = status.Title,
            artist = status.Artist,
            albumId = status.AlbumId,
            duration = status.Duration,
            position = status.Position,
            isPlaying = status.IsPlaying,
            shuffle = status.Shuffle,
            repeatMode = RepeatModeToString(status.RepeatMode),
            volume = status.Volume
        };

        private static object ToTrackDto(Track track) => new
        {
            uuid = track.Uuid,
            title = track.Title,
            artist = track.Artist,
            albumId = track.AlbumId,
            duration = track.Duration,
            moduleId = track.ModuleId,
            sourceType = track.SourceType.ToString(),
            sourcePath = track.SourcePath,
            isFavorite = track.IsFavorite,
            isExcluded = track.IsExcluded,
            coverUri = track.CoverUri,
            playCount = track.PlayCount,
            createdAt = ToTimestampDto(track.CreatedAt),
            lastPlayedAt = ToTimestampDto(track.LastPlayedAt)
        };

        private static object ToPlaylistDto(Playlist playlist) => new
        {
            id = playlist.Id,
            name = playlist.Name,
            moduleId = playlist.ModuleId,
            kind = playlist.Kind.ToString(),
            coverUri = playlist.CoverUri,
            sortOrder = playlist.SortOrder,
            createdAt = ToTimestampDto(playlist.CreatedAt),
            updatedAt = ToTimestampDto(playlist.UpdatedAt)
        };

        private static object ToPlaylistWithEntriesDto(PlaylistWithEntries playlist) => new
        {
            playlistId = playlist.PlaylistId,
            playlistName = playlist.PlaylistName,
            entries = playlist.Entries.OrderBy(e => e.Position).Select(e => new
            {
                entryId = e.EntryId,
                trackUuid = e.TrackUuid,
                title = e.Title,
                artist = e.Artist,
                duration = e.Duration,
                albumId = e.AlbumId,
                coverUri = e.CoverUri,
                position = e.Position
            })
        };

        private static object ToPlaylistSourceDto(PlaylistSourceInfo source) => new
        {
            id = source.Id,
            name = source.Name,
            songCount = source.SongCount,
            kind = PlaylistSourceKindToString(source.Kind),
            refId = source.RefId
        };

        private static object ToTimestampDto(OmniTimestamp timestamp)
            => timestamp == null ? null : new { seconds = timestamp.Seconds, nanos = timestamp.Nanos };

        private TrackQuery BuildTrackQuery(HttpContext ctx)
        {
            var q = ctx.Request.Query;
            var query = new TrackQuery
            {
                Text = q.TryGetValue("text", out var text) ? text.ToString() : "",
                AlbumId = q.TryGetValue("albumId", out var albumId) ? albumId.ToString() : "",
                PlaylistId = q.TryGetValue("playlistId", out var playlistId) ? playlistId.ToString() : "",
                ModuleId = q.TryGetValue("moduleId", out var moduleId) ? moduleId.ToString() : "",
                Offset = q.TryGetValue("offset", out var offset) && int.TryParse(offset, out var offsetValue) ? Math.Max(0, offsetValue) : 0,
                Limit = q.TryGetValue("limit", out var limit) && int.TryParse(limit, out var limitValue) ? Math.Max(0, limitValue) : 80
            };
            if (q.TryGetValue("favorite", out var fav) && bool.TryParse(fav, out var favorite)) query.IsFavorite = favorite;
            if (q.TryGetValue("excluded", out var excl) && bool.TryParse(excl, out var excluded)) query.IsExcluded = excluded;
            if (q.TryGetValue("tagId", out var tagId) && !string.IsNullOrWhiteSpace(tagId))
                query.TagIds.Add(tagId.ToString());
            return query;
        }

        private static PlaylistQuery BuildPlaylistQuery(HttpContext ctx)
        {
            var q = ctx.Request.Query;
            return new PlaylistQuery
            {
                ModuleId = q.TryGetValue("moduleId", out var moduleId) ? moduleId.ToString() : "",
                Offset = q.TryGetValue("offset", out var offset) && int.TryParse(offset, out var offsetValue) ? Math.Max(0, offsetValue) : 0,
                Limit = q.TryGetValue("limit", out var limit) && int.TryParse(limit, out var limitValue) ? Math.Max(0, limitValue) : 80
            };
        }

        private static async Task<JsonElement> ReadJsonRootAsync(HttpContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.Clone();
        }

        private static string GetString(JsonElement root, string name, string fallback = "")
            => root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty(name, out var prop)
               && prop.ValueKind == JsonValueKind.String
                ? prop.GetString() ?? fallback
                : fallback;

        private static bool? GetBool(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var prop)) return null;
            return prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        private static int? GetInt(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var prop)) return null;
            return prop.TryGetInt32(out var value) ? value : null;
        }

        private static float? GetFloat(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var prop)) return null;
            return prop.TryGetSingle(out var value) ? value : null;
        }

        private static string[] GetStringArray(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            return prop.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        private static PlaylistSourceRequest GetPlaylistSourceRequest(JsonElement root)
        {
            var sourceRoot = root;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("source", out var source))
                sourceRoot = source;

            var kind = ParsePlaylistSourceKind(GetString(sourceRoot, "kind", "track"));
            var refId = GetString(sourceRoot, "refId");
            var explicitId = GetString(sourceRoot, "id");
            if (string.IsNullOrWhiteSpace(refId))
                refId = GetString(sourceRoot, "playlistId", GetString(sourceRoot, "trackUuid"));
            if (string.IsNullOrWhiteSpace(explicitId))
                explicitId = $"{PlaylistSourceKindToString(kind)}_{refId}";

            return new PlaylistSourceRequest
            {
                id = explicitId,
                name = GetString(sourceRoot, "name", explicitId),
                kind = kind,
                refId = refId,
                uuids = GetStringArray(sourceRoot, "uuids")
            };
        }

        private static IEnumerable<PlaylistSourceRequest> GetPlaylistSourceRequests(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
                return Enumerable.Empty<PlaylistSourceRequest>();
            return sources.EnumerateArray().Select(GetPlaylistSourceRequest).Where(s => !string.IsNullOrWhiteSpace(s.id)).ToList();
        }

        private static RepeatMode ParseRepeatMode(string mode) => (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "one" or "repeatone" => RepeatMode.One,
            "all" or "repeatall" => RepeatMode.All,
            "none" or "off" => RepeatMode.None,
            _ => RepeatMode.None
        };

        private static string RepeatModeToString(RepeatMode mode) => mode switch
        {
            RepeatMode.One => "one",
            RepeatMode.All => "all",
            _ => "none"
        };

        private static PlaylistSourceKind ParsePlaylistSourceKind(string kind) => (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "tag" => PlaylistSourceKind.Tag,
            "album" => PlaylistSourceKind.Album,
            "playlist" => PlaylistSourceKind.Playlist,
            "track" => PlaylistSourceKind.Track,
            _ => PlaylistSourceKind.Track
        };

        private static string PlaylistSourceKindToString(PlaylistSourceKind kind) => kind switch
        {
            PlaylistSourceKind.Tag => "tag",
            PlaylistSourceKind.Album => "album",
            PlaylistSourceKind.Playlist => "playlist",
            PlaylistSourceKind.Track => "track",
            _ => "unspecified"
        };

        private static object ConvertElement(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.Number when el.TryGetDouble(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => el.EnumerateArray().Select(ConvertElement).ToList(),
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => ConvertElement(p.Value)),
            _ => null
        };

        internal string GetClientId(HttpContext ctx) => ctx.Connection.Id;

        public async Task<(byte[] data, string mimeType)> GetCoverAsync(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return (null, null);

            var track = _libraryRegistry.GetTrack(uuid);
            if (track == null || string.IsNullOrEmpty(track.ModuleId)) return (null, null);

            var provider = ModuleLoader.Instance?.GetProvider<ICoverProvider>(track.ModuleId);
            if (provider == null) return (null, null);

            try
            {
                return await provider.GetMusicCoverAsync(uuid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get cover for uuid {Uuid}", uuid);
                return (null, null);
            }
        }
    }
}
