namespace RustServer.Updater.Executor;

/// <summary>Entry point for socket-activated deployment execution and boot recovery.</summary>
internal static class Program
{
    /// <summary>Runs one socket request or scans incomplete transactions for recovery.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("RustServer.Updater.Executor is supported only on Linux.");
            return 1;
        }

        var options = new DeploymentExecutorOptions();
        using var readinessVerifier = new HttpApplicationReadinessVerifier();
        var executor = new DeploymentExecutor(options, new SystemdController(), readinessVerifier);

        if (args.Length == 1 && string.Equals(args[0], "--recover", StringComparison.Ordinal))
        {
            await executor.RecoverAsync(CancellationToken.None);
            return 0;
        }

        var operationText = args.Length == 1 ? args[0] : await Console.In.ReadLineAsync();
        if (!Guid.TryParse(operationText, out var operationId))
        {
            Console.Error.WriteLine("Deployment executor requires exactly one operation UUID.");
            return 2;
        }

        var transaction = await executor.ExecuteRequestAsync(operationId, CancellationToken.None);
        return transaction.Status == RustServer.Updater.Api.Deployments.DeploymentTransactionStatus.Succeeded ? 0 : 1;
    }
}
