using System.Threading.Tasks;
using Grpc.Core;
using OmniMixPlayer.Backend.Http;
using OmniMixPlayer.SDK.Protos.Services;

namespace OmniMixPlayer.Backend.Services
{
    public class BackendServiceImpl : BackendService.BackendServiceBase
    {
        private readonly ApiServer _apiServer;

        public BackendServiceImpl(ApiServer apiServer)
        {
            _apiServer = apiServer;
        }

        public override Task<StopBackendResponse> StopBackend(StopBackendRequest request, ServerCallContext context)
        {
            _apiServer.RequestBackendStop();
            return Task.FromResult(new StopBackendResponse { Stopping = true });
        }
    }
}
