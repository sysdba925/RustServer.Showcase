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

public sealed class EmergencyEndpointTests(UpdaterWebApplicationFactory factory)
    : IClassFixture<UpdaterWebApplicationFactory>
{
    [Fact]
    public async Task EmergencyPage_AndLocalAssets_AreServedByUpdater()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/emergency");
        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Updater Console", page, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", page, StringComparison.OrdinalIgnoreCase);
        foreach (var asset in new[] { "console.css", "console.js", "rust.ico" })
        {
            using var assetResponse = await client.GetAsync($"/console/assets/{asset}");
            Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
        }
    }
    [Fact]
    public async Task GetEmergencyState_ReturnsActualChannelAndServerOrderedVersions()
    {
        using var directory = new TemporaryDirectory();
        using var configuredFactory = CreateFactory(directory.Path, new CapturingQueue());
        using var client = configuredFactory.CreateClient();

        using var response = await client.GetAsync("/api/emergency");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Development", document.RootElement.GetProperty("channel").GetString());
        var application = Assert.Single(document.RootElement.GetProperty("applications").EnumerateArray());
        Assert.Equal("RustServer.Updater", application.GetProperty("name").GetString());
        Assert.Equal("v0.3.1-dev.2", application.GetProperty("installedVersion").GetString());
        Assert.Equal(
            ["v0.3.3-dev.1", "v0.3.2-dev.1"],
            application.GetProperty("availableVersions").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task GetEmergencyState_KeepsRollbackAvailableWhenReleaseDiscoveryFails()
    {
        using var directory = new TemporaryDirectory();
        using var configuredFactory = CreateFactory(
            directory.Path,
            new CapturingQueue(),
            availableVersionProvider: new ThrowingAvailableVersionProvider());
        using var client = configuredFactory.CreateClient();

        using var response = await client.GetAsync("/api/emergency");
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var application = Assert.Single(document.RootElement.GetProperty("applications").EnumerateArray());
        Assert.Empty(application.GetProperty("availableVersions").EnumerateArray());
        Assert.Equal("Available release discovery is unavailable.", application.GetProperty("availableVersionsError").GetString());
        Assert.DoesNotContain("test-token", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmergencyUpdate_PreparesVerifiesAndActivatesSelectedReleaseInOneOperation()
    {
        using var directory = new TemporaryDirectory();
        var queue = new CapturingQueue();
        var executor = new SuccessfulExecutor();
        using var configuredFactory = CreateFactory(directory.Path, queue, executor);
        using var client = configuredFactory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/emergency/applications/RustServer.Updater/update",
            new { version = "v0.3.3-dev.1" });
        var payload = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Accepted, payload);
        var operationId = JsonDocument.Parse(payload).RootElement.GetProperty("operationId").GetGuid();
        Assert.Single(queue.Work);

        await queue.Work.Single()(CancellationToken.None);
        var operation = await configuredFactory.Services.GetRequiredService<IOperationManager>()
            .GetAsync(operationId, CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.NotNull(operation.Result);
        Assert.Equal("v0.3.3-dev.1", operation.Result.ReleaseTag);
        Assert.NotNull(operation.Deployment);
        Assert.Equal("v0.3.3-dev.1", operation.Deployment.TargetVersion);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(OperationType.SelfUpdate, request.Type);
        Assert.Equal("v0.3.3-dev.1", request.TargetVersion);
    }

    [Fact]
    public async Task EmergencyUpdate_RejectsAReleaseOutsideTheInstallationChannel()
    {
        using var directory = new TemporaryDirectory();
        var queue = new CapturingQueue();
        using var configuredFactory = CreateFactory(directory.Path, queue);
        using var client = configuredFactory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/emergency/applications/RustServer.Updater/update",
            new { version = "v0.3.3" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(queue.Work);
    }

    [Fact]
    public async Task Rollback_UsesExistingPreviousSuccessfulReleaseSemantics()
    {
        using var directory = new TemporaryDirectory();
        var queue = new CapturingQueue();
        var executor = new SuccessfulExecutor();
        using var configuredFactory = CreateFactory(directory.Path, queue, executor);
        using var client = configuredFactory.CreateClient();

        using var response = await client.PostAsync("/api/applications/RustServer.Updater/rollback", content: null);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Accepted, payload);
        await queue.Work.Single()(CancellationToken.None);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(OperationType.Rollback, request.Type);
        Assert.Null(request.TargetVersion);
    }

    private WebApplicationFactory<Program> CreateFactory(
        string operationDirectory,
        CapturingQueue queue,
        SuccessfulExecutor? executor = null,
        IAvailableVersionProvider? availableVersionProvider = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Operations:StoragePath", operationDirectory);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationRegistry>();
                services.RemoveAll<IInstalledVersionProvider>();
                services.RemoveAll<IAvailableVersionProvider>();
                services.RemoveAll<IBackgroundOperationQueue>();
                services.RemoveAll<IPackagePreparationService>();
                services.RemoveAll<IPreparedPackageLocator>();
                services.RemoveAll<IDeploymentExecutorClient>();
                services.AddSingleton<IApplicationRegistry>(_ => new InMemoryApplicationRegistry(new ApplicationRegistration(
                    "RustServer.Updater",
                    "sysdba925/RustServer.Updater",
                    "/opt/rustserver/updater",
                    "rustserver-updater.service",
                    "RustServer.Updater.Api.dll",
                    new ApplicationReadinessProbe("http://127.0.0.1:8080/health"))));
                services.AddSingleton<IInstalledVersionProvider>(new InstalledVersionProvider("v0.3.1-dev.2"));
                services.AddSingleton<IAvailableVersionProvider>(availableVersionProvider ?? new AvailableVersionProvider(
                    "v0.3.3-dev.1",
                    "v0.3.2-dev.1"));
                services.AddSingleton<IBackgroundOperationQueue>(queue);
                services.AddSingleton<IPackagePreparationService>(new SuccessfulPreparationService(operationDirectory));
                services.AddSingleton<IPreparedPackageLocator>(new SuccessfulPackageLocator());
                services.AddSingleton<IDeploymentExecutorClient>(executor ?? new SuccessfulExecutor());
            });
        });

    private sealed class CapturingQueue : IBackgroundOperationQueue
    {
        public List<Func<CancellationToken, Task>> Work { get; } = [];

        public void Enqueue(Func<CancellationToken, Task> work) => Work.Add(work);
    }

    private sealed class InstalledVersionProvider(string version) : IInstalledVersionProvider
    {
        public string? GetInstalledVersion(ApplicationRegistration application) => version;
    }

    private sealed class AvailableVersionProvider(params string[] versions) : IAvailableVersionProvider
    {
        public Task<string?> GetAvailableVersionAsync(ApplicationRegistration application, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(versions.FirstOrDefault());

        public Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
            ApplicationRegistration application,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(versions);
    }

    private sealed class ThrowingAvailableVersionProvider : IAvailableVersionProvider
    {
        public Task<string?> GetAvailableVersionAsync(ApplicationRegistration application, CancellationToken cancellationToken) =>
            throw new HttpRequestException("GitHub rejected test-token.");

        public Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
            ApplicationRegistration application,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("GitHub rejected test-token.");
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

    private sealed class SuccessfulPackageLocator : IPreparedPackageLocator
    {
        public Task<PackagePreparationResult> GetAsync(
            ApplicationRegistration application,
            string releaseTag,
            CancellationToken cancellationToken) => Task.FromResult(new PackagePreparationResult(
                application.Name,
                releaseTag,
                "/staging/package",
                new string('A', 64),
                DateTimeOffset.UtcNow));
    }

    private sealed class SuccessfulExecutor : IDeploymentExecutorClient
    {
        public List<DeploymentExecutorRequest> Requests { get; } = [];

        public Task<DeploymentTransaction> ExecuteAsync(DeploymentExecutorRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new DeploymentTransaction(
                request.OperationId,
                request.ApplicationName,
                DeploymentTransactionStatus.Succeeded,
                "Deployment completed",
                "v0.3.1-dev.2",
                request.TargetVersion,
                request.TargetVersion,
                null,
                DateTimeOffset.UtcNow));
        }

        public Task<DeploymentTransaction?> GetTransactionAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentTransaction?>(null);
    }
}
