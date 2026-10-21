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

public sealed class PrepareEndpointTests(UpdaterWebApplicationFactory factory)
    : IClassFixture<UpdaterWebApplicationFactory>
{
    [Fact]
    public async Task Prepare_AcceptsDurableOperationAndReturnsItsIdentifier()
    {
        using var directory = new TemporaryDirectory();
        var queue = new CapturingQueue();
        var executor = new SecuringExecutor();
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Operations:StoragePath", directory.Path);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBackgroundOperationQueue>();
                services.RemoveAll<IPackagePreparationService>();
                services.RemoveAll<IDeploymentExecutorClient>();
                services.AddSingleton<IBackgroundOperationQueue>(queue);
                services.AddSingleton<IPackagePreparationService>(new SuccessfulPreparationService(directory.Path));
                services.AddSingleton<IDeploymentExecutorClient>(executor);
            });
        });
        using var client = configuredFactory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/applications/RustServer.Updater/prepare",
            new { version = "v0.1.0-dev.2" });
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, responseContent);
        using var document = JsonDocument.Parse(responseContent);
        var operationId = document.RootElement.GetProperty("operationId").GetGuid();
        Assert.NotEqual(Guid.Empty, operationId);
        Assert.Single(queue.Work);

        await queue.Work.Single()(CancellationToken.None);
        var manager = configuredFactory.Services.GetRequiredService<IOperationManager>();
        var operation = await manager.GetAsync(operationId, CancellationToken.None);
        Assert.NotNull(operation);
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.Equal("Verified package secured", operation.Step);
        Assert.NotNull(operation.Result);
        Assert.Equal("v0.1.0-dev.2", operation.Result.ReleaseTag);
        var handoff = Assert.Single(executor.Requests);
        Assert.Equal(OperationType.Prepare, handoff.Type);
        Assert.Equal("RustServer.Updater", handoff.ApplicationName);
        Assert.Equal("v0.1.0-dev.2", handoff.TargetVersion);
    }

    private sealed class SecuringExecutor : IDeploymentExecutorClient
    {
        public List<DeploymentExecutorRequest> Requests { get; } = [];

        public Task<DeploymentTransaction> ExecuteAsync(
            DeploymentExecutorRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new DeploymentTransaction(
                request.OperationId,
                request.ApplicationName,
                DeploymentTransactionStatus.Succeeded,
                "Verified package secured",
                null,
                request.TargetVersion,
                null,
                null,
                DateTimeOffset.UtcNow));
        }

        public Task<DeploymentTransaction?> GetTransactionAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentTransaction?>(null);
    }

    private sealed class CapturingQueue : IBackgroundOperationQueue
    {
        public List<Func<CancellationToken, Task>> Work { get; } = [];

        public void Enqueue(Func<CancellationToken, Task> work) => Work.Add(work);
    }

    private sealed class SuccessfulPreparationService(string stagingRoot) : IPackagePreparationService
    {
        public async Task<PackagePreparationResult> PrepareAsync(
            ApplicationRegistration application,
            string releaseTag,
            Func<string, CancellationToken, Task> reportStepAsync,
            CancellationToken cancellationToken)
        {
            foreach (var step in new[] { "Resolving", "Downloading", "Verifying", "Extracting", "Staged" })
            {
                await reportStepAsync(step, cancellationToken);
            }

            return new PackagePreparationResult(
                application.Name,
                releaseTag,
                Path.Combine(stagingRoot, application.Name, releaseTag),
                new string('A', 64),
                DateTimeOffset.UtcNow);
        }
    }
}
