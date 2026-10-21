using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class EnvironmentApiTests
{
    [Fact]
    public async Task FirstStart_ReturnsStagingAndUninitializedWithoutCreatingState()
    {
        using var stateRoot = new TemporaryStateRoot();
        using var factory = CreateFactory(stateRoot.Path);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/environment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(stateRoot.Path, "environment.json")));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("staging", document.RootElement.GetProperty("environment").GetString());
        Assert.Equal("uninitialized", document.RootElement.GetProperty("systemState").GetString());
    }

    [Fact]
    public async Task SwitchToProduction_PersistsAcrossApplicationRestart()
    {
        using var stateRoot = new TemporaryStateRoot();

        using (var initialFactory = CreateFactory(stateRoot.Path))
        using (var initialClient = initialFactory.CreateClient())
        using (var request = new StringContent(
                   "{\"environment\":\"production\"}",
                   Encoding.UTF8,
                   "application/json"))
        using (var response = await initialClient.PutAsync("/api/environment", request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var restartedFactory = CreateFactory(stateRoot.Path);
        using var restartedClient = restartedFactory.CreateClient();
        using var restartedResponse = await restartedClient.GetAsync("/api/environment");

        restartedResponse.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await restartedResponse.Content.ReadAsStringAsync());
        Assert.Equal("production", document.RootElement.GetProperty("environment").GetString());
    }

    private static PluginApplicationFactory CreateFactory(string stateRootPath) =>
        new(new Dictionary<string, string?>
        {
            ["PluginManagement:StateRootPath"] = stateRootPath
        });

    private sealed class TemporaryStateRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "RustServer.Plugins.Tests",
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
