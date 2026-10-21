using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using RustServer.Plugins.Features.PluginManagement.Runtime;

namespace RustServer.Plugins.Features.PluginManagement.SystemState;

public sealed class SystemStateReader(
    IBaselineStore baselineStore,
    GitStatusReader gitStatusReader,
    IRuntimeFiles runtimeFiles)
{
    public async Task<PluginManagementSystemState> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await baselineStore.GetAsync(cancellationToken) is null)
            {
                return PluginManagementSystemState.Uninitialized;
            }

            var gitStatus = await gitStatusReader.GetAsync(cancellationToken);
            if (gitStatus.Status != GitReadiness.Ready)
            {
                return PluginManagementSystemState.Degraded;
            }

            var runtimeAvailability = await runtimeFiles.GetAvailabilityAsync(cancellationToken);
            return runtimeAvailability.IsAvailable
                ? PluginManagementSystemState.Ready
                : PluginManagementSystemState.Degraded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BaselineStateException)
        {
            return PluginManagementSystemState.Degraded;
        }
        catch (LocalGitException)
        {
            return PluginManagementSystemState.Degraded;
        }
        catch (RemoteGitException)
        {
            return PluginManagementSystemState.Degraded;
        }
        catch (RuntimeFilesException)
        {
            return PluginManagementSystemState.Degraded;
        }
    }
}
