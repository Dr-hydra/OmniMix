using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.Backend.Audio;
using OmniMixPlayer.Backend.ModuleSystem;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Services;

namespace OmniMixPlayer.Backend.Services
{
    public class LyricServiceImpl : LyricService.LyricServiceBase
    {
        private readonly ILibraryRegistry _library;
        private readonly ILogger<LyricServiceImpl> _logger;

        public LyricServiceImpl(ILibraryRegistry library, ILogger<LyricServiceImpl> logger)
        {
            _library = library;
            _logger = logger;
        }

        public override Task<GetLyricResponse> GetLyric(GetLyricRequest request, ServerCallContext context)
        {
            var uuid = request?.Uuid?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(uuid))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Track uuid is required"));

            var track = _library.GetTrack(uuid);
            if (track == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Track not found"));

            if (string.IsNullOrWhiteSpace(track.ModuleId))
                throw new RpcException(new Status(StatusCode.NotFound, "Track has no module id"));

            var provider = ModuleLoader.Instance?.GetProvider<ILyricProvider>(track.ModuleId);
            if (provider == null)
                throw new RpcException(new Status(StatusCode.Unimplemented, "Track module does not provide lyrics"));

            try
            {
                var lrcRaw = provider.GetLyric(uuid);
                if (string.IsNullOrWhiteSpace(lrcRaw))
                    throw new RpcException(new Status(StatusCode.NotFound, "Lyric not found"));

                string lrc = lrcRaw;
                string tlyric = "";
                string rlyric = "";

                if (lrcRaw.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        using (var doc = System.Text.Json.JsonDocument.Parse(lrcRaw))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("lrc", out var lrcProp)) lrc = lrcProp.GetString() ?? "";
                            if (root.TryGetProperty("tlyric", out var tlProp)) tlyric = tlProp.GetString() ?? "";
                            if (root.TryGetProperty("rlyric", out var rlProp)) rlyric = rlProp.GetString() ?? "";
                        }
                    }
                    catch
                    {
                        lrc = lrcRaw;
                    }
                }

                return Task.FromResult(new GetLyricResponse
                {
                    Uuid = uuid,
                    ModuleId = track.ModuleId ?? "",
                    Lrc = lrc,
                    Tlyric = tlyric,
                    Rlyric = rlyric
                });
            }
            catch (RpcException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get lyric for uuid {Uuid}", uuid);
                throw new RpcException(new Status(StatusCode.Internal, "Failed to get lyric"));
            }
        }
    }
}
