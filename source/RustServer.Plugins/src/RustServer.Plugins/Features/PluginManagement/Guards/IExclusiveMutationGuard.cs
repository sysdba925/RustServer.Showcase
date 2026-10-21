namespace RustServer.Plugins.Features.PluginManagement.Guards;

public interface IExclusiveMutationGuard
{
    IExclusiveMutationLease? TryAcquire();
}
