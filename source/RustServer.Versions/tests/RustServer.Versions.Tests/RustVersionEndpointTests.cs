using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.Versions.Api.Versions;
using System.Net;
using System.Text.Json;

namespace RustServer.Versions.Tests;

public sealed class HealthEndpointTests(RustVersionsApiFactory factory) : IClassFixture<RustVersionsApiFactory>
{
    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class RustVersionEndpointTests(RustVersionsApiFactory factory) : IClassFixture<RustVersionsApiFactory>
{
    [Fact]
    public async Task GetRustVersion_ReturnsExpectedContract()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/versions/rust");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(5, root.EnumerateObject().Count());
        Assert.Equal("public", root.GetProperty("branch").GetString());
        Assert.Equal(12345678UL, root.GetProperty("installedBuildId").GetUInt64());
        var running = root.GetProperty("running");
        Assert.Equal(JsonValueKind.Object, running.ValueKind);
        Assert.Equal("2633.288.1", running.GetProperty("protocol").GetString());
        Assert.Equal("2026-09-22T07:48:10", running.GetProperty("buildDate").GetString());
        Assert.Equal("6000.3.15x1-11", running.GetProperty("unityVersion").GetString());
        Assert.Equal(165217, running.GetProperty("changeset").GetInt32());
        Assert.Equal("release", running.GetProperty("branch").GetString());
        Assert.Equal(87654321UL, root.GetProperty("availableBuildId").GetUInt64());
        Assert.True(root.GetProperty("updateAvailable").GetBoolean());
    }
}

public sealed class StoppedRustVersionEndpointTests(StoppedRustVersionsApiFactory factory)
    : IClassFixture<StoppedRustVersionsApiFactory>
{
    [Fact]
    public async Task GetRustVersion_ReturnsBuildIdsWhenTheServerIsStopped()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/versions/rust");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var root = document.RootElement;

        Assert.Equal("public", root.GetProperty("branch").GetString());
        Assert.Equal(12345678UL, root.GetProperty("installedBuildId").GetUInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("running").ValueKind);
        Assert.Equal(12345678UL, root.GetProperty("availableBuildId").GetUInt64());
        Assert.False(root.GetProperty("updateAvailable").GetBoolean());
    }
}

public sealed class OxideVersionEndpointTests(RustVersionsApiFactory factory) : IClassFixture<RustVersionsApiFactory>
{
    [Fact]
    public async Task GetOxideVersion_ReturnsExpectedContract()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/versions/oxide");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("2.0.7726", root.GetProperty("installedVersion").GetString());
        Assert.Equal("2.0.7723", root.GetProperty("runningVersion").GetString());
        Assert.Equal("2.0.7726", root.GetProperty("availableVersion").GetString());
        Assert.False(root.GetProperty("updateAvailable").GetBoolean());
    }
}

public sealed class StoppedOxideVersionEndpointTests(StoppedOxideVersionsApiFactory factory)
    : IClassFixture<StoppedOxideVersionsApiFactory>
{
    [Fact]
    public async Task GetOxideVersion_ReturnsInstalledAndAvailableVersionsWhenServerIsStopped()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/versions/oxide");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var root = document.RootElement;

        Assert.Equal("2.0.7726", root.GetProperty("installedVersion").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("runningVersion").ValueKind);
        Assert.Equal("2.0.7726", root.GetProperty("availableVersion").GetString());
        Assert.False(root.GetProperty("updateAvailable").GetBoolean());
    }
}

public sealed class UninstalledRustVersionEndpointTests(UninstalledRustVersionsApiFactory factory)
    : IClassFixture<UninstalledRustVersionsApiFactory>
{
    [Fact]
    public async Task GetRustVersion_ReturnsNullableInstalledState()
    {
        using var response = await factory.CreateClient().GetAsync("/api/versions/rust");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("installedBuildId").ValueKind);
        Assert.Equal(12345678UL, root.GetProperty("availableBuildId").GetUInt64());
        Assert.True(root.GetProperty("updateAvailable").GetBoolean());
    }
}

public sealed class UninstalledOxideVersionEndpointTests(UninstalledOxideVersionsApiFactory factory)
    : IClassFixture<UninstalledOxideVersionsApiFactory>
{
    [Fact]
    public async Task GetOxideVersion_ReturnsNullableInstalledState()
    {
        using var response = await factory.CreateClient().GetAsync("/api/versions/oxide");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("installedVersion").ValueKind);
        Assert.Equal("2.0.7726", root.GetProperty("availableVersion").GetString());
        Assert.True(root.GetProperty("updateAvailable").GetBoolean());
    }
}

public sealed class RustVersionsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRustVersionProvider>();
            services.AddSingleton<IRustVersionProvider>(new TestRustVersionProvider());
            services.RemoveAll<IOxideVersionProvider>();
            services.AddSingleton<IOxideVersionProvider>(new TestOxideVersionProvider());
        });
    }

    private sealed class TestRustVersionProvider : IRustVersionProvider
    {
        public Task<RustVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new RustVersionInfo(
                "public",
                12345678,
                new RustRunningVersion(
                    "2633.288.1",
                    new DateTime(2026, 9, 22, 7, 48, 10),
                    "6000.3.15x1-11",
                    165217,
                    "release"),
                87654321,
                true));
        }
    }

    private sealed class TestOxideVersionProvider : IOxideVersionProvider
    {
        public Task<OxideVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new OxideVersionInfo(
                "2.0.7726",
                "2.0.7723",
                "2.0.7726",
                false));
        }
    }
}

public sealed class StoppedRustVersionsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRustVersionProvider>();
            services.AddSingleton<IRustVersionProvider>(new StoppedRustVersionProvider());
        });
    }

    private sealed class StoppedRustVersionProvider : IRustVersionProvider
    {
        public Task<RustVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new RustVersionInfo(
                "public",
                12345678,
                null,
                12345678,
                false));
        }
    }
}

public sealed class StoppedOxideVersionsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOxideVersionProvider>();
            services.AddSingleton<IOxideVersionProvider>(new StoppedOxideVersionProvider());
        });
    }

    private sealed class StoppedOxideVersionProvider : IOxideVersionProvider
    {
        public Task<OxideVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new OxideVersionInfo(
                "2.0.7726",
                null,
                "2.0.7726",
                false));
        }
    }
}

public sealed class UninstalledRustVersionsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRustVersionProvider>();
            services.AddSingleton<IRustVersionProvider>(new UninstalledRustVersionProvider());
        });
    }

    private sealed class UninstalledRustVersionProvider : IRustVersionProvider
    {
        public Task<RustVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RustVersionInfo("public", null, null, 12345678UL, true));
    }
}

public sealed class UninstalledOxideVersionsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOxideVersionProvider>();
            services.AddSingleton<IOxideVersionProvider>(new UninstalledOxideVersionProvider());
        });
    }

    private sealed class UninstalledOxideVersionProvider : IOxideVersionProvider
    {
        public Task<OxideVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OxideVersionInfo(null, null, "2.0.7726", true));
    }
}
