using System;
using System.Collections.Generic;
using OmniMixPlayer.SDK.Interfaces;

namespace OmniMixPlayer.Module.YouTubeMusic
{
    public sealed class YouTubeMusicEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Url { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public float Duration { get; set; }
    }

    public sealed class YouTubeMusicPlaylistImport
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public List<YouTubeMusicEntry> Entries { get; set; } = new();
    }

    public sealed class YouTubeMusicPlayable
    {
        public string Url { get; set; } = "";
        public string Extension { get; set; } = "";
        public AudioFormat Format { get; set; } = AudioFormat.Unknown;
        public long? FileSize { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
