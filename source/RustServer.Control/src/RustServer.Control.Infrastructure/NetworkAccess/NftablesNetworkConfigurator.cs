using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Application.Processes;
using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Infrastructure.NetworkAccess;

/// <summary>
/// Applies Control-owned nftables rules for the running Rust Docker container.
/// </summary>
public sealed class NftablesNetworkConfigurator
{
    private const string DockerExecutable = "docker";
    private const string NftExecutable = "nft";
    private const string SocketStatisticsExecutable = "ss";
    private const string NetworkTableFamily = "inet";
    private const string NetworkTableName = "rsc_network_access";
    private const string PreroutingChain = "prerouting";
    private const string ForwardChain = "forward";
    private const string DockerTableFamily = "ip";
    private const string DockerTableName = "filter";
    private const string DockerUserChain = "DOCKER-USER";
    private const string DockerControlChain = "RSC_NETWORK_ACCESS";
    private const string RuleComment = "rustserver-control-network-access";
    private readonly IProcessExecutor _processExecutor;
    private readonly ILogger<NftablesNetworkConfigurator> _logger;

    public NftablesNetworkConfigurator(
        IProcessExecutor processExecutor,
        ILogger<NftablesNetworkConfigurator> logger)
    {
        _processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal async Task<NetworkAccessHelperResponse> ApplyAsync(
        NetworkAccessHelperRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        try
        {
            return await ApplyRulesAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "NetworkAccess rule application failed unexpectedly.");
            return Failure(NetworkAccessRuleFailureKind.ApplyFailed, "The NetworkAccess nftables rules could not be applied.");
        }
    }

