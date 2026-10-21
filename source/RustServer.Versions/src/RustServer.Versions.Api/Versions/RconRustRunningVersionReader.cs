using System.Globalization;

namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Reads version metadata from the currently running Rust server through the RCON service.
/// </summary>
public sealed class RconRustRunningVersionReader(
    IRconClient rconClient,
    ILogger<RconRustRunningVersionReader> logger) : IRustRunningVersionReader
{
    private const string ProtocolPrefix = "Protocol:";
    private const string BuildDatePrefix = "Build Date:";
    private const string UnityVersionPrefix = "Unity Version:";
    private const string ChangesetPrefix = "Changeset:";
    private const string BranchPrefix = "Branch:";
    private const string BuildDateFormat = "MM/dd/yyyy HH:mm:ss";

    public async Task<RustRunningVersion?> ReadVersionAsync(CancellationToken cancellationToken)
    {
        RconCommandResult result;
        try
        {
            result = await rconClient.ExecuteCommandAsync("version", cancellationToken);
        }
        catch (RconClientException)
        {
            logger.LogInformation(
                "The RCON service is unavailable; the running Rust version cannot be reported.");
            return null;
        }

        try
        {
            return ParseVersion(result.ResponseMessage);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "Rust server returned an invalid version response.");
            throw;
        }
    }

    private static RustRunningVersion ParseVersion(string responseMessage)
    {
        if (responseMessage is null)
        {
            throw new InvalidOperationException("The version response does not contain a response message.");
        }

        var protocol = ReadRequiredValue(responseMessage, ProtocolPrefix, "Protocol");
        var buildDateValue = ReadRequiredValue(responseMessage, BuildDatePrefix, "Build Date");
        var unityVersion = ReadRequiredValue(responseMessage, UnityVersionPrefix, "Unity Version");
        var changesetValue = ReadRequiredValue(responseMessage, ChangesetPrefix, "Changeset");
        var branch = ReadRequiredValue(responseMessage, BranchPrefix, "Branch");

        if (!DateTime.TryParseExact(
                buildDateValue,
                BuildDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var buildDate))
        {
            throw new InvalidOperationException(
                $"The version response contains an invalid Build Date value: '{buildDateValue}'.");
        }

        if (!int.TryParse(changesetValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var changeset))
        {
            throw new InvalidOperationException(
                $"The version response contains an invalid Changeset value: '{changesetValue}'.");
        }

        return new RustRunningVersion(protocol, buildDate, unityVersion, changeset, branch);
    }

    private static string ReadRequiredValue(string responseMessage, string prefix, string fieldName)
    {
        string? value = null;

        foreach (var line in responseMessage.Split('\n'))
        {
            var trimmedLine = line.TrimEnd('\r');
            if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (value is not null)
            {
                throw new InvalidOperationException(
                    $"The version response contains multiple {fieldName} lines.");
            }

            value = trimmedLine[prefix.Length..].Trim();
        }

        if (value is null)
        {
            throw new InvalidOperationException(
                $"The version response does not contain a {fieldName} line.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The version response contains an empty {fieldName} value.");
        }

        return value;
    }
}
