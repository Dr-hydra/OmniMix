export type HealthResponse = {
  status: string;
  timestamp: number;
};

export type VersionResponse = {
  name: string;
  version: string;
};

export type AppConfig = Record<string, unknown>;

export type OmniTimestamp = {
  seconds: number;
  nanos: number;
};

export type InstanceCapabilities = {
  serverControlledPlayback: boolean;
  queueManagement: boolean;
  playlistManagement: boolean;
  multiplePlaylists: boolean;
  tagFiltering: boolean;
  albumFiltering: boolean;
  shuffle: boolean;
  repeat: boolean;
  seek: boolean;
  volumeControl: boolean;
  equalizer: boolean;
  audioPlayback: boolean;
};

export type PlaybackStatus = {
  trackUuid: string;
  title: string;
  artist: string;
  albumId: string;
  duration: number;
  position: number;
  isPlaying: boolean;
  shuffle: boolean;
  repeatMode: "none" | "one" | "all";
  volume: number;
};

export type PlaybackInstance = {
  id: string;
  displayName: string;
  kind: string;
  isOnline: boolean;
  currentTrackUuid: string;
  queueCount: number;
  modId: string;
  gameName: string;
  connectedAt?: OmniTimestamp | null;
  capabilities?: InstanceCapabilities | null;
  status: PlaybackStatus;
};

export type TrackInfo = {
  uuid: string;
  title: string;
  artist: string;
  albumId: string;
  duration: number;
  moduleId: string;
  sourceType: string;
  sourcePath: string;
  isFavorite: boolean;
  isExcluded: boolean;
  coverUri: string;
  playCount: number;
  createdAt?: OmniTimestamp | null;
  lastPlayedAt?: OmniTimestamp | null;
};

export type QueueTrack = {
  index: number;
  uuid: string;
  title: string;
  artist: string;
  albumId: string;
  duration: number;
  moduleId: string;
  coverUri: string;
};

export type PlaylistInfo = {
  id: string;
  name: string;
  moduleId: string;
  kind: string;
  coverUri: string;
  sortOrder: number;
  createdAt?: OmniTimestamp | null;
  updatedAt?: OmniTimestamp | null;
};

export type PlaylistEntryInfo = {
  entryId: string;
  trackUuid: string;
  title: string;
  artist: string;
  duration: number;
  albumId: string;
  coverUri: string;
  position: number;
};

export type PlaylistWithEntries = {
  playlistId: string;
  playlistName: string;
  entries: PlaylistEntryInfo[];
};

export type PlaylistSourceInfo = {
  id: string;
  name: string;
  songCount: number;
  kind: "tag" | "album" | "playlist" | "track" | "unspecified";
  refId: string;
};

export type Pagination = {
  offset: number;
  limit: number;
  total: number;
};

export type TrackQueryParams = {
  text?: string;
  moduleId?: string;
  playlistId?: string;
  offset?: number;
  limit?: number;
};

export type TrackQueryResponse = {
  tracks: TrackInfo[];
  pagination: Pagination;
};

export type PlaylistQueryResponse = {
  playlists: PlaylistInfo[];
  pagination: Pagination;
};

export type ModuleLinkEntry = {
  id: string;
  title: string;
  icon?: string;
  svg?: string;
  backgroundColor?: string;
  iconColor?: string;
};

export type ModuleInfo = {
  id: string;
  name: string;
  version: string;
  priority: number;
  loadedAt: string;
  enabled: boolean;
  hasSettingsUI: boolean;
  hasQuickLinks: boolean;
  linkEntries: ModuleLinkEntry[];
};

export type RawOptionData = {
  value: string;
  label: string;
};

export type RawNodeData = {
  id?: string;
  "node-type"?: string;
  text?: string;
  "font-size"?: number;
  color?: string;
  direction?: string;
  spacing?: number;
  padding?: number;
  "cross-axis-align"?: string;
  children?: RawNodeData[];
  value?: string;
  "input-type"?: string;
  "button-variant"?: string | null;
  checked?: boolean;
  source?: string;
  "image-width"?: number;
  "image-height"?: number;
  "image-fit"?: string;
  "selected-value"?: string;
  options?: RawOptionData[];
  items?: RawNodeData[];
};

export type UiKind = "default" | "settings" | "link";

export type UiPushPayload = {
  type?: string;
  moduleId: string;
  replace?: boolean;
  tree?: RawNodeData;
};

export type WsJsonEnvelope = {
  type?: string;
  data?: unknown;
  timestamp?: number;
};