    private async Task<NetworkAccessHelperResponse> ApplyRulesAsync(
        NetworkAccessHelperRequest request,
        CancellationToken cancellationToken)
    {
        // The host firewall can be configured while Docker is completely stopped.
        var hostSockets = await ExecuteAsync(
            new ProcessExecutionRequest(SocketStatisticsExecutable, ["-H", "-u", "-l", "-n"]),
            cancellationToken);
        if (hostSockets is null || hostSockets.ExitCode != 0)
        {
            return Failure(NetworkAccessRuleFailureKind.ApplyFailed, "Host UDP socket inspection failed.");
        }

        if (IsHostUdpPortBound(hostSockets.StdOut, request.MaintenancePort))
        {
            return Failure(NetworkAccessRuleFailureKind.PortInUse, "The maintenance UDP port is already in use.");
        }

        string? containerIp = null;
        string? dockerUserChain = null;
        var hasDockerControlChain = false;
        NetworkAccessRuleFailureKind? degraded = null;

        var rustContainers = await ExecuteAsync(
            new ProcessExecutionRequest(
                DockerExecutable,
                ["ps", "--filter", "label=com.docker.compose.service=rust-server", "--format", "{{.ID}}"]),
            cancellationToken,
            nonZeroExitIsExpected: true);

        var ids = rustContainers?.ExitCode == 0 ? SplitTokens(rustContainers.StdOut) : [];
        if (ids.Length > 1)
        {
            degraded = NetworkAccessRuleFailureKind.ContainerUnavailable;
        }
        else if (ids.Length == 1)
        {
            var result = await ExecuteAsync(
                new ProcessExecutionRequest(
                    DockerExecutable,
                    ["inspect", "--format", "{{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}", ids[0]]),
                cancellationToken,
                nonZeroExitIsExpected: true);
            if (result?.ExitCode == 0 && TryGetSingleIpv4Address(result.StdOut, out var ip))
            {
                containerIp = ip;
            }
        }

        if (containerIp is not null)
        {
            var running = await ExecuteAsync(
                new ProcessExecutionRequest(DockerExecutable, ["ps", "-q"]),
                cancellationToken);
            if (running is null || running.ExitCode != 0)
            {
                degraded = NetworkAccessRuleFailureKind.ApplyFailed;
                containerIp = null;
            }
            else
            {
                var allIds = SplitTokens(running.StdOut);
                if (allIds.Length > 0)
                {
                    var ports = await ExecuteAsync(
                        new ProcessExecutionRequest(
                            DockerExecutable,
                            ["inspect", "--format",
                                "{{range $containerPort, $bindings := .NetworkSettings.Ports}}{{range $bindings}}{{printf \"%s %s\\n\" $containerPort .HostPort}}{{end}}{{end}}",
                                .. allIds]),
                        cancellationToken);
                    if (ports is null || ports.ExitCode != 0)
                    {
                        degraded = NetworkAccessRuleFailureKind.ApplyFailed;
                        containerIp = null;
                    }
                    else if (IsDockerUdpPortPublished(ports.StdOut, request.MaintenancePort))
                    {
                        return Failure(NetworkAccessRuleFailureKind.PortInUse, "The maintenance UDP port is published by Docker.");
                    }
                }
            }
        }

        if (containerIp is not null)
        {
            var dockerChain = await ExecuteAsync(
                new ProcessExecutionRequest(
                    NftExecutable, ["-a", "list", "chain", DockerTableFamily, DockerTableName, DockerUserChain]),
                cancellationToken,
                nonZeroExitIsExpected: true);
            if (dockerChain is null || dockerChain.ExitCode != 0)
            {
                degraded = NetworkAccessRuleFailureKind.UnsupportedDockerFirewall;
                containerIp = null;
            }
            else
            {
                dockerUserChain = dockerChain.StdOut;
                var controlChain = await ExecuteAsync(
                    new ProcessExecutionRequest(
                        NftExecutable, ["list", "chain", DockerTableFamily, DockerTableName, DockerControlChain]),
                    cancellationToken,
                    nonZeroExitIsExpected: true);
                if (controlChain is null)
                {
                    degraded = NetworkAccessRuleFailureKind.ApplyFailed;
                    containerIp = null;
                }
                else
                {
                    hasDockerControlChain = controlChain.ExitCode == 0;
                }
            }
        }

        // No container / no Docker -> only host guard rules. No DNAT required.
        var rules = BuildRules(
            request,
            containerIp,
            hasDockerControlChain,
            dockerUserChain?.Contains($"jump {DockerControlChain}", StringComparison.Ordinal) ?? false);
        var applyResult = await ExecuteAsync(
            new ProcessExecutionRequest(NftExecutable, ["-f", "-"], StandardInput: rules),
            cancellationToken);
        if (applyResult is null || applyResult.ExitCode != 0)
        {
            return Failure(NetworkAccessRuleFailureKind.ApplyFailed, "The NetworkAccess nftables rules could not be applied.");
        }

        if (degraded.HasValue)
        {
            return Failure(degraded.Value, "The firewall guard was applied; Docker integration needs attention.");
        }

        _logger.LogInformation("NetworkAccess rules applied; public={Public}, hasContainer={HasContainer}.",
            request.PublicEnabled && containerIp is not null, containerIp is not null);
        return new NetworkAccessHelperResponse(true, null, null);
    }

