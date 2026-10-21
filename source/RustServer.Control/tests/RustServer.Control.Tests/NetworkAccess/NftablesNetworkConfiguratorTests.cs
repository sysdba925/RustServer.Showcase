using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Application.Processes;
using RustServer.Control.Infrastructure.NetworkAccess;

namespace RustServer.Control.Tests.NetworkAccess;

public sealed class NftablesNetworkConfiguratorTests
{
    [Fact]
    public async Task ApplyAsync_OfflineDocker_InstallsFailClosedHostGuardWithoutContainerIp()
    {
        var executor = new RecordingProcessExecutor([
            Success(),
            new ProcessExecutionResult(1, "", "Cannot connect to Docker daemon"),
            Success()
        ]);
        var configurator = CreateConfigurator(executor);

        var result = await configurator.ApplyAsync(
            new NetworkAccessHelperRequest(PublicEnabled: true, MaintenancePort: 50_016));

        Assert.True(result.Succeeded);
        var nft = Assert.Single(executor.Requests, request =>
            request.FileName == "nft" && request.Arguments.SequenceEqual(["-f", "-"]));
        Assert.Contains("input udp dport { 28015, 28017 } drop", nft.StandardInput);
        Assert.Contains("ct original proto-dst { 28015, 28017 } drop", nft.StandardInput);
        Assert.DoesNotContain("dnat ip to", nft.StandardInput);
        Assert.DoesNotContain("DOCKER-USER", nft.StandardInput);
    }

    [Fact]
    public async Task ApplyAsync_NoRustContainer_AppliesGuardAndReturnsSuccess()
    {
        var executor = new RecordingProcessExecutor([Success(), Success(), Success()]);
        var configurator = CreateConfigurator(executor);

        var result = await configurator.ApplyAsync(
            new NetworkAccessHelperRequest(PublicEnabled: false, MaintenancePort: 50_016));

        Assert.True(result.Succeeded);
        Assert.Contains(executor.Requests, request =>
            request.FileName == "nft" && request.Arguments.SequenceEqual(["-f", "-"]));
    }

    [Fact]
    public async Task ApplyAsync_RunningContainer_InstallsMaintenanceDnatAndBlocksPublicPorts()
    {
        var executor = new RecordingProcessExecutor([.. ContainerPrerequisites(), Success()]);
        var configurator = CreateConfigurator(executor);

        var result = await configurator.ApplyAsync(
            new NetworkAccessHelperRequest(PublicEnabled: false, MaintenancePort: 50_016));

        Assert.True(result.Succeeded);
        var nft = Assert.Single(executor.Requests, request =>
            request.FileName == "nft" && request.Arguments.SequenceEqual(["-f", "-"]));
        Assert.Equal(ExpectedBlockedRules, nft.StandardInput);
    }

    [Fact]
    public async Task ApplyAsync_RunningAndReadyWithPublicEnabled_OpensOnlyPublicPorts()
    {
        var executor = new RecordingProcessExecutor([.. ContainerPrerequisites(), Success()]);
        var configurator = CreateConfigurator(executor);

        var result = await configurator.ApplyAsync(
            new NetworkAccessHelperRequest(PublicEnabled: true, MaintenancePort: 50_016));

        Assert.True(result.Succeeded);
        var nft = Assert.Single(executor.Requests, request =>
            request.FileName == "nft" && request.Arguments.SequenceEqual(["-f", "-"]));
        Assert.Contains("dnat ip to 172.17.0.2:28015", nft.StandardInput);
        Assert.DoesNotContain("udp dport { 28015, 28017 } drop", nft.StandardInput);
    }

    [Fact]
    public async Task ApplyAsync_InvalidFirewallSyntax_ReturnsSafeErrorWithoutCommandOutput()
    {
        var executor = new RecordingProcessExecutor([
            .. ContainerPrerequisites(),
            new ProcessExecutionResult(1, "", "syntax error: sensitive host info")
        ]);
        var configurator = CreateConfigurator(executor);

        var result = await configurator.ApplyAsync(
            new NetworkAccessHelperRequest(PublicEnabled: true, MaintenancePort: 50_016));

        Assert.False(result.Succeeded);
        Assert.Equal(NetworkAccessRuleFailureKind.ApplyFailed.ToString(), result.FailureKind);
        Assert.DoesNotContain("sensitive", result.Detail);
    }

    private static NftablesNetworkConfigurator CreateConfigurator(RecordingProcessExecutor executor) =>
        new(executor, NullLogger<NftablesNetworkConfigurator>.Instance);

    private static IEnumerable<ProcessExecutionResult> ContainerPrerequisites()
    {
        yield return Success(); // ss
        yield return Success("rust-container-id\n"); // Docker filtered ps
        yield return Success("172.17.0.2\n"); // Docker inspect IP
        yield return Success("rust-container-id\n"); // all running containers
        yield return Success(); // published ports
        yield return Success("chain DOCKER-USER {\n}\n");
        yield return new ProcessExecutionResult(1, "", "No such file or directory");
    }

    private static ProcessExecutionResult Success(string standardOutput = "") => new(0, standardOutput, "");

    private static string ExpectedBlockedRules => string.Join(
        Environment.NewLine,
        [
            "destroy table inet rsc_network_access",
            "add table inet rsc_network_access",
            "add chain inet rsc_network_access input { type filter hook input priority -1; policy accept; }",
            "add chain inet rsc_network_access prerouting { type nat hook prerouting priority -99; policy accept; }",
            "add chain inet rsc_network_access forward { type filter hook forward priority -1; policy accept; }",
            "add rule inet rsc_network_access input udp dport { 28015, 28017 } drop",
            "add rule inet rsc_network_access forward meta l4proto udp ct original proto-dst { 28015, 28017 } drop",
            "add rule inet rsc_network_access prerouting udp dport 50016 dnat ip to 172.17.0.2:28015",
            "add chain ip filter RSC_NETWORK_ACCESS",
            "flush chain ip filter RSC_NETWORK_ACCESS",
            "add rule ip filter RSC_NETWORK_ACCESS ip daddr 172.17.0.2 udp dport 28015 accept comment \"rustserver-control-network-access\"",
            "insert rule ip filter DOCKER-USER jump RSC_NETWORK_ACCESS comment \"rustserver-control-network-access\"",
            ""
        ]);

    private sealed class RecordingProcessExecutor(IEnumerable<ProcessExecutionResult> results) : IProcessExecutor
    {
        private readonly Queue<ProcessExecutionResult> _results = new(results);
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
