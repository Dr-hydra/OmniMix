<script lang="ts">
  import { onMount } from "svelte";
  import packageInfo from "../package.json";
  import RawNode from "./lib/RawNode.svelte";
  import { ApiClient } from "./lib/api";
  import type {
    AppConfig,
    ModuleInfo,
    PlaybackInstance,
    PlaybackStatus,
    PlaylistInfo,
    PlaylistSourceInfo,
    PlaylistWithEntries,
    QueueTrack,
    RawNodeData,
    TrackInfo,
    UiKind,
    UiPushPayload,
    VersionResponse,
    WsJsonEnvelope
  } from "./lib/types";
  import { formatDateTime, stringifyValue } from "./lib/util";

  const api = new ApiClient();
  const webUiVersion = packageInfo.version;

  type ViewMode = "player" | "library" | "modules";

  let view: ViewMode = "player";
  let healthState: "checking" | "online" | "offline" = "checking";
  let wsState: "open" | "closed" | "error" | "binary" = "closed";
  let backendVersion: VersionResponse | null = null;
  let config: AppConfig = {};
  let modules: ModuleInfo[] = [];
  let instances: PlaybackInstance[] = [];
  let selectedInstanceId = "";
  let playbackStatus: PlaybackStatus | null = null;
  let queue: QueueTrack[] = [];
  let history: QueueTrack[] = [];
  let playlistSources: PlaylistSourceInfo[] = [];
  let tracks: TrackInfo[] = [];
  let playlists: PlaylistInfo[] = [];
  let selectedPlaylistId = "";
  let selectedPlaylist: PlaylistWithEntries | null = null;
  let librarySearch = "";
  let libraryModuleId = "";
  let selectedModuleId = "";
  let selectedUiKind: UiKind = "default";
  let selectedLinkId = "";
  let selectedUi: RawNodeData | null = null;
  let busy = false;
  let message = "";
  let socket: WebSocket | null = null;

  $: selectedInstance = instances.find((item) => item.id === selectedInstanceId) ?? null;
  $: selectedModule = modules.find((item) => item.id === selectedModuleId) ?? null;
  $: enabledCount = modules.filter((item) => item.enabled).length;
  $: configRows = Object.entries(config).slice(0, 10);
  $: pageTitle = view === "player" ? "播放控制" : view === "library" ? "曲库" : selectedModule ? selectedModule.name : "模块";
  $: currentTrackTitle = playbackStatus?.title || "未播放";
  $: currentTrackSub = [playbackStatus?.artist, playbackStatus?.albumId].filter(Boolean).join(" / ") || "等待播放实例";
  $: djEnabled = configBoolean(config["fh6_dj_enabled"]);
  $: djScopeText = configString(config["fh6_dj_scope"]) === "desktop_instances" ? "桌面实例" : "FH6 实例";

  onMount(() => {
    void refreshAll();
    connectWs();
    const timer = window.setInterval(() => void refreshPlayback(false), 4000);
    const configTimer = window.setInterval(() => void refreshConfig(false), 4000);
    return () => {
      window.clearInterval(timer);
      window.clearInterval(configTimer);
      socket?.close();
    };
  });

  async function refreshAll() {
    busy = true;
    message = "";
    try {
      await Promise.all([refreshHealth(), refreshModules(), refreshConfig(), refreshPlayback(false), refreshLibrary(false)]);
    } catch (error) {
      message = error instanceof Error ? error.message : String(error);
    } finally {
      busy = false;
    }
  }

  async function refreshHealth() {
    try {
      await api.health();
      backendVersion = await api.version();
      healthState = "online";
    } catch {
      healthState = "offline";
    }
  }

  async function refreshModules() {
    modules = await api.modules();
    if (!selectedModuleId && modules.length > 0) {
      selectedModuleId = modules[0].id;
      await loadModuleUi(selectedModuleId, "default", "");
    }
  }

  async function refreshConfig(showErrors = true) {
    try {
      config = await api.config();
    } catch (error) {
      if (showErrors) message = error instanceof Error ? error.message : String(error);
    }
  }

  function pickPreferredInstanceId(nextInstances: PlaybackInstance[]) {
    return (
      nextInstances.find((item) => item.isOnline && item.capabilities?.audioPlayback)?.id ??
      nextInstances.find((item) => item.isOnline)?.id ??
      nextInstances[0]?.id ??
      ""
    );
  }

  function clearPlaybackDetails() {
    playbackStatus = null;
    queue = [];
    history = [];
    playlistSources = [];
  }

  async function refreshPlayback(showErrors = true) {
    try {
      const nextInstances = await api.instances();
      instances = nextInstances;
      if (!selectedInstanceId || !nextInstances.some((item) => item.id === selectedInstanceId)) {
        selectedInstanceId = pickPreferredInstanceId(nextInstances);
      }

      const selected = nextInstances.find((item) => item.id === selectedInstanceId) ?? null;
      playbackStatus = selected?.status ?? null;
      if (selectedInstanceId) {
        try {
          const [freshStatus, freshQueue, freshHistory, freshSources] = await Promise.all([
            api.playbackStatus(selectedInstanceId),
            api.queue(selectedInstanceId),
            api.history(selectedInstanceId),
            api.playlistSources(selectedInstanceId)
          ]);
          playbackStatus = freshStatus;
          queue = freshQueue;
          history = freshHistory;
          playlistSources = freshSources;
        } catch (error) {
          if (error instanceof Error && error.message === "HTTP 404") {
            const latestInstances = await api.instances();
            instances = latestInstances;
            if (!latestInstances.some((item) => item.id === selectedInstanceId)) {
              selectedInstanceId = pickPreferredInstanceId(latestInstances);
              playbackStatus = latestInstances.find((item) => item.id === selectedInstanceId)?.status ?? null;
              clearPlaybackDetails();
              return;
            }
          }
          throw error;
        }
      } else {
        clearPlaybackDetails();
      }
    } catch (error) {
      if (showErrors) message = error instanceof Error ? error.message : String(error);
    }
  }

  async function refreshLibrary(showErrors = true) {
    try {
      const [trackResponse, playlistResponse] = await Promise.all([
        api.tracks({ text: librarySearch.trim(), moduleId: libraryModuleId, limit: 120 }),
        api.playlists(libraryModuleId)
      ]);
      tracks = trackResponse.tracks;
      playlists = playlistResponse.playlists;
      if (!selectedPlaylistId || !playlists.some((item) => item.id === selectedPlaylistId)) {
        selectedPlaylistId = playlists[0]?.id ?? "";
      }
      if (selectedPlaylistId) {
        selectedPlaylist = await api.playlist(selectedPlaylistId);
      } else {
        selectedPlaylist = null;
      }
    } catch (error) {
      if (showErrors) message = error instanceof Error ? error.message : String(error);
    }
  }

  async function selectInstance(instanceId: string) {
    selectedInstanceId = instanceId;
    await refreshPlayback();
  }

  async function selectPlaylist(playlistId: string) {
    selectedPlaylistId = playlistId;
    selectedPlaylist = playlistId ? await api.playlist(playlistId) : null;
  }

  async function selectModule(moduleId: string) {
    selectedModuleId = moduleId;
    selectedUiKind = "default";
    selectedLinkId = "";
    await loadModuleUi(moduleId, "default", "");
  }

  async function loadModuleUi(moduleId: string, kind: UiKind, linkId = "") {
    selectedUiKind = kind;
    selectedLinkId = linkId;
    selectedUi = null;
    try {
      selectedUi = await api.moduleUi(moduleId, kind, linkId);
    } catch (error) {
      selectedUi = {
        "node-type": "Container",
        direction: "Vertical",
        spacing: 8,
        children: [
          { "node-type": "Text", text: "模块界面不可用", "font-size": 15, color: "#dc2626" },
          { "node-type": "Text", text: error instanceof Error ? error.message : String(error), "font-size": 12, color: "#64748b" }
        ]
      };
    }
  }

  async function runAction(action: () => Promise<void>) {
    busy = true;
    message = "";
    try {
      await action();
    } catch (error) {
      message = error instanceof Error ? error.message : String(error);
    } finally {
      busy = false;
    }
  }

  function requireInstance(): string {
    if (!selectedInstanceId) throw new Error("没有可用播放实例");
    return selectedInstanceId;
  }

  async function playback(command: string, extra: Record<string, unknown> = {}) {
    const instanceId = requireInstance();
    await api.playbackCommand(instanceId, { command, ...extra });
    await refreshPlayback();
  }

  async function toggleDj() {
    await runAction(async () => {
      await api.updateConfig({ fh6_dj_enabled: !djEnabled });
      await api.saveConfig();
      await refreshConfig();
    });
  }

  async function playTrack(uuid: string) {
    await runAction(async () => playback("play", { uuid }));
  }

  async function queueTrack(uuid: string) {
    await runAction(async () => {
      queue = await api.addToQueue(requireInstance(), uuid);
      await refreshPlayback(false);
    });
  }

  async function queuePlaylistEntries() {
    if (!selectedPlaylist) return;
    await runAction(async () => {
      queue = await api.addManyToQueue(requireInstance(), selectedPlaylist.entries.map((item) => item.trackUuid));
      await refreshPlayback(false);
    });
  }

  async function usePlaylistAsSource() {
    if (!selectedPlaylist) return;
    await runAction(async () => {
      playlistSources = await api.addPlaylistSource(requireInstance(), {
        id: `playlist_${selectedPlaylist!.playlistId}`,
        name: selectedPlaylist!.playlistName,
        kind: "playlist",
        refId: selectedPlaylist!.playlistId
      });
      await refreshPlayback(false);
    });
  }

  async function removeSource(sourceId: string) {
    await runAction(async () => {
      playlistSources = await api.removePlaylistSource(requireInstance(), sourceId);
      await refreshPlayback(false);
    });
  }

  async function removeQueue(index: number) {
    await runAction(async () => {
      queue = await api.removeQueueItem(requireInstance(), index);
      await refreshPlayback(false);
    });
  }

  async function moveQueue(index: number, delta: number) {
    await runAction(async () => {
      queue = await api.moveQueueItem(requireInstance(), index, Math.max(0, index + delta));
      await refreshPlayback(false);
    });
  }

  async function clearQueue() {
    await runAction(async () => {
      queue = await api.clearQueue(requireInstance());
      await refreshPlayback(false);
    });
  }

  async function toggleModule(module: ModuleInfo) {
    await api.setModuleEnabled(module.id, !module.enabled);
    await refreshModules();
  }

  async function dispatchModuleEvent(
    moduleId: string,
    nodeId: string,
    action: string,
    value: string,
    uiKind: UiKind,
    linkId: string
  ) {
    await api.sendUiEvent(moduleId, nodeId, action, value, uiKind, linkId);
    setTimeout(() => void loadModuleUi(moduleId, uiKind, linkId), 160);
  }

  async function stopBackend() {
    await api.stopBackend();
    healthState = "offline";
  }

  function handleWsJson(messageJson: WsJsonEnvelope) {
    if (messageJson.type !== "ui_push") return;
    const payload = messageJson.data as UiPushPayload | undefined;
    if (!payload || payload.moduleId !== selectedModuleId || !payload.tree) return;
    selectedUi = payload.tree;
  }

  function connectWs() {
    socket?.close();
    socket = api.openEvents(
      handleWsJson,
      (state) => {
        wsState = state;
        if (state === "closed") {
          setTimeout(connectWs, 1800);
        }
      },
      () => void refreshPlayback(false)
    );
  }

  function formatDuration(value?: number) {
    const total = Math.max(0, Math.floor(value ?? 0));
    const minutes = Math.floor(total / 60);
    const seconds = total % 60;
    return `${minutes}:${seconds.toString().padStart(2, "0")}`;
  }

  function nextRepeatMode(mode: PlaybackStatus["repeatMode"] | undefined) {
    if (mode === "none") return "one";
    if (mode === "one") return "all";
    return "none";
  }

  function configBoolean(value: unknown): boolean {
    if (typeof value === "boolean") return value;
    if (typeof value === "string") return value.toLowerCase() === "true";
    return false;
  }

  function configString(value: unknown): string {
    return typeof value === "string" ? value : "";
  }
