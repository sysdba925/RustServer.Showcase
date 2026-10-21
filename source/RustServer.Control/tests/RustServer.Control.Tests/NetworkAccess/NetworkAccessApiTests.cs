using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RustServer.Control.Api.NetworkAccess;
using RustServer.Control.Application.NetworkAccess;
using RustServer.Control.Domain.NetworkAccess;

namespace RustServer.Control.Tests.NetworkAccess;

public sealed class NetworkAccessApiTests
{
    [Fact]
    public async Task PutNetworkAccess_RejectsReservedMaintenancePortWithoutApplyingRules()
    {
        var applier = new RecordingRuleApplier();
        using var factory = new NetworkAccessApiFactory(
            new InMemorySettingsStore(NetworkAccessSettings.Default),
            applier);
        using var client = factory.CreateClient();
        applier.Reset();

        var response = await client.PutAsJsonAsync(
            "/api/server/network-access",
            new UpdateNetworkAccessRequest(PublicEnabled: false, MaintenancePort: NetworkAccessSettings.GamePort));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(applier.Applied);
    }

    [Fact]
    public async Task PutNetworkAccess_AppliesRulesBeforePersistingNewMode()
    {
        var store = new InMemorySettingsStore(NetworkAccessSettings.Default);
        var applier = new RecordingRuleApplier();
        using var factory = new NetworkAccessApiFactory(store, applier);
        using var client = factory.CreateClient();
        applier.Reset();
        var requested = new UpdateNetworkAccessRequest(PublicEnabled: false, MaintenancePort: 50_016);

        var response = await client.PutAsJsonAsync("/api/server/network-access", requested);
        var value = await response.Content.ReadFromJsonAsync<NetworkAccessResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new NetworkAccessResponse(false, 50_016), value);
        Assert.Equal(new NetworkAccessSettings(false, 50_016), store.Get());
        Assert.Equal([new NetworkAccessSettings(false, 50_016)], applier.Applied);
    }

    [Fact]
    public async Task PutNetworkAccess_ReturnsFailureAndKeepsPersistedSettingsWhenHelperIsUnavailable()
    {
        var current = NetworkAccessSettings.Default;
        var store = new InMemorySettingsStore(current);
        var applier = new RecordingRuleApplier
        {
            Failure = new NetworkAccessRuleApplicationException(
                NetworkAccessRuleFailureKind.HelperUnavailable,
                "The privileged helper is unavailable")
        };
        using var factory = new NetworkAccessApiFactory(store, applier);
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/server/network-access",
            new UpdateNetworkAccessRequest(PublicEnabled: false, MaintenancePort: 50_016));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(current, store.Get());
    }

    [Fact]
    public async Task PutNetworkAccess_ReturnsSafeStructuredReasonWhenHelperUnavailable()
    {
        var store = new InMemorySettingsStore(NetworkAccessSettings.Default);
        var applier = new RecordingRuleApplier
        {
            Failure = new NetworkAccessRuleApplicationException(
                NetworkAccessRuleFailureKind.HelperUnavailable,
                "untrusted root process diagnostic")
        };
        using var factory = new NetworkAccessApiFactory(store, applier);
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/server/network-access",
            new UpdateNetworkAccessRequest(false, 50_015));
        var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("HelperUnavailable", payload.GetProperty("code").GetString());
        Assert.Contains("systemd", payload.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("untrusted root", payload.GetRawText());
    }

    private sealed class NetworkAccessApiFactory(
        INetworkAccessSettingsStore settingsStore,
        INetworkAccessRuleApplier ruleApplier) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INetworkAccessSettingsStore>();
                services.RemoveAll<INetworkAccessRuleApplier>();
                services.AddSingleton(settingsStore);
                services.AddSingleton(ruleApplier);
            });
        }
    }

    private sealed class InMemorySettingsStore(NetworkAccessSettings settings) : INetworkAccessSettingsStore
    {
        private NetworkAccessSettings _settings = settings;

        public NetworkAccessSettings Get() => _settings;

        public void Save(NetworkAccessSettings settings) => _settings = settings;
    }

    private sealed class RecordingRuleApplier : INetworkAccessRuleApplier
    {
        public List<NetworkAccessSettings> Applied { get; } = [];

        public NetworkAccessRuleApplicationException? Failure { get; init; }

        public Task ApplyAsync(NetworkAccessSettings settings, CancellationToken cancellationToken = default)
        {
            Applied.Add(settings);
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }

        public void Reset() => Applied.Clear();
    }
}
