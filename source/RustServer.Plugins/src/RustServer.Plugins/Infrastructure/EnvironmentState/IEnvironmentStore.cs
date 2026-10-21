using RustServer.Plugins.Features.PluginManagement.Models;

namespace RustServer.Plugins.Infrastructure.EnvironmentState;

public interface IEnvironmentStore
{
    Task<PluginEnvironment> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(PluginEnvironment environment, CancellationToken cancellationToken);
}
