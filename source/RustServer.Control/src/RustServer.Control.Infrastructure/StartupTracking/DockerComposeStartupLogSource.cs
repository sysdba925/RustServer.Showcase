using System.Globalization;
using RustServer.Control.Application.Processes;
using RustServer.Control.Application.StartupTracking;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Infrastructure.StartupTracking;

/// <summary>
/// Reads log lines for exactly the current Docker Compose Rust server run.
/// </summary>
public sealed class DockerComposeStartupLogSource : IStartupLogSource
{
    private const int MaximumReadAttempts = 2;
    private const string InspectFormat = "{{.Id}}|{{.State.StartedAt}}";

    private readonly IProcessExecutor _processExecutor;
    private readonly RustServerControlOptions _options;
    private readonly RustServerRconPort _rconPort;

    public DockerComposeStartupLogSource(
        IProcessExecutor processExecutor,
        RustServerControlOptions options,
        RustServerRconPort rconPort)
    {
        ArgumentNullException.ThrowIfNull(processExecutor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rconPort);

        _processExecutor = processExecutor;
        _options = options;
        _rconPort = rconPort;
    }

    public async Task<StartupRunLog?> ReadCurrentRunAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        for (var attempt = 0; attempt < MaximumReadAttempts; attempt++)
        {
            var run = await ResolveCurrentRunAsync(cancellationToken);
            if (run is null)
            {
                return null;
            }

            var lines = await ReadLogLinesAsync(run, cancellationToken);
            var verifiedRun = await ResolveCurrentRunAsync(cancellationToken);
            if (run == verifiedRun)
            {
                return new StartupRunLog(run.ContainerId, run.StartedAt, lines);
            }
        }

        throw new StartupTrackingException("current run verification");
    }

    private async Task<DockerRunIdentity?> ResolveCurrentRunAsync(CancellationToken cancellationToken)
    {
        var composeResult = await _processExecutor.ExecuteAsync(
            RustServerDockerCompose.CreateRequest(
                _options,
                _rconPort,
                "ps",
                "-q",
                RustServerDockerCompose.ServiceName),
            cancellationToken);
        ThrowIfFailed("container lookup", composeResult.ExitCode);

        var containerReference = GetSingleOutputLine(composeResult.StdOut, "container lookup");
        if (containerReference is null)
        {
            return null;
        }

        var inspectResult = await _processExecutor.ExecuteAsync(
            new ProcessExecutionRequest(
                "docker",
                ["inspect", "--format", InspectFormat, containerReference]),
            cancellationToken);
        ThrowIfFailed("container inspection", inspectResult.ExitCode);

        var inspectLine = GetSingleOutputLine(inspectResult.StdOut, "container inspection")
            ?? throw new StartupTrackingException("container inspection");
        var separatorIndex = inspectLine.IndexOf('|', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == inspectLine.Length - 1)
        {
            throw new StartupTrackingException("container inspection");
        }

        var containerId = inspectLine[..separatorIndex];
        var startedAt = inspectLine[(separatorIndex + 1)..];
        if (!TryParseDockerTimestamp(startedAt, out _))
        {
            throw new StartupTrackingException("container inspection");
        }

        return new DockerRunIdentity(containerId, startedAt);
    }

    private async Task<IReadOnlyList<string>> ReadLogLinesAsync(
        DockerRunIdentity run,
        CancellationToken cancellationToken)
    {
        var result = await _processExecutor.ExecuteAsync(
            new ProcessExecutionRequest(
                "docker",
                ["logs", "--timestamps", "--since", run.StartedAt, run.ContainerId]),
            cancellationToken);
        ThrowIfFailed("log read", result.ExitCode);

        return ParseTimestampedLines(result.StdOut, result.StdErr);
    }

    private static IReadOnlyList<string> ParseTimestampedLines(string standardOutput, string standardError)
    {
        var timestampedLines = new List<TimestampedLogLine>();
        var sequence = 0L;

        AddTimestampedLines(standardOutput, timestampedLines, ref sequence);
        AddTimestampedLines(standardError, timestampedLines, ref sequence);

        return timestampedLines
            .OrderBy(line => line.Timestamp.SecondsSinceUnixEpoch)
            .ThenBy(line => line.Timestamp.Nanoseconds)
            .ThenBy(line => line.Sequence)
            .Select(line => line.Text)
            .ToArray();
    }

    private static void AddTimestampedLines(
        string output,
        ICollection<TimestampedLogLine> timestampedLines,
        ref long sequence)
    {
        using var reader = new StringReader(output);

        while (reader.ReadLine() is { } line)
        {
            var separatorIndex = FindTimestampSeparator(line);
            if (separatorIndex <= 0
                || !TryParseDockerTimestamp(line[..separatorIndex], out var timestamp))
            {
                throw new StartupTrackingException("log timestamp parsing");
            }

            timestampedLines.Add(new TimestampedLogLine(
                timestamp,
                sequence++,
                line[(separatorIndex + 1)..]));
        }
    }

    private static int FindTimestampSeparator(string line)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (char.IsWhiteSpace(line[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? GetSingleOutputLine(string output, string operation)
    {
        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.Length switch
        {
            0 => null,
            1 => lines[0],
            _ => throw new StartupTrackingException(operation)
        };
    }

    private static bool TryParseDockerTimestamp(string value, out DockerLogTimestamp timestamp)
    {
        timestamp = default;

        const int dateAndTimeLength = 19;
        if (value.Length <= dateAndTimeLength
            || !DateTime.TryParseExact(
                value[..dateAndTimeLength],
                "yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateAndTime))
        {
            return false;
        }

        var position = dateAndTimeLength;
        var nanoseconds = 0;
        if (value[position] == '.')
        {
            var fractionStart = ++position;
            while (position < value.Length && char.IsDigit(value[position]))
            {
                if (position - fractionStart == 9)
                {
                    return false;
                }

                nanoseconds = (nanoseconds * 10) + (value[position] - '0');
                position++;
            }

            if (position == fractionStart)
            {
                return false;
            }

            for (var digits = position - fractionStart; digits < 9; digits++)
            {
                nanoseconds *= 10;
            }
        }

        if (!TryParseOffset(value, position, out var offset))
        {
            return false;
        }

        try
        {
            timestamp = new DockerLogTimestamp(
                new DateTimeOffset(DateTime.SpecifyKind(dateAndTime, DateTimeKind.Unspecified), offset)
                    .ToUnixTimeSeconds(),
                nanoseconds);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseOffset(string value, int position, out TimeSpan offset)
    {
        offset = default;

        if (position == value.Length - 1 && value[position] == 'Z')
        {
            return true;
        }

        if (position + 6 != value.Length
            || (value[position] is not ('+' or '-'))
            || value[position + 3] != ':'
            || !int.TryParse(value.AsSpan(position + 1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(value.AsSpan(position + 4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || hours > 14
            || minutes > 59)
        {
            return false;
        }

        offset = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        if (value[position] == '-')
        {
            offset = -offset;
        }

        return true;
    }

    private static void ThrowIfFailed(string operation, int exitCode)
    {
        if (exitCode != 0)
        {
            throw new StartupTrackingException(operation);
        }
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.RustServerComposePath))
        {
            throw new RustServerControlConfigurationException(
                $"Configuration value '{RustServerControlOptions.SectionName}:RustServerComposePath' is required.");
        }
    }

    private sealed record DockerRunIdentity(string ContainerId, string StartedAt);

    private readonly record struct DockerLogTimestamp(long SecondsSinceUnixEpoch, int Nanoseconds);

    private sealed record TimestampedLogLine(DockerLogTimestamp Timestamp, long Sequence, string Text);
}
