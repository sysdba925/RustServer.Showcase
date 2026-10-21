namespace RustServer.Plugins.Features.PluginManagement.Operations;

public sealed class OperationHistoryStartupService(IOperationHistoryStore historyStore) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        historyStore.MarkInProgressAsInterrupted(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
