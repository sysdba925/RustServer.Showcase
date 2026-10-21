using RustServer.Plugins.Infrastructure.EnvironmentState;

namespace RustServer.Plugins.Features.PluginManagement.Commands.SwitchEnvironment;

public sealed class SwitchEnvironmentHandler(IEnvironmentStore environmentStore)
{
    public Task HandleAsync(
        SwitchEnvironmentCommand command,
        CancellationToken cancellationToken) =>
        environmentStore.SaveAsync(command.Environment, cancellationToken);
}