</script>

<div class="shell">
  <aside class="sidebar">
    <div class="brand">
      <div class="brand-mark">OM</div>
      <div>
        <h1>OmniMix</h1>
        <p>WebUI {webUiVersion}</p>
      </div>
    </div>

    <div class="status-grid">
      <div>
        <span>后端</span>
        <strong class={healthState}>{healthState === "online" ? "在线" : healthState === "offline" ? "离线" : "检测中"}</strong>
      </div>
      <div>
        <span>事件</span>
        <strong class={wsState === "open" ? "online" : "muted"}>{wsState === "open" ? "已连接" : "等待"}</strong>
      </div>
    </div>

    <nav class="view-nav" aria-label="主功能">
      <button type="button" class:active={view === "player"} on:click={() => (view = "player")}>播放</button>
      <button type="button" class:active={view === "library"} on:click={() => (view = "library")}>曲库</button>
      <button type="button" class:active={view === "modules"} on:click={() => (view = "modules")}>模块</button>
    </nav>

    <div class="sidebar-section">
      <span>播放实例</span>
      <div class="instance-list">
        {#each instances as instance}
          <button type="button" class:selected={instance.id === selectedInstanceId} on:click={() => selectInstance(instance.id)}>
            <strong>{instance.displayName || instance.id}</strong>
            <small>{instance.isOnline ? "在线" : "离线"} · {instance.queueCount} 首</small>
          </button>
        {/each}
        {#if instances.length === 0}
          <div class="empty dark">暂无实例</div>
        {/if}
      </div>
    </div>
  </aside>

  <main class="main">
    <header class="topbar">
      <div>
        <h2>{pageTitle}</h2>
        <p>{backendVersion ? `${backendVersion.name} ${backendVersion.version}` : "等待后端响应"}</p>
      </div>
      <div class="actions">
        <button type="button" class="ghost" disabled={busy} on:click={refreshAll}>刷新</button>
        <button type="button" class="danger" on:click={stopBackend}>停止后端</button>
      </div>
    </header>

    {#if message}
      <div class="notice">{message}</div>
    {/if}

    <section class="overview">
      <div class="metric">
        <span>实例</span>
        <strong>{instances.filter((item) => item.isOnline).length}/{instances.length}</strong>
      </div>
      <div class="metric">
        <span>队列</span>
        <strong>{queue.length}</strong>
      </div>
      <div class="metric">
        <span>曲库</span>
        <strong>{tracks.length}</strong>
      </div>
      <div class="metric">
        <span>模块</span>
        <strong>{enabledCount}/{modules.length}</strong>
      </div>
    </section>

    {#if view === "player"}
      <section class="player-grid">
        <div class="panel now-panel">
          <div class="panel-head">
            <div>
              <h3>{currentTrackTitle}</h3>
              <p>{currentTrackSub}</p>
            </div>
            <div class="now-actions">
              <span class:online={selectedInstance?.isOnline} class="pill">{selectedInstance?.isOnline ? "在线" : "不可播放"}</span>
              <button
                type="button"
                class="dj-toggle"
                class:enabled={djEnabled}
                disabled={busy}
                aria-label={djEnabled ? "关闭 DJ 模式" : "开启 DJ 模式"}
                aria-pressed={djEnabled}
                title={`${djEnabled ? "关闭" : "开启"} DJ 模式 · ${djScopeText}`}
                on:click={toggleDj}
              >
                <span class="dj-mark" aria-hidden="true">DJ</span>
              </button>
            </div>
          </div>

          <div class="transport">
            <div class="transport-row">
              <button type="button" on:click={() => runAction(() => playback("prev"))}>上一首</button>
              <button type="button" class="primary" on:click={() => runAction(() => playback(playbackStatus?.isPlaying ? "pause" : "play"))}>
                {playbackStatus?.isPlaying ? "暂停" : "播放"}
              </button>
              <button type="button" on:click={() => runAction(() => playback("next"))}>下一首</button>
              <button type="button" class="ghost" on:click={() => runAction(() => playback("stop"))}>停止</button>
            </div>

            <label class="slider-row">
              <span>{formatDuration(playbackStatus?.position)}</span>
              <input
                type="range"
                min="0"
                max={Math.max(1, playbackStatus?.duration ?? 1)}
                step="1"
                value={playbackStatus?.position ?? 0}
                on:change={(event) => runAction(() => playback("seek", { position: Number(event.currentTarget.value) }))}
              />
              <span>{formatDuration(playbackStatus?.duration)}</span>
            </label>

            <label class="slider-row volume">
              <span>音量</span>
              <input
                type="range"
                min="0"
                max="1"
                step="0.01"
                value={playbackStatus?.volume ?? 0}
                on:change={(event) => runAction(() => playback("volume", { volume: Number(event.currentTarget.value) }))}
              />
              <strong>{Math.round((playbackStatus?.volume ?? 0) * 100)}%</strong>
            </label>

            <div class="transport-row">
              <button type="button" class:active={playbackStatus?.shuffle} on:click={() => runAction(() => playback("shuffle", { enabled: !playbackStatus?.shuffle }))}>
                随机 {playbackStatus?.shuffle ? "开" : "关"}
              </button>
              <button type="button" on:click={() => runAction(() => playback("repeat", { mode: nextRepeatMode(playbackStatus?.repeatMode) }))}>
                循环 {playbackStatus?.repeatMode === "one" ? "单曲" : playbackStatus?.repeatMode === "all" ? "全部" : "关闭"}
              </button>
            </div>
          </div>
        </div>

        <div class="panel source-panel">
          <div class="panel-head compact-head">
            <div>
              <h3>播放列表源</h3>
              <p>{playlistSources.length} 项</p>
            </div>
          </div>
          <div class="source-list">
            {#each playlistSources as source}
              <div>
                <strong>{source.name || source.id}</strong>
                <span>{source.kind} · {source.songCount} 首</span>
                <button type="button" class="ghost" on:click={() => removeSource(source.id)}>移除</button>
              </div>
            {/each}
            {#if playlistSources.length === 0}
              <div class="empty small">未设置播放列表源</div>
            {/if}
          </div>
        </div>

        <div class="panel queue-panel">
          <div class="panel-head compact-head">
            <div>
              <h3>播放队列</h3>
              <p>{queue.length} 首待播</p>
            </div>
            <button type="button" class="ghost" on:click={clearQueue}>清空</button>
          </div>
          <div class="track-list">
            {#each queue as item}
              <div class="track-row">
                <div>
                  <strong>{item.title || item.uuid}</strong>
                  <span>{item.artist || item.moduleId} · {formatDuration(item.duration)}</span>
                </div>
                <div class="row-actions">
                  <button type="button" class="ghost" on:click={() => moveQueue(item.index, -1)}>上移</button>
                  <button type="button" class="ghost" on:click={() => moveQueue(item.index, 1)}>下移</button>
                  <button type="button" class="ghost" on:click={() => removeQueue(item.index)}>移除</button>
                </div>
              </div>
            {/each}
            {#if queue.length === 0}
              <div class="empty">队列为空</div>
            {/if}
          </div>
        </div>

        <div class="panel history-panel">
          <div class="panel-head compact-head">
            <div>
              <h3>播放历史</h3>
              <p>{history.length} 首</p>
            </div>
          </div>
          <div class="track-list compact">
            {#each history.slice(0, 12) as item}
              <div class="track-row">
                <div>
                  <strong>{item.title || item.uuid}</strong>
                  <span>{item.artist || item.moduleId}</span>
                </div>
              </div>
            {/each}
            {#if history.length === 0}
              <div class="empty small">暂无历史</div>
            {/if}
          </div>
        </div>
      </section>
    {:else if view === "library"}
      <section class="library-grid">
        <div class="panel library-panel">
          <div class="panel-head">
            <div>
              <h3>曲库浏览</h3>
              <p>{tracks.length} 首</p>
            </div>
            <div class="filters">
              <input type="search" placeholder="搜索歌曲或艺人" bind:value={librarySearch} on:keydown={(event) => event.key === "Enter" && refreshLibrary()} />
              <select bind:value={libraryModuleId} on:change={() => refreshLibrary()}>
                <option value="">全部模块</option>
                {#each modules as module}
                  <option value={module.id}>{module.name}</option>
                {/each}
              </select>
              <button type="button" on:click={() => refreshLibrary()}>查询</button>
            </div>
          </div>

          <div class="track-list">
            {#each tracks as track}
              <div class="track-row">
                <div>
                  <strong>{track.title || track.uuid}</strong>
                  <span>{track.artist || "未知艺人"} · {track.moduleId} · {formatDuration(track.duration)}</span>
                </div>
                <div class="row-actions">
                  <button type="button" on:click={() => playTrack(track.uuid)}>播放</button>
                  <button type="button" class="ghost" on:click={() => queueTrack(track.uuid)}>入队</button>
                </div>
              </div>
            {/each}
            {#if tracks.length === 0}
              <div class="empty">暂无歌曲</div>
            {/if}
          </div>
        </div>

        <div class="panel playlist-panel">
          <div class="panel-head compact-head">
            <div>
              <h3>歌单</h3>
              <p>{playlists.length} 个</p>
            </div>
          </div>
          <div class="playlist-list">
            {#each playlists as playlist}
              <button type="button" class:selected={playlist.id === selectedPlaylistId} on:click={() => selectPlaylist(playlist.id)}>
                <strong>{playlist.name || playlist.id}</strong>
                <span>{playlist.moduleId}</span>
              </button>
            {/each}
            {#if playlists.length === 0}
              <div class="empty small">暂无歌单</div>
            {/if}
          </div>
        </div>

        <div class="panel playlist-detail">
          <div class="panel-head">
            <div>
              <h3>{selectedPlaylist?.playlistName ?? "歌单详情"}</h3>
              <p>{selectedPlaylist?.entries.length ?? 0} 首</p>
            </div>
            <div class="actions">
              <button type="button" class="ghost" disabled={!selectedPlaylist} on:click={queuePlaylistEntries}>全部入队</button>
              <button type="button" disabled={!selectedPlaylist} on:click={usePlaylistAsSource}>设为播放源</button>
            </div>
          </div>
          <div class="track-list compact">
            {#each selectedPlaylist?.entries ?? [] as entry}
              <div class="track-row">
                <div>
                  <strong>{entry.title || entry.trackUuid}</strong>
                  <span>{entry.artist || "未知艺人"} · {formatDuration(entry.duration)}</span>
                </div>
                <div class="row-actions">
                  <button type="button" class="ghost" on:click={() => playTrack(entry.trackUuid)}>播放</button>
                  <button type="button" class="ghost" on:click={() => queueTrack(entry.trackUuid)}>入队</button>
                </div>
              </div>
            {/each}
            {#if !selectedPlaylist || selectedPlaylist.entries.length === 0}
              <div class="empty">请选择歌单</div>
            {/if}
          </div>
        </div>
      </section>
    {:else}
      <section class="workbench">
        <div class="panel module-panel">
          {#if selectedModule}
            <div class="panel-head">
              <div>
                <h3>{selectedModule.name}</h3>
                <p>{selectedModule.id}</p>
              </div>
              <button type="button" class:enabled={selectedModule.enabled} class="toggle" on:click={() => toggleModule(selectedModule)}>
                {selectedModule.enabled ? "已启用" : "已停用"}
              </button>
            </div>

            <div class="tabs">
              <button class:active={selectedUiKind === "default"} type="button" on:click={() => loadModuleUi(selectedModule.id, "default")}>界面</button>
              {#if selectedModule.hasSettingsUI}
                <button class:active={selectedUiKind === "settings"} type="button" on:click={() => loadModuleUi(selectedModule.id, "settings")}>设置</button>
              {/if}
              {#each selectedModule.linkEntries ?? [] as link}
                <button
                  class:active={selectedUiKind === "link" && selectedLinkId === link.id}
                  type="button"
                  on:click={() => loadModuleUi(selectedModule.id, "link", link.id)}
                >
                  {link.title}
                </button>
              {/each}
            </div>

            <div class="raw-surface">
              {#if selectedUi}
                <RawNode
                  node={selectedUi}
                  moduleId={selectedModule.id}
                  uiKind={selectedUiKind}
                  linkId={selectedLinkId}
                  dispatchEvent={dispatchModuleEvent}
                />
              {:else}
                <div class="empty">正在加载</div>
              {/if}
            </div>
          {:else}
            <div class="empty">暂无模块</div>
          {/if}
        </div>

        <div class="panel side-panel">
          <div class="panel-head compact-head">
            <div>
              <h3>模块列表</h3>
              <p>{enabledCount}/{modules.length}</p>
            </div>
          </div>
          <nav class="module-nav" aria-label="模块">
            {#each modules as module}
              <button type="button" class:selected={module.id === selectedModuleId} on:click={() => selectModule(module.id)}>
                <span>{module.name || module.id}</span>
                <small>{module.version}</small>
              </button>
            {/each}
          </nav>

          <div class="panel-head compact-head">
            <div>
              <h3>配置摘要</h3>
              <p>{Object.keys(config).length} 项</p>
            </div>
            <button type="button" class="ghost" on:click={() => api.saveConfig()}>保存</button>
          </div>
          <div class="config-list">
            {#each configRows as [key, value]}
              <div>
                <span>{key}</span>
                <strong>{stringifyValue(value)}</strong>
              </div>
            {/each}
            {#if configRows.length === 0}
              <div class="empty small">暂无配置</div>
            {/if}
          </div>
          <div class="panel-foot">
            {#if selectedModule}
              <span>加载时间</span>
              <strong>{formatDateTime(selectedModule.loadedAt)}</strong>
            {/if}
          </div>
        </div>
      </section>
    {/if}
  </main>
</div>
