using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

/// <summary>Supplies an isolated registry because production startup requires runtime files.</summary>
public sealed class UpdaterWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IApplicationRegistry>();
            services.RemoveAll<IReleaseChannelProvider>();
            services.AddSingleton<IReleaseChannelProvider>(new StaticReleaseChannelProvider());
            services.AddSingleton<IApplicationRegistry>(_ => new InMemoryApplicationRegistry(
                new ApplicationRegistration(
                    "RustServer.Updater",
                    "sysdba925/RustServer.Updater",
                    "/opt/rustserver/updater",
                    "rustserver-updater.service",
                    "RustServer.Updater.Api.dll")));
        });
    }

    private sealed class StaticReleaseChannelProvider : IReleaseChannelProvider
    {
        public ReleaseChannel GetChannel() => ReleaseChannel.Development;
    }
}
