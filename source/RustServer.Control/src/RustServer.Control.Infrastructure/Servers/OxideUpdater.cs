using RustServer.Control.Application.Processes;
using RustServer.Control.Application.Servers;

namespace RustServer.Control.Infrastructure.Servers;

public sealed class OxideUpdater : IOxideUpdater
{
    private readonly IProcessExecutor _processExecutor;
    private readonly OxideUpdateOptions _options;

    public OxideUpdater(IProcessExecutor processExecutor, OxideUpdateOptions options)
    {
        _processExecutor = processExecutor;
        _options = options;
    }

    public async Task<OxideUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var executionResult = await _processExecutor.ExecuteAsync(
            new ProcessExecutionRequest(
                "docker",
                ["compose", "-f", _options.OxideComposePath, "run", "--rm", "oxide"]),
            cancellationToken);

        return new OxideUpdateResult(
            executionResult.ExitCode == 0,
            executionResult.ExitCode);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.OxideComposePath))
        {
            throw new OxideUpdateConfigurationException(
                $"Configuration value '{OxideUpdateOptions.SectionName}:OxideComposePath' is required.");
        }
    }
}
