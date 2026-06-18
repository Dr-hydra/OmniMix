using System.Collections.Generic;

namespace OmniMixPlayer.Module.Kuwo
{
    public class KuwoSongInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string CoverUrl { get; set; }
        public float Duration { get; set; }
    }

    public class KuwoPlaylistInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CoverUrl { get; set; }
        public int Count { get; set; }
    }

    public class KuwoSearchResult
    {
        public bool Success { get; set; }
        public List<KuwoSongInfo> Songs { get; set; } = new();
        public string ErrorMessage { get; set; }

        public static KuwoSearchResult Ok(List<KuwoSongInfo> songs) => new()
        {
            Success = true,
            Songs = songs ?? new List<KuwoSongInfo>()
        };

        public static KuwoSearchResult Failed(string message) => new()
        {
            Success = false,
            ErrorMessage = message ?? ""
        };
    }
}
