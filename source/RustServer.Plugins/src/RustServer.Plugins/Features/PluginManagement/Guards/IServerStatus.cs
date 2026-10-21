namespace RustServer.Plugins.Features.PluginManagement.Guards;

public interface IServerStatus
{
    Task<ServerStatusSnapshot> GetAsync(CancellationToken cancellationToken);
}
