using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.Baseline;

public interface IBaselineStore
{
    Task<BaselineModel?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(BaselineModel baseline, CancellationToken cancellationToken);
}
