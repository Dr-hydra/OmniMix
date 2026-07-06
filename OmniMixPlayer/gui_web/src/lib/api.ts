import type {
  AppConfig,
  HealthResponse,
  PlaybackInstance,
  PlaybackStatus,
  PlaylistQueryResponse,
  PlaylistSourceInfo,
  PlaylistWithEntries,
  QueueTrack,
  TrackQueryParams,
  TrackQueryResponse,
  ModuleInfo,
  RawNodeData,
  UiKind,
  VersionResponse,
  WsJsonEnvelope
} from "./types";

type JsonBody = Record<string, unknown>;

async function parseJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }
  return (await response.json()) as T;
}

export class ApiClient {
  async health(): Promise<HealthResponse> {
    return parseJson<HealthResponse>(await fetch("/api/health"));
  }

  async version(): Promise<VersionResponse> {
    return parseJson<VersionResponse>(await fetch("/api/version"));
  }

  async config(): Promise<AppConfig> {
    return parseJson<AppConfig>(await fetch("/api/config"));
  }

  async saveConfig(): Promise<void> {
    await parseJson(await fetch("/api/config/save", { method: "POST" }));
  }

  async stopBackend(): Promise<void> {
    await parseJson(await fetch("/api/backend/stop", { method: "POST" }));
  }

  async modules(): Promise<ModuleInfo[]> {
    return parseJson<ModuleInfo[]>(await fetch("/api/modules"));
  }

  async instances(): Promise<PlaybackInstance[]> {
    return parseJson<PlaybackInstance[]>(await fetch("/api/instances"));
  }

  async playbackStatus(instanceId: string): Promise<PlaybackStatus> {
    return parseJson<PlaybackStatus>(await fetch(`/api/instances/${encodeURIComponent(instanceId)}/status`));
  }

  async playbackCommand(instanceId: string, payload: JsonBody): Promise<void> {
    await parseJson(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/playback`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      })
    );
  }

  async queue(instanceId: string): Promise<QueueTrack[]> {
    const response = await parseJson<{ queue: QueueTrack[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/queue`)
    );
    return response.queue ?? [];
  }

  async addToQueue(instanceId: string, uuid: string): Promise<QueueTrack[]> {
    const response = await parseJson<{ queue: QueueTrack[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/queue`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ uuid })
      })
    );
    return response.queue ?? [];
  }

  async addManyToQueue(instanceId: string, uuids: string[]): Promise<QueueTrack[]> {
    const response = await parseJson<{ queue: QueueTrack[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/queue`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ uuids })
      })
    );
    return response.queue ?? [];
  }

  async removeQueueItem(instanceId: string, index: number): Promise<QueueTrack[]> {
    const response = await parseJson<{ queue: QueueTrack[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/queue?index=${index}`, { method: "DELETE" })
    );
    return response.queue ?? [];
  }

  async clearQueue(instanceId: string): Promise<QueueTrack[]> {
    const response = await parseJson<{ queue: QueueTrack[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/queue`, { method: "DELETE" })
    );
    return response.queue ?? [];
  }

  async moveQueueItem(instanceId: string, fromIndex: number, toIndex: number): Promise<QueueTrack[]> {
    const response = await parseJson<{ queue: QueueTrack[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/queue/move`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fromIndex, toIndex })
      })
    );
    return response.queue ?? [];
  }

  async history(instanceId: string): Promise<QueueTrack[]> {
    const response = await parseJson<{ history: QueueTrack[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/history`)
    );
    return response.history ?? [];
  }

  async playlistSources(instanceId: string): Promise<PlaylistSourceInfo[]> {
    const response = await parseJson<{ sources: PlaylistSourceInfo[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/playlist-sources`)
    );
    return response.sources ?? [];
  }

  async addPlaylistSource(
    instanceId: string,
    source: { id: string; name: string; kind: "playlist" | "track"; refId: string; uuids?: string[] }
  ): Promise<PlaylistSourceInfo[]> {
    const response = await parseJson<{ sources: PlaylistSourceInfo[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/playlist-sources`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ action: "add", source })
      })
    );
    return response.sources ?? [];
  }

  async removePlaylistSource(instanceId: string, sourceId: string): Promise<PlaylistSourceInfo[]> {
    const response = await parseJson<{ sources: PlaylistSourceInfo[] }>(
      await fetch(`/api/instances/${encodeURIComponent(instanceId)}/playlist-sources`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ action: "remove", sourceId })
      })
    );
    return response.sources ?? [];
  }

  async tracks(params: TrackQueryParams = {}): Promise<TrackQueryResponse> {
    const search = new URLSearchParams();
    if (params.text) search.set("text", params.text);
    if (params.moduleId) search.set("moduleId", params.moduleId);
    if (params.playlistId) search.set("playlistId", params.playlistId);
    if (params.offset !== undefined) search.set("offset", String(params.offset));
    search.set("limit", String(params.limit ?? 80));
    return parseJson<TrackQueryResponse>(await fetch(`/api/library/tracks?${search}`));
  }

  async playlists(moduleId = ""): Promise<PlaylistQueryResponse> {
    const search = new URLSearchParams();
    if (moduleId) search.set("moduleId", moduleId);
    search.set("limit", "80");
    return parseJson<PlaylistQueryResponse>(await fetch(`/api/library/playlists?${search}`));
  }

  async playlist(id: string): Promise<PlaylistWithEntries> {
    return parseJson<PlaylistWithEntries>(await fetch(`/api/library/playlists/${encodeURIComponent(id)}`));
  }

  async moduleUi(moduleId: string, kind: UiKind = "default", linkId = ""): Promise<RawNodeData> {
    const encodedModuleId = encodeURIComponent(moduleId);
    let url = `/api/modules/${encodedModuleId}/ui`;
    if (kind === "settings") {
      url = `/api/modules/${encodedModuleId}/settings`;
    } else if (kind === "link") {
      url = `/api/modules/${encodedModuleId}/link/${encodeURIComponent(linkId)}`;
    }
    return parseJson<RawNodeData>(await fetch(url));
  }

  async setModuleEnabled(moduleId: string, enabled: boolean): Promise<void> {
    await parseJson(
      await fetch(`/api/modules/${encodeURIComponent(moduleId)}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ enabled })
      })
    );
  }

  async sendUiEvent(
    moduleId: string,
    nodeId: string,
    action: string,
    value: string,
    uiKind: UiKind = "default",
    linkId = ""
  ): Promise<void> {
    const payload: JsonBody = {
      type: "ui_event",
      moduleId,
      uiKind,
      event: { nodeId, action, value }
    };
    if (linkId) {
      payload.linkId = linkId;
    }

    await fetch("/api/ui/event", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
  }

  openEvents(
    onJson: (message: WsJsonEnvelope) => void,
    onState: (state: "open" | "closed" | "error" | "binary") => void,
    onBinary?: () => void
  ): WebSocket {
    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    const socket = new WebSocket(`${protocol}//${window.location.host}/ws`);
    socket.binaryType = "arraybuffer";
    socket.addEventListener("open", () => onState("open"));
    socket.addEventListener("close", () => onState("closed"));
    socket.addEventListener("error", () => onState("error"));
    socket.addEventListener("message", (event) => {
      if (typeof event.data !== "string") {
        onBinary?.();
        return;
      }
      try {
        onJson(JSON.parse(event.data) as WsJsonEnvelope);
      } catch {
        onState("error");
      }
    });
    return socket;
  }
}
