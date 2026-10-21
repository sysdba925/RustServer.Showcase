using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Tests;

public sealed class DeploymentEndpointTests(UpdaterWebApplicationFactory factory)
    : IClassFixture<UpdaterWebApplicationFactory>
{
    [Fact]
    public async Task Install_AcceptsVerifiedPackageAndPublishesExecutorConfirmedSuccess()
    {
        using var directory = new TemporaryDirectory();
        var queue = new CapturingQueue();
        var executor = new SuccessfulExecutor();
        using var configuredFactory = CreateFactory(directory.Path, queue, executor, new SuccessfulPackageLocator());
        using var client = configuredFactory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/applications/RustServer.Updater/install",
            new { version = "v1.0.0-dev.2" });
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, payload);
        var operationId = JsonDocument.Parse(payload).RootElement.GetProperty("operationId").GetGuid();
        Assert.Single(queue.Work);

        await queue.Work.Single()(CancellationToken.None);
        var operation = await configuredFactory.Services.GetRequiredService<IOperationManager>()
            .GetAsync(operationId, CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.NotNull(operation.Deployment);
        Assert.Equal("v1.0.0-dev.2", operation.Deployment.TargetVersion);
        Assert.False(operation.Deployment.RolledBack);
        Assert.Equal("Deployment completed", operation.Step);
    }

    [Fact]
    public async Task OperationEndpoint_ReturnsReconciledProvisioningDiagnostic()
    {
        using var directory = new TemporaryDirectory();
        using var configuredFactory = CreateFactory(directory.Path, new CapturingQueue(), new SuccessfulExecutor(), new SuccessfulPackageLocator());
        using var client = configuredFactory.CreateClient();
        var manager = configuredFactory.Services.GetRequiredService<IOperationManager>();
        var store = configuredFactory.Services.GetRequiredService<IOperationStore>();
        var operation = await manager.CreateAsync(new CreateOperationRequest("api", OperationType.Install, "v2.0.0", null), CancellationToken.None);
        await manager.StartAsync(operation.Id, CancellationToken.None);
        const string error = "First-install provisioning failed: stage database; category external_command; reason postgresql_command_failed; exit code 3.";
        var transaction = new DeploymentTransaction(operation.Id, "api", DeploymentTransactionStatus.Failed, "Pre-activation failed", null, "v2.0.0", null, error, DateTimeOffset.UtcNow);
        var reconciler = new DeploymentOperationReconciler(store, manager, new TransactionExecutor(transaction));
        await reconciler.ReconcileAsync(operation, CancellationToken.None);
        using var response = await client.GetAsync($"/api/operations/{operation.Id}");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Update_RejectsMissingVerifiedStagingWithoutQueuingDownloadOrExecutor()
    {
        using var directory = new TemporaryDirectory();
        var queue = new CapturingQueue();
        using var configuredFactory = CreateFactory(directory.Path, queue, new SuccessfulExecutor(), new MissingPackageLocator());
        using var client = configuredFactory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/applications/RustServer.Updater/update",
            new { version = "v1.0.0-dev.2" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(queue.Work);
    }

    [Fact]
    public async Task Reconciler_UsesExecutorRollbackOutcomeInsteadOfAssumingSubmittedCommandSucceeded()
    {
        var store = new InMemoryOperationStore();
        var manager = new OperationManager(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<OperationManager>.Instance);
        var operation = await manager.CreateAsync(
            new CreateOperationRequest("api", OperationType.Update, "v2.0.0", "v1.0.0"),
            CancellationToken.None);
        await manager.StartAsync(operation.Id, CancellationToken.None);
        var transaction = new DeploymentTransaction(
            operation.Id,
            "api",
            DeploymentTransactionStatus.RolledBack,
            "Rollback completed",
            "v1.0.0",
            "v2.0.0",
            "v1.0.0",
            "Target service did not pass readiness",
            DateTimeOffset.UtcNow);
        var reconciler = new DeploymentOperationReconciler(store, manager, new TransactionExecutor(transaction));

        var found = await reconciler.ReconcileAsync(operation, CancellationToken.None);
        var reconciled = await manager.GetAsync(operation.Id, CancellationToken.None);

        Assert.True(found);
        Assert.NotNull(reconciled);
        Assert.Equal(OperationStatus.Failed, reconciled.Status);
        Assert.NotNull(reconciled.Deployment);
        Assert.True(reconciled.Deployment.RolledBack);
        Assert.Equal("v1.0.0", reconciled.Deployment.ActiveVersion);
    }

    [Fact]
    public async Task Reconciler_RecordsSelfUpdateRollbackWithThePriorBundleStillActive()
    {
        var store = new InMemoryOperationStore();
        var manager = new OperationManager(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<OperationManager>.Instance);
        var operation = await manager.CreateAsync(
            new CreateOperationRequest("RustServer.Updater", OperationType.SelfUpdate, "v2.0.0", "v1.0.0"),
            CancellationToken.None);
        await manager.StartAsync(operation.Id, CancellationToken.None);
        var transaction = new DeploymentTransaction(
            operation.Id,
            "RustServer.Updater",
            DeploymentTransactionStatus.RolledBack,
            "Rollback completed",
            "v1.0.0",
            "v2.0.0",
            "v1.0.0",
            "Paired bundle readiness failed",
            DateTimeOffset.UtcNow);
        var reconciler = new DeploymentOperationReconciler(store, manager, new TransactionExecutor(transaction));

        await reconciler.ReconcileAsync(operation, CancellationToken.None);
        var reconciled = await manager.GetAsync(operation.Id, CancellationToken.None);

        Assert.NotNull(reconciled);
        Assert.Equal(OperationStatus.Failed, reconciled.Status);
        Assert.NotNull(reconciled.Deployment);
        Assert.True(reconciled.Deployment.RolledBack);
        Assert.Equal("v1.0.0", reconciled.Deployment.ActiveVersion);
    }

    private WebApplicationFactory<Program> CreateFactory(
        string operationDirectory,
        CapturingQueue queue,
        IDeploymentExecutorClient executor,
        IPreparedPackageLocator packageLocator) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Operations:StoragePath", operationDirectory);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationRegistry>();
                services.RemoveAll<IBackgroundOperationQueue>();
                services.RemoveAll<IPreparedPackageLocator>();
                services.RemoveAll<IDeploymentExecutorClient>();
                services.AddSingleton<IApplicationRegistry>(_ => new InMemoryApplicationRegistry(new ApplicationRegistration(
                    "RustServer.Updater",
                    "sysdba925/RustServer.Updater",
                    "/opt/rustserver/updater",
                    "rustserver-updater.service",
                    "RustServer.Updater.Api.dll",
                    new ApplicationReadinessProbe("http://127.0.0.1:8080/health"))));
                services.AddSingleton<IBackgroundOperationQueue>(queue);
                services.AddSingleton(packageLocator);
                services.AddSingleton(executor);
            });
        });

    private sealed class CapturingQueue : IBackgroundOperationQueue
    {
        public List<Func<CancellationToken, Task>> Work { get; } = [];

        public void Enqueue(Func<CancellationToken, Task> work) => Work.Add(work);
    }

    private sealed class SuccessfulPackageLocator : IPreparedPackageLocator
    {
        public Task<PackagePreparationResult> GetAsync(ApplicationRegistration application, string releaseTag, CancellationToken cancellationToken) =>
            Task.FromResult(new PackagePreparationResult(application.Name, releaseTag, "/staging/package", new string('A', 64), DateTimeOffset.UtcNow));
    }

    private sealed class MissingPackageLocator : IPreparedPackageLocator
    {
        public Task<PackagePreparationResult> GetAsync(ApplicationRegistration application, string releaseTag, CancellationToken cancellationToken) =>
            throw new PreparedPackageNotFoundException("No verified staging package exists.");
    }

    private sealed class SuccessfulExecutor : IDeploymentExecutorClient
    {
        public Task<DeploymentTransaction> ExecuteAsync(DeploymentExecutorRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DeploymentTransaction(
                request.OperationId,
                request.ApplicationName,
                DeploymentTransactionStatus.Succeeded,
                "Deployment completed",
                null,
                request.TargetVersion,
                request.TargetVersion,
                null,
                DateTimeOffset.UtcNow));

        public Task<DeploymentTransaction?> GetTransactionAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentTransaction?>(null);
    }

    private sealed class TransactionExecutor(DeploymentTransaction transaction) : IDeploymentExecutorClient
    {
        public Task<DeploymentTransaction> ExecuteAsync(DeploymentExecutorRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(transaction);

        public Task<DeploymentTransaction?> GetTransactionAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentTransaction?>(operationId == transaction.OperationId ? transaction : null);
    }
}
