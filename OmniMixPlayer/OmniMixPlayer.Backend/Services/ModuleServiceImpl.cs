using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using OmniMixPlayer.Backend.ModuleSystem;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Services;

namespace OmniMixPlayer.Backend.Services
{
    public class ModuleServiceImpl : ModuleService.ModuleServiceBase
    {
        public override Task<ListModulesResponse> ListModules(ListModulesRequest request, ServerCallContext context)
        {
            var loader = ModuleLoader.Instance;
            var response = new ListModulesResponse();
            if (loader == null)
                return Task.FromResult(response);

            response.Modules.AddRange(loader.LoadedModules.Select(ToModuleInfo));
            return Task.FromResult(response);
        }

        public override Task<SetModuleEnabledResponse> SetModuleEnabled(SetModuleEnabledRequest request, ServerCallContext context)
        {
            var loader = ModuleLoader.Instance;
            if (loader == null)
                throw new RpcException(new Status(StatusCode.Unavailable, "Module loader not available"));

            if (string.IsNullOrWhiteSpace(request.ModuleId))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "module_id is required"));

            loader.SetModuleEnabled(request.ModuleId, request.Enabled);
            return Task.FromResult(new SetModuleEnabledResponse { Updated = true });
        }

        private static ModuleInfo ToModuleInfo(LoadedModule loaded)
        {
            var module = loaded.Module;
            var ui = module as IModuleUIProvider;
            var info = new ModuleInfo
            {
                Id = module.ModuleId ?? "",
                Name = module.DisplayName ?? "",
                Version = Convert.ToString(module.Version) ?? "",
                Priority = module.Priority,
                LoadedAt = loaded.LoadedAt.ToString("o"),
                Enabled = ModuleLoader.Instance?.IsModuleEnabled(module.ModuleId) ?? true,
                HasSettingsUi = ui?.HasSettingsUI ?? false,
                HasQuickLinks = ui?.HasQuickLinks ?? false
            };

            var links = ui?.GetQuickLinks() ?? Array.Empty<SDK.Interfaces.ModuleLinkEntry>();
            info.LinkEntries.AddRange(links.Select(link => new SDK.Protos.Services.ModuleLinkEntry
            {
                Id = link.Id ?? "",
                Title = link.Title ?? "",
                Icon = link.Icon ?? "",
                Svg = link.Svg ?? "",
                BackgroundColor = link.BackgroundColor ?? "",
                IconColor = link.IconColor ?? ""
            }));
            return info;
        }
    }
}
