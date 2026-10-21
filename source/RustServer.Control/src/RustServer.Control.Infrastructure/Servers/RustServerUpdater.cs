using RustServer.Control.Application.Processes;
using RustServer.Control.Application.Servers;

namespace RustServer.Control.Infrastructure.Servers;

public sealed class RustServerUpdater : IRustServerUpdater
{
    private readonly IProcessExecutor _processExecutor;
    private readonly RustServerUpdateOptions _options;

    public RustServerUpdater(IProcessExecutor processExecutor, RustServerUpdateOptions options)
    {
        _processExecutor = processExecutor;
        _options = options;
    }

    public async Task<RustServerUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var executionResult = await _processExecutor.ExecuteAsync(
            new ProcessExecutionRequest(
                "docker",
                ["compose", "-f", _options.SteamCmdComposePath, "run", "--rm", "steamcmd"]),
            cancellationToken);

        return new RustServerUpdateResult(
            executionResult.ExitCode == 0,
            executionResult.ExitCode);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.SteamCmdComposePath))
        {
            throw new RustServerUpdateConfigurationException(
                $"Configuration value '{RustServerUpdateOptions.SectionName}:SteamCmdComposePath' is required.");
        }

    }
}
