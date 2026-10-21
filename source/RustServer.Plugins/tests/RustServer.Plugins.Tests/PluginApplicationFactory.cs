using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.Plugins.Features.PluginManagement.Guards;

namespace RustServer.Plugins.Tests;

public sealed class PluginApplicationFactory : WebApplicationFactory<global::Program>
{
    private readonly IReadOnlyDictionary<string, string?>? configurationOverrides;
    private readonly bool serverIsRunning;
    private readonly Action<IServiceCollection>? configureServices;

    public PluginApplicationFactory()
    {
    }

    internal PluginApplicationFactory(
        IReadOnlyDictionary<string, string?> configurationOverrides,
        bool serverIsRunning = false,
        Action<IServiceCollection>? configureServices = null)
    {
        this.configurationOverrides = configurationOverrides;
        this.serverIsRunning = serverIsRunning;
        this.configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        if (configurationOverrides is not null)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(configurationOverrides));
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IServerStatus>();
            services.AddSingleton<IServerStatus>(new FixedServerStatus(serverIsRunning));
            configureServices?.Invoke(services);
        });
    }

    private sealed class FixedServerStatus(bool isRunning) : IServerStatus
    {
        public Task<ServerStatusSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServerStatusSnapshot(isRunning, IsReady: false));
    }
}