    private async Task<ProcessExecutionResult?> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken,
        bool nonZeroExitIsExpected = false)
    {
        try
        {
            var result = await _processExecutor.ExecuteAsync(request, cancellationToken);
            if (result.ExitCode == 0)
            {
                _logger.LogDebug(
                    "NetworkAccess command {FileName} completed with exit code {ExitCode}. StdOut: {StdOut}. StdErr: {StdErr}",
                    request.FileName,
                    result.ExitCode,
                    result.StdOut,
                    result.StdErr);
            }
            else if (nonZeroExitIsExpected)
            {
                _logger.LogDebug(
                    "NetworkAccess command {FileName} completed with expected non-zero exit code {ExitCode}. StdOut: {StdOut}. StdErr: {StdErr}",
                    request.FileName,
                    result.ExitCode,
                    result.StdOut,
                    result.StdErr);
            }
            else
            {
                _logger.LogError(
                    "NetworkAccess command {FileName} failed with exit code {ExitCode}. StdOut: {StdOut}. StdErr: {StdErr}",
                    request.FileName,
                    result.ExitCode,
                    result.StdOut,
                    result.StdErr);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "NetworkAccess command {FileName} could not be executed.", request.FileName);
            return null;
        }
    }

    private static string BuildRules(
        NetworkAccessHelperRequest request,
        string? containerIp,
        bool hasDockerControlChain,
        bool hasDockerControlJump)
    {
        var port = request.MaintenancePort.ToString(CultureInfo.InvariantCulture);
        var blockPublic = !request.PublicEnabled || containerIp is null;
        var rules = new StringBuilder();

        // Recreate only our own nft table in a single atomic transaction.
        rules.AppendLine($"destroy table {NetworkTableFamily} {NetworkTableName}");
        rules.AppendLine($"add table {NetworkTableFamily} {NetworkTableName}");
        rules.AppendLine($"add chain {NetworkTableFamily} {NetworkTableName} input {{ type filter hook input priority -1; policy accept; }}");
        rules.AppendLine($"add chain {NetworkTableFamily} {NetworkTableName} {PreroutingChain} {{ type nat hook prerouting priority -99; policy accept; }}");
        rules.AppendLine($"add chain {NetworkTableFamily} {NetworkTableName} {ForwardChain} {{ type filter hook forward priority -1; policy accept; }}");
        if (blockPublic)
        {
            rules.AppendLine($"add rule {NetworkTableFamily} {NetworkTableName} input udp dport {{ {NetworkAccessSettings.GamePort}, {NetworkAccessSettings.QueryPort} }} drop");
            rules.AppendLine($"add rule {NetworkTableFamily} {NetworkTableName} {ForwardChain} meta l4proto udp ct original proto-dst {{ {NetworkAccessSettings.GamePort}, {NetworkAccessSettings.QueryPort} }} drop");
        }

        if (containerIp is not null)
        {
            rules.AppendLine($"add rule {NetworkTableFamily} {NetworkTableName} {PreroutingChain} udp dport {port} dnat ip to {containerIp}:{NetworkAccessSettings.GamePort}");
            if (!hasDockerControlChain)
            {
                rules.AppendLine($"add chain {DockerTableFamily} {DockerTableName} {DockerControlChain}");
            }

            rules.AppendLine($"flush chain {DockerTableFamily} {DockerTableName} {DockerControlChain}");
            rules.AppendLine($"add rule {DockerTableFamily} {DockerTableName} {DockerControlChain} ip daddr {containerIp} udp dport {NetworkAccessSettings.GamePort} accept comment \"{RuleComment}\"");
            if (!hasDockerControlJump)
            {
                rules.AppendLine($"insert rule {DockerTableFamily} {DockerTableName} {DockerUserChain} jump {DockerControlChain} comment \"{RuleComment}\"");
            }
        }
        return rules.ToString();
    }

    private static string[] SplitTokens(string value) =>
        value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryGetSingleIpv4Address(string value, out string address)
    {
        var addresses = SplitTokens(value);
        if (addresses.Length == 1 &&
            IPAddress.TryParse(addresses[0], out var parsedAddress) &&
            parsedAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            address = parsedAddress.ToString();
            return true;
        }

        address = string.Empty;
        return false;
    }

    private static bool IsHostUdpPortBound(string value, int port)
    {
        var portSuffix = $":{port.ToString(CultureInfo.InvariantCulture)}";
        return SplitTokens(value).Any(field => field.EndsWith(portSuffix, StringComparison.Ordinal));
    }

    private static bool IsDockerUdpPortPublished(string value, int port)
    {
        var portValue = port.ToString(CultureInfo.InvariantCulture);
        foreach (var line in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = SplitTokens(line);
            if (fields.Length >= 2 &&
                fields[0].EndsWith("/udp", StringComparison.Ordinal) &&
                string.Equals(fields[1], portValue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void Validate(NetworkAccessHelperRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaintenancePort is < 1 or > 65_535 ||
            request.MaintenancePort is NetworkAccessSettings.GamePort or NetworkAccessSettings.QueryPort)
        {
            throw new ArgumentException("The requested maintenance port is invalid.");
        }
    }

    private static NetworkAccessHelperResponse Failure(NetworkAccessRuleFailureKind failureKind, string detail) =>
        new(false, failureKind.ToString(), detail);
}
