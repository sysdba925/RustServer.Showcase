using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;
using RustServer.Updater.Executor;

namespace RustServer.Updater.Tests;

public sealed class DeploymentExecutorInvariantTests
{
    [Fact]
    public void NormalSystemdStart_DoesNotBypassDependencies()
    {
        var arguments = SystemdController.GetStartArguments(
            "rustserver-example.service",
            SystemdStartMode.Normal);

        Assert.Equal(["start", "rustserver-example.service"], arguments);
        Assert.DoesNotContain("--ignore-dependencies", arguments);
    }

    [Fact]
    public void EnableCommand_RejectsInvalidUnitNames()
    {
        Assert.Throws<ArgumentException>(() =>
            SystemdController.GetStartArguments("../malicious.service", SystemdStartMode.Normal));
    }

    [Fact]
    public void DeploymentStartPolicy_RestrictsDependencyBypassToSelfUpdateAndRecovery()
    {
        Assert.Equal(SystemdStartMode.Normal, DeploymentStartPolicy.For(isPairedSelfUpdate: false, isRecovery: false));
        Assert.Equal(SystemdStartMode.SelfUpdateOrRecovery, DeploymentStartPolicy.For(isPairedSelfUpdate: true, isRecovery: false));
        Assert.Equal(SystemdStartMode.SelfUpdateOrRecovery, DeploymentStartPolicy.For(isPairedSelfUpdate: false, isRecovery: true));
    }

    [Theory]
    [InlineData(SystemdStartMode.SelfUpdateOrRecovery)]
    public void RestrictedSystemdStart_IsTheOnlyModeThatBypassesDependencies(SystemdStartMode mode)
    {
        var arguments = SystemdController.GetStartArguments("rustserver-updater.service", mode);

        Assert.Equal(["start", "--ignore-dependencies", "rustserver-updater.service"], arguments);
    }

    [Fact]
    public async Task Executor_RecordsAFailedTransactionWhenTheDeploymentLockIsHeld()
    {
        using var directory = new TemporaryDirectory();
        var lockPath = Path.Combine(directory.Path, "deployment.lock");
        await using var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var executor = new DeploymentExecutor(
            new RustServer.Updater.Executor.DeploymentExecutorOptions
            {
                RequestStoragePath = Path.Combine(directory.Path, "requests"),
                TransactionStoragePath = Path.Combine(directory.Path, "transactions"),
                PackageStoragePath = Path.Combine(directory.Path, "staging"),
                ApplicationRegistryPath = Path.Combine(directory.Path, "applications.ini"),
                LockPath = lockPath,
                SystemdUnitDirectory = Path.Combine(directory.Path, "systemd")
            },
            new NoOpSystemdController(),
            new NoOpReadinessVerifier());
        var request = new DeploymentExecutorRequest(
            Guid.NewGuid(),
            "Example.Api",
            OperationType.Update,
            "v1.0.0");

        var transaction = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(DeploymentTransactionStatus.Failed, transaction.Status);
        Assert.Equal("Deployment lock unavailable", transaction.Step);
        Assert.Contains("lock", transaction.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(directory.Path, "transactions", $"{request.OperationId:N}.json")));
    }

    [Fact]
    public async Task Stop_MissingSystemdUnit_IsIdempotent()
    {
        if (!OperatingSystem.IsLinux()) return;

        // systemctl returns exit code 5 when the requested unit is absent.
        var controller = new SystemdController("/bin/sh", ["-c", "exit 5", "--"]);
        await controller.StopAsync("rustserver-plugin-catalog.service", CancellationToken.None);
        await Assert.ThrowsAsync<DeploymentExecutionException>(() =>
            controller.StartAsync("rustserver-plugin-catalog.service", SystemdStartMode.Normal, CancellationToken.None));
    }

    private sealed class NoOpSystemdController : ISystemdController
    {
        public Task StopAsync(string unitName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(string unitName, SystemdStartMode mode, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnableAsync(string unitName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisableAsync(string unitName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpReadinessVerifier : IApplicationReadinessVerifier
    {
        public Task VerifyAsync(
            RustServer.Updater.Api.Applications.ApplicationRegistration application,
            string expectedReleaseVersion,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
