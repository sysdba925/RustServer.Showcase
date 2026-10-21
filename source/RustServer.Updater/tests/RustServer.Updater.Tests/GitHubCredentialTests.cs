using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

public sealed class GitHubCredentialTests
{
    [Fact]
    public void SystemdTokenProvider_ReportsAMissingCredentialWithoutDisclosingAPath()
    {
        var original = Environment.GetEnvironmentVariable(SystemdGitHubTokenProvider.CredentialPathEnvironmentVariable);
        Environment.SetEnvironmentVariable(SystemdGitHubTokenProvider.CredentialPathEnvironmentVariable, null);
        try
        {
            var exception = Assert.Throws<GitHubCredentialUnavailableException>(() => new SystemdGitHubTokenProvider().GetToken());

            Assert.DoesNotContain("/etc", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("token", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemdGitHubTokenProvider.CredentialPathEnvironmentVariable, original);
        }
    }

    [Fact]
    public async Task Refresh_PreservesThePreviousSnapshotWhenCredentialIsUnavailable()
    {
        var registry = new InMemoryApplicationRegistry(new ApplicationRegistration(
            "RustServer.Updater",
            "sysdba925/RustServer.Updater",
            "/opt/rustserver/updater",
            "rustserver-updater.service",
            "RustServer.Updater.Api.dll"));
        registry.ReplaceSnapshots(
        [
            new ApplicationSnapshot("RustServer.Updater", "v1.0.0", "v1.0.1", ApplicationStatus.Running)
        ]);
        var handler = new RefreshApplicationsHandler(
            registry,
            new RefreshLocalApplicationStateHandler(registry, new NoOpInstalledVersionProvider(), new RunningStatusProvider()),
            new CredentialUnavailableVersionProvider(),
            NullLogger<RefreshApplicationsHandler>.Instance);

        await Assert.ThrowsAsync<GitHubRefreshUnavailableException>(() => handler.HandleAsync(CancellationToken.None));

        Assert.Equal("v1.0.1", registry.GetSnapshotByName("RustServer.Updater")!.AvailableVersion);
    }

    private sealed class NoOpInstalledVersionProvider : IInstalledVersionProvider
    {
        public string? GetInstalledVersion(ApplicationRegistration application) => "v1.0.0";
    }

    private sealed class RunningStatusProvider : IApplicationStatusProvider
    {
        public Task<ApplicationStatus> GetStatusAsync(
            ApplicationRegistration application,
            bool isInstalled,
            CancellationToken cancellationToken) => Task.FromResult(ApplicationStatus.Running);
    }

    private sealed class CredentialUnavailableVersionProvider : IAvailableVersionProvider
    {
        public Task<string?> GetAvailableVersionAsync(ApplicationRegistration application, CancellationToken cancellationToken) =>
            Task.FromException<string?>(new GitHubCredentialUnavailableException("missing"));
    }
}
