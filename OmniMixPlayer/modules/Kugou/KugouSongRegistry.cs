using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Module.Kugou
{
    public class KugouSongRegistry
    {
        public const string PLAYLIST_SEARCH = "kugou_search";
        public const string PLAYLIST_PREFIX = "kugou_playlist_";

        private readonly IModuleContext _context;
        private readonly string _moduleId;

        public KugouSongRegistry(IModuleContext context, string moduleId)
        {
            _context = context;
            _moduleId = moduleId;
        }

        public void RegisterSearchResults(string keyword, List<KugouSongInfo> songs)
        {
            _context.Library.UpsertPlaylist(new Playlist
            {
                Id = PLAYLIST_SEARCH,
                Name = string.IsNullOrWhiteSpace(keyword) ? "Kugou Search" : $"Kugou: {keyword}",
                ModuleId = _moduleId,
                Kind = PlaylistKind.Imported
            });

            var entries = new List<PlaylistEntrySpec>();
            int position = 0;

            foreach (var song in songs ?? new List<KugouSongInfo>())
            {
                if (string.IsNullOrWhiteSpace(song.Hash)) continue;

                var uuid = GenerateUuid(song.Hash);
                _context.Library.UpsertTrack(new Track
                {
                    Uuid = uuid,
                    Title = song.Title ?? song.Hash,
                    Artist = song.Artist ?? "",
                    AlbumId = string.IsNullOrWhiteSpace(song.Album) ? "" : $"kugou_album_{HashId(song.Album)}",
                    SourceType = SourceType.Stream,
                    SourcePath = song.Hash,
                    Duration = song.Duration,
                    ModuleId = _moduleId,
                    CoverUri = song.CoverUrl ?? ""
                });

                if (!string.IsNullOrWhiteSpace(song.Album))
                {
                    _context.Library.UpsertAlbum(new Album
                    {
                        Id = $"kugou_album_{HashId(song.Album)}",
                        Title = song.Album,
                        Artist = song.Artist ?? "",
                        ModuleId = _moduleId,
                        CoverUri = song.CoverUrl ?? ""
                    });
                }

                entries.Add(new PlaylistEntrySpec { TrackUuid = uuid, Position = position++ });
            }

            _context.Library.ReplacePlaylistEntries(PLAYLIST_SEARCH, entries);
        }

        public void RegisterPlaylist(KugouPlaylistInfo playlist, List<KugouSongInfo> songs)
        {
            if (playlist == null || string.IsNullOrWhiteSpace(playlist.Id)) return;

            var playlistId = PLAYLIST_PREFIX + playlist.Id;
            _context.Library.UpsertPlaylist(new Playlist
            {
                Id = playlistId,
                Name = string.IsNullOrWhiteSpace(playlist.Name) ? $"Kugou {playlist.Id}" : playlist.Name,
                ModuleId = _moduleId,
                Kind = PlaylistKind.Imported,
                CoverUri = playlist.CoverUrl ?? ""
            });

            var entries = new List<PlaylistEntrySpec>();
            int position = 0;
            foreach (var song in songs ?? new List<KugouSongInfo>())
            {
                if (string.IsNullOrWhiteSpace(song.Hash)) continue;

                var uuid = GenerateUuid(song.Hash, song.AlbumAudioId);
                var albumId = song.AlbumId > 0
                    ? $"kugou_album_{song.AlbumId}"
                    : string.IsNullOrWhiteSpace(song.Album) ? "" : $"kugou_album_{HashId(song.Album)}";

                _context.Library.UpsertTrack(new Track
                {
                    Uuid = uuid,
                    Title = song.Title ?? song.Hash,
                    Artist = song.Artist ?? "",
                    AlbumId = albumId,
                    SourceType = SourceType.Stream,
                    SourcePath = song.Hash,
                    Duration = song.Duration,
                    ModuleId = _moduleId,
                    CoverUri = song.CoverUrl ?? ""
                });

                if (!string.IsNullOrWhiteSpace(song.Album))
                {
                    _context.Library.UpsertAlbum(new Album
                    {
                        Id = albumId,
                        Title = song.Album,
                        Artist = song.Artist ?? "",
                        ModuleId = _moduleId,
                        CoverUri = song.CoverUrl ?? ""
                    });
                }

                entries.Add(new PlaylistEntrySpec { TrackUuid = uuid, Position = position++ });
            }

            _context.Library.ReplacePlaylistEntries(playlistId, entries);
        }

        public static string GenerateUuid(string hash)
        {
            return GenerateUuid(hash, 0);
        }

        public static string GenerateUuid(string hash, long albumAudioId)
        {
            using var md5 = MD5.Create();
            var key = albumAudioId > 0 ? $"kugou_{hash}_{albumAudioId}" : $"kugou_{hash}";
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
            return new System.Guid(bytes).ToString("N");
        }

        private static string HashId(string text)
        {
            using var md5 = MD5.Create();
            return System.Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();
        }
    }
}
