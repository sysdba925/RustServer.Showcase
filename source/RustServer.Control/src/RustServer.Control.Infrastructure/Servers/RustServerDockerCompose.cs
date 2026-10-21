using RustServer.Control.Application.Processes;
using System.Globalization;

namespace RustServer.Control.Infrastructure.Servers;

internal static class RustServerDockerCompose
{
    internal const string ServiceName = "rust-server";

    internal static ProcessExecutionRequest CreateRequest(
        RustServerControlOptions options,
        RustServerRconPort rconPort,
        params string[] composeArguments)
        => CreateRequest(options, rconPort, null, composeArguments);

    internal static ProcessExecutionRequest CreateRequest(
        RustServerControlOptions options,
        RustServerRconPort rconPort,
        IReadOnlyDictionary<string, string>? environmentVariables,
        params string[] composeArguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rconPort);

        var arguments = new List<string>
        {
            "compose",
            "-f",
            options.RustServerComposePath
        };
        arguments.AddRange(composeArguments);

        var composeEnvironment = environmentVariables is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(environmentVariables, StringComparer.Ordinal);
        composeEnvironment["RUST_SERVER_RCON_PORT"] = rconPort.Value.ToString(CultureInfo.InvariantCulture);

        return new ProcessExecutionRequest("docker", arguments, composeEnvironment);
    }
}
