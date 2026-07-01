using System;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.Backend.Http;
using OmniMixPlayer.SDK.Protos.Services;

namespace OmniMixPlayer.Backend.Services
{
    public class ConfigServiceImpl : ConfigService.ConfigServiceBase
    {
        private readonly GlobalConfigManager _config;
        private readonly ILogger<ConfigServiceImpl> _logger;

        public ConfigServiceImpl(GlobalConfigManager config, ILogger<ConfigServiceImpl> logger)
        {
            _config = config;
            _logger = logger;
        }

        public override Task<GetConfigResponse> GetConfig(GetConfigRequest request, ServerCallContext context)
        {
            return Task.FromResult(new GetConfigResponse { Json = _config.GetRawJson() });
        }

        public override Task<UpdateConfigResponse> UpdateConfig(UpdateConfigRequest request, ServerCallContext context)
        {
            try
            {
                _config.UpdateFromJson(request?.Json ?? "{}");
                return Task.FromResult(new UpdateConfigResponse { Updated = true });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid config update JSON");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid config update JSON"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update config");
                throw new RpcException(new Status(StatusCode.Internal, "Failed to update config"));
            }
        }

        public override Task<SaveConfigResponse> SaveConfig(SaveConfigRequest request, ServerCallContext context)
        {
            _config.Save();
            return Task.FromResult(new SaveConfigResponse { Saved = true });
        }
    }
}
