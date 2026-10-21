namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Reads the version loaded by the currently running Rust server through the RCON service.
/// </summary>
public sealed class RconOxideRunningVersionReader(
    IRconClient rconClient,
    ILogger<RconOxideRunningVersionReader> logger) : IOxideRunningVersionReader
{
    private const string VersionPrefix = "Oxide.Rust Version:";

    public async Task<OxideVersion?> ReadVersionAsync(CancellationToken cancellationToken)
    {
        RconCommandResult result;
        try
        {
            result = await rconClient.ExecuteCommandAsync("oxide.version", cancellationToken);
        }
        catch (RconClientException)
        {
            logger.LogInformation(
                "The RCON service is unavailable; the running Oxide version cannot be reported.");
            return null;
        }

        try
        {
            return ParseVersion(result.ResponseMessage);
        }
        catch (InvalidOperationException exception)
        {
            // A malformed or unexpected RCON reply cannot invalidate the
            // independently available installed/available Oxide versions.
            logger.LogWarning(
                "Unable to parse the running Oxide version: {Reason}. Reporting it as unavailable.",
                exception.Message);
            return null;
        }
    }

    private static OxideVersion ParseVersion(string responseMessage)
    {
        if (responseMessage is null)
        {
            throw new InvalidOperationException(
                "The oxide.version response does not contain a response message.");
        }

        string? versionValue = null;
        foreach (var line in responseMessage.Split('\n'))
        {
            var trimmedLine = line.Trim();
            // Rust WebRCON can wrap console output in an Oxide log prefix:
            // [Oxide] 20:11 [Info] Oxide.Rust Version: 2.0.7830
            // Also retain support for the raw "Oxide.Rust Version:" response.
            if (trimmedLine.StartsWith("[Oxide] ", StringComparison.Ordinal))
            {
                var logLevelEnd = trimmedLine.IndexOf("] ", "[Oxide] ".Length, StringComparison.Ordinal);
                if (logLevelEnd < 0)
                {
                    continue;
                }

                trimmedLine = trimmedLine[(logLevelEnd + 2)..];
            }

            if (!trimmedLine.StartsWith(VersionPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (versionValue is not null)
            {
                throw new InvalidOperationException(
                    "The oxide.version response contains multiple Oxide.Rust Version lines.");
            }

            versionValue = trimmedLine[VersionPrefix.Length..].Trim();
        }

        if (versionValue is null)
        {
            throw new InvalidOperationException(
                "The oxide.version response does not contain an Oxide.Rust Version line.");
        }

        return OxideVersion.Parse(versionValue, "the oxide.version response");
    }
}
