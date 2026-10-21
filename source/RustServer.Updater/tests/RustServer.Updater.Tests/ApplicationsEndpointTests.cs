using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

public sealed class ApplicationsEndpointTests(UpdaterWebApplicationFactory factory)
    : IClassFixture<UpdaterWebApplicationFactory>
{
    [Fact]
    public async Task GetApplications_ReturnsAllRegisteredApplications()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/applications");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var applications = document.RootElement;
        Assert.Equal(JsonValueKind.Array, applications.ValueKind);
        Assert.Single(applications.EnumerateArray());
        Assert.Equal("RustServer.Updater", applications[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetApplication_ReturnsRegisteredApplication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/applications/RustServer.Updater");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RustServer.Updater", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetApplication_ReturnsNotFoundForUnknownApplication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/applications/unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetApplication_ReturnsExpectedResponseDto()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/applications/RustServer.Updater");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var application = document.RootElement;

        Assert.Equal(JsonValueKind.Object, application.ValueKind);
        Assert.Equal("RustServer.Updater", application.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, application.GetProperty("installedVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, application.GetProperty("availableVersion").ValueKind);
        Assert.Equal("NotInstalled", application.GetProperty("status").GetString());
        Assert.False(application.GetProperty("updateAvailable").GetBoolean());
    }

    [Fact]
    public async Task GetEndpoints_ReturnLastRefreshSnapshot()
    {
        using var refreshedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IInstalledVersionProvider>();
                services.RemoveAll<IApplicationStatusProvider>();
                services.RemoveAll<IAvailableVersionProvider>();
                services.AddSingleton<IInstalledVersionProvider>(new StubInstalledVersionProvider("v1.0.0"));
                services.AddSingleton<IApplicationStatusProvider>(new StubApplicationStatusProvider(ApplicationStatus.Running));
                services.AddSingleton<IAvailableVersionProvider>(new StubAvailableVersionProvider("v1.0.1"));
            }));
        using var client = refreshedFactory.CreateClient();

        using var refreshResponse = await client.PostAsync("/api/applications/refresh", content: null);
        using var allResponse = await client.GetAsync("/api/applications");
        using var oneResponse = await client.GetAsync("/api/applications/RustServer.Updater");
        using var allDocument = JsonDocument.Parse(await allResponse.Content.ReadAsStringAsync());
        using var oneDocument = JsonDocument.Parse(await oneResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oneResponse.StatusCode);
        Assert.Equal("v1.0.0", allDocument.RootElement[0].GetProperty("installedVersion").GetString());
        Assert.Equal("v1.0.1", allDocument.RootElement[0].GetProperty("availableVersion").GetString());
        Assert.Equal("Running", allDocument.RootElement[0].GetProperty("status").GetString());
        Assert.True(allDocument.RootElement[0].GetProperty("updateAvailable").GetBoolean());
        Assert.Equal("v1.0.1", oneDocument.RootElement.GetProperty("availableVersion").GetString());
    }

    [Fact]
    public async Task Refresh_ReturnsServiceUnavailableWhenTheIniCannotBeValidated()
    {
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationRegistry>();
                services.AddSingleton<IApplicationRegistry>(new InvalidReloadRegistry());
            }));
        using var client = configuredFactory.CreateClient();

        using var response = await client.PostAsync("/api/applications/refresh", content: null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Application registry configuration is invalid", document.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task RegistrationEndpoints_AreReadOnly()
    {
        using var client = factory.CreateClient();
        using var createResponse = await client.PostAsync("/api/applications", content: null);
        using var registrationsResponse = await client.GetAsync("/api/applications/registrations");
        using var registrations = JsonDocument.Parse(await registrationsResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.MethodNotAllowed, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, registrationsResponse.StatusCode);
        Assert.Single(registrations.RootElement.EnumerateArray());
    }

    private sealed class StubInstalledVersionProvider(string version) : IInstalledVersionProvider
    {
        public string? GetInstalledVersion(ApplicationRegistration application) => version;
    }

    private sealed class StubApplicationStatusProvider(ApplicationStatus status) : IApplicationStatusProvider
    {
        public Task<ApplicationStatus> GetStatusAsync(
            ApplicationRegistration application,
            bool isInstalled,
            CancellationToken cancellationToken) => Task.FromResult(status);
    }

    private sealed class StubAvailableVersionProvider(string version) : IAvailableVersionProvider
    {
        public Task<string?> GetAvailableVersionAsync(ApplicationRegistration application, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(version);
    }

    private sealed class InvalidReloadRegistry : IApplicationRegistry
    {
        private readonly InMemoryApplicationRegistry _inner = new(new ApplicationRegistration(
            "RustServer.Updater",
            "sysdba925/RustServer.Updater",
            "/opt/rustserver/updater",
            "rustserver-updater.service",
            "RustServer.Updater.Api.dll"));

        public IReadOnlyCollection<ApplicationRegistration> GetAll() => _inner.GetAll();

        public ApplicationRegistration? GetByName(string name) => _inner.GetByName(name);

        public IReadOnlyCollection<ApplicationSnapshot> GetSnapshots() => _inner.GetSnapshots();

        public ApplicationSnapshot? GetSnapshotByName(string name) => _inner.GetSnapshotByName(name);

        public void ReplaceSnapshots(IReadOnlyCollection<ApplicationSnapshot> snapshots) => _inner.ReplaceSnapshots(snapshots);

        public void Reload() => throw new ApplicationRegistryConfigurationException("HealthPort must be an integer from 1 through 65535.");

        public void EnsureConfigurationIsCurrent() => Reload();
    }
}
