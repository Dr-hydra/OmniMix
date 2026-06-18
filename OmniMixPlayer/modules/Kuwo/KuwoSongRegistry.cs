using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Module.Kuwo
{
    public class KuwoSongRegistry
    {
        public const string PLAYLIST_SEARCH = "kuwo_search";
        public const string PLAYLIST_PREFIX = "kuwo_playlist_";

        private readonly IModuleContext _context;
        private readonly string _moduleId;

        public KuwoSongRegistry(IModuleContext context, string moduleId)
        {
            _context = context;
            _moduleId = moduleId;
        }

        public void RegisterSearchResults(string keyword, List<KuwoSongInfo> songs)
        {
            _context.Library.UpsertPlaylist(new Playlist
            {
                Id = PLAYLIST_SEARCH,
                Name = string.IsNullOrWhiteSpace(keyword) ? "Kuwo Search" : $"Kuwo: {keyword}",
                ModuleId = _moduleId,
                Kind = PlaylistKind.Imported
            });

            var entries = new List<PlaylistEntrySpec>();
            int position = 0;

            foreach (var song in songs ?? new List<KuwoSongInfo>())
            {
                if (string.IsNullOrWhiteSpace(song.Id)) continue;

                var uuid = GenerateUuid(song.Id);
                var albumId = string.IsNullOrWhiteSpace(song.Album) ? "" : $"kuwo_album_{HashId(song.Album)}";
                _context.Library.UpsertTrack(new Track
                {
                    Uuid = uuid,
                    Title = song.Title ?? song.Id,
                    Artist = song.Artist ?? "",
                    AlbumId = albumId,
                    SourceType = SourceType.Stream,
                    SourcePath = song.Id,
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

            _context.Library.ReplacePlaylistEntries(PLAYLIST_SEARCH, entries);
        }

        public void RegisterPlaylist(KuwoPlaylistInfo playlist, List<KuwoSongInfo> songs)
        {
            if (playlist == null || string.IsNullOrWhiteSpace(playlist.Id)) return;

            var playlistId = PLAYLIST_PREFIX + playlist.Id;
            _context.Library.UpsertPlaylist(new Playlist
            {
                Id = playlistId,
                Name = string.IsNullOrWhiteSpace(playlist.Name) ? $"Kuwo {playlist.Id}" : playlist.Name,
                ModuleId = _moduleId,
                Kind = PlaylistKind.Imported,
                CoverUri = playlist.CoverUrl ?? ""
            });

            var entries = new List<PlaylistEntrySpec>();
            int position = 0;

            foreach (var song in songs ?? new List<KuwoSongInfo>())
            {
                if (string.IsNullOrWhiteSpace(song.Id)) continue;

                var uuid = GenerateUuid(song.Id);
                var albumId = string.IsNullOrWhiteSpace(song.Album) ? "" : $"kuwo_album_{HashId(song.Album)}";
                _context.Library.UpsertTrack(new Track
                {
                    Uuid = uuid,
                    Title = song.Title ?? song.Id,
                    Artist = song.Artist ?? "",
                    AlbumId = albumId,
                    SourceType = SourceType.Stream,
                    SourcePath = song.Id,
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

        public static string GenerateUuid(string id)
        {
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes($"kuwo_{id}"));
            return new System.Guid(bytes).ToString("N");
        }

        private static string HashId(string text)
        {
            using var md5 = MD5.Create();
            return System.Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();
        }
    }
}
