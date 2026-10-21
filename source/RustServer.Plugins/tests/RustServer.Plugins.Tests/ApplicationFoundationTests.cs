using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RustServer.Plugins.Infrastructure.Hosting;
using RustServer.Plugins.Infrastructure.OpenApi;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class ApplicationFoundationTests(PluginApplicationFactory factory)
    : IClassFixture<PluginApplicationFactory>
{
    [Fact]
    public async Task Host_StartsAndResponds()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProductionRegistrations_StartHostWithHostedServices()
    {
        var root = Path.Combine(Path.GetTempPath(), "plugins-host-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            await using var host = new WebApplicationFactory<global::Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.ConfigureAppConfiguration((_, configuration) =>
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["PluginManagement:StateRootPath"] = Path.Combine(root, "state"),
                            ["PluginManagement:Runtime:ServerRootPath"] = Path.Combine(root, "server"),
                            ["PluginManagement:Repositories:Plugins:WorkspacePath"] = Path.Combine(root, "plugins"),
                            ["PluginManagement:Repositories:Configs:WorkspacePath"] = Path.Combine(root, "configs"),
                            ["PluginManagement:WorkspaceInitialization:Enabled"] = "false"
                        }));
                });
            using var client = host.CreateClient();
            using var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidDocumentationConfiguration_PreventsHostStartup()
    {
        using var invalidFactory = new PluginApplicationFactory(
            new Dictionary<string, string?>
            {
                ["ApiDocumentation:Title"] = " "
            });

        var exception = Assert.Throws<OptionsValidationException>(invalidFactory.CreateClient);

        Assert.Contains("ApiDocumentation:Title", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RUSTSERVER_PLUGINS_URL", "https://127.0.0.1:5114/path")]
    [InlineData("RUSTSERVER_PLUGINS_BIND_HOST", "localhost")]
    [InlineData("RUSTSERVER_CONTROL_URL", "https://127.0.0.1:5112/path")]
    public void InvalidHostingConfiguration_FailsFast(string key, string value)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["RUSTSERVER_PLUGINS_URL"] = "http://127.0.0.1:5114",
            ["RUSTSERVER_PLUGINS_BIND_HOST"] = "127.0.0.1",
            ["RUSTSERVER_CONTROL_URL"] = "http://127.0.0.1:5112",
            [key] = value
        };
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(configuration);

        var exception = Assert.Throws<InvalidOperationException>(builder.ConfigureRustServerHosting);

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PluginManagement:GitCommitAuthor:Name")]
    [InlineData("PluginManagement:GitCommitAuthor:Email")]
    public void InvalidGitCommitAuthorConfiguration_PreventsHostStartup(string key)
    {
        using var invalidFactory = new PluginApplicationFactory(
            new Dictionary<string, string?>
            {
                [key] = " "
            });

        var exception = Assert.Throws<OptionsValidationException>(invalidFactory.CreateClient);

        Assert.Contains("PluginManagement:GitCommitAuthor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiDocument_IsAvailable()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi.json");

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RustServer.Plugins API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", document.RootElement.GetProperty("info").GetProperty("version").GetString());
    }
}
