using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

public sealed class RefreshApplicationsHandlerTests
{
    [Fact]
    public async Task RefreshLocalState_RecordsInstalledAndNotInstalledApplications()
    {
        var registry = CreateRegistry(
            CreateOptions("RustServer.Updater"),
            CreateOptions("RustServer.Other"));
        var installedVersionProvider = new StubInstalledVersionProvider(application =>
            application.Name == "RustServer.Updater" ? "v1.0.0" : null);
        var statusProvider = new StubApplicationStatusProvider((_, isInstalled) =>
            Task.FromResult(isInstalled ? ApplicationStatus.Running : ApplicationStatus.NotInstalled));
        var localRefresh = new RefreshLocalApplicationStateHandler(registry, installedVersionProvider, statusProvider);

        await localRefresh.HandleAsync(CancellationToken.None);

        Assert.Equal("v1.0.0", registry.GetSnapshotByName("RustServer.Updater")!.InstalledVersion);
        Assert.Equal(ApplicationStatus.Running, registry.GetSnapshotByName("RustServer.Updater")!.Status);
        Assert.Null(registry.GetSnapshotByName("RustServer.Other")!.InstalledVersion);
        Assert.Equal(ApplicationStatus.NotInstalled, registry.GetSnapshotByName("RustServer.Other")!.Status);
    }

    [Fact]
    public async Task HandleAsync_RefreshesLocalStateAndAvailableVersion()
    {
        var registry = CreateRegistry(CreateOptions("RustServer.Updater"));
        var handler = CreateRefreshHandler(
            registry,
            new StubInstalledVersionProvider(_ => "v1.0.0"),
            new StubApplicationStatusProvider((_, _) => Task.FromResult(ApplicationStatus.Running)),
            new StubAvailableVersionProvider(_ => Task.FromResult<string?>("v1.0.1")));

        var response = Assert.Single(await handler.HandleAsync(CancellationToken.None));
        var snapshot = registry.GetSnapshotByName("RustServer.Updater");

        Assert.Equal("v1.0.0", response.InstalledVersion);
        Assert.Equal("v1.0.1", response.AvailableVersion);
        Assert.True(response.UpdateAvailable);
        Assert.NotNull(snapshot);
        Assert.Equal(ApplicationStatus.Running, snapshot.Status);
    }

    [Fact]
    public async Task HandleAsync_PreservesPreviousAvailableVersionWhenGitHubFails()
    {
        var registry = CreateRegistry(CreateOptions("RustServer.Updater"));
        var availableVersionProvider = new StubAvailableVersionProvider(_ => Task.FromResult<string?>("v1.0.1"));
        var handler = CreateRefreshHandler(
            registry,
            new StubInstalledVersionProvider(_ => "v1.0.0"),
            new StubApplicationStatusProvider((_, _) => Task.FromResult(ApplicationStatus.Running)),
            availableVersionProvider);

        await handler.HandleAsync(CancellationToken.None);
        availableVersionProvider.GetVersionAsync = _ => Task.FromException<string?>(new HttpRequestException());

        var response = Assert.Single(await handler.HandleAsync(CancellationToken.None));

        Assert.Equal("v1.0.1", response.AvailableVersion);
        Assert.Equal("v1.0.1", registry.GetSnapshotByName("RustServer.Updater")!.AvailableVersion);
    }

    [Fact]
    public async Task HandleAsync_SerializesConcurrentRefreshes()
    {
        var registry = CreateRegistry(CreateOptions("RustServer.Updater"));
        var concurrentCalls = 0;
        var maximumConcurrentCalls = 0;
        var availableVersionProvider = new StubAvailableVersionProvider(async _ =>
        {
            var currentCalls = Interlocked.Increment(ref concurrentCalls);
            maximumConcurrentCalls = Math.Max(maximumConcurrentCalls, currentCalls);
            await Task.Delay(50);
            Interlocked.Decrement(ref concurrentCalls);
            return "v1.0.1";
        });
        var handler = CreateRefreshHandler(
            registry,
            new StubInstalledVersionProvider(_ => "v1.0.0"),
            new StubApplicationStatusProvider((_, _) => Task.FromResult(ApplicationStatus.Running)),
            availableVersionProvider);

        await Task.WhenAll(
            handler.HandleAsync(CancellationToken.None),
            handler.HandleAsync(CancellationToken.None),
            handler.HandleAsync(CancellationToken.None));

        Assert.Equal(1, maximumConcurrentCalls);
        Assert.Equal(3, availableVersionProvider.CallCount);
        Assert.Equal("v1.0.1", registry.GetSnapshotByName("RustServer.Updater")!.AvailableVersion);
    }

    private static RefreshApplicationsHandler CreateRefreshHandler(
        IApplicationRegistry registry,
        IInstalledVersionProvider installedVersionProvider,
        IApplicationStatusProvider statusProvider,
        IAvailableVersionProvider availableVersionProvider)
    {
        var localRefresh = new RefreshLocalApplicationStateHandler(registry, installedVersionProvider, statusProvider);
        return new RefreshApplicationsHandler(
            registry,
            localRefresh,
            availableVersionProvider,
            NullLogger<RefreshApplicationsHandler>.Instance);
    }

    private static InMemoryApplicationRegistry CreateRegistry(params ApplicationRegistration[] applications) => new(applications);

    private static ApplicationRegistration CreateOptions(string name) => new(
        name,
        "owner/repository",
        $"/opt/rustserver/{name}",
        $"{name}.service",
        $"{name}.dll");

    private sealed class StubInstalledVersionProvider(Func<ApplicationRegistration, string?> getVersion)
        : IInstalledVersionProvider
    {
        public string? GetInstalledVersion(ApplicationRegistration application) => getVersion(application);
    }

    private sealed class StubApplicationStatusProvider(
        Func<ApplicationRegistration, bool, Task<ApplicationStatus>> getStatus)
        : IApplicationStatusProvider
    {
        public Task<ApplicationStatus> GetStatusAsync(
            ApplicationRegistration application,
            bool isInstalled,
            CancellationToken cancellationToken) => getStatus(application, isInstalled);
    }

    private sealed class StubAvailableVersionProvider(Func<ApplicationRegistration, Task<string?>> getVersionAsync)
        : IAvailableVersionProvider
    {
        public Func<ApplicationRegistration, Task<string?>> GetVersionAsync { get; set; } = getVersionAsync;

        public int CallCount { get; private set; }

        public Task<string?> GetAvailableVersionAsync(ApplicationRegistration application, CancellationToken cancellationToken)
        {
            CallCount++;
            return GetVersionAsync(application);
        }
    }
}
