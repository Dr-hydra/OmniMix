using System.Collections.Generic;

namespace OmniMixPlayer.Module.Kugou
{
    public class KugouSession
    {
        public string UserId { get; set; } = "0";
        public string Token { get; set; } = "";
        public string VipToken { get; set; } = "";
        public string VipType { get; set; } = "0";
        public string Dfid { get; set; } = "-";
        public string Mid { get; set; } = "0";
        public string Guid { get; set; } = "";
        public long LoginTime { get; set; }

        public bool IsLoggedIn => !string.IsNullOrWhiteSpace(Token) &&
                                  !string.IsNullOrWhiteSpace(UserId) &&
                                  UserId != "0";
    }

    public class KugouQrLoginInfo
    {
        public string Key { get; set; }
        public byte[] ImageBytes { get; set; }
        public string StatusText { get; set; }
    }

    public class KugouPlaylistInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CoverUrl { get; set; }
        public int Count { get; set; }
    }

    public class KugouSongInfo
    {
        public string Hash { get; set; }
        public long AlbumAudioId { get; set; }
        public long AlbumId { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string CoverUrl { get; set; }
        public float Duration { get; set; }
    }

    public class KugouSearchResult
    {
        public bool Success { get; set; }
        public List<KugouSongInfo> Songs { get; set; } = new();
        public string ErrorMessage { get; set; }

        public static KugouSearchResult Ok(List<KugouSongInfo> songs) => new()
        {
            Success = true,
            Songs = songs ?? new List<KugouSongInfo>()
        };

        public static KugouSearchResult Failed(string message) => new()
        {
            Success = false,
            ErrorMessage = message ?? ""
        };
    }

    public class KugouPlayableUrl
    {
        public string Url { get; set; }
        public int Bitrate { get; set; }
        public long FileSize { get; set; }
        public string Format { get; set; } = "mp3";
    }
}
