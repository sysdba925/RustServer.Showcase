using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RustServer.Snapshots.Tests;

public sealed class SnapshotsApiFactory : WebApplicationFactory<global::Program>
{
    public SnapshotsApiFactory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "RustServer.Snapshots.Tests", Guid.NewGuid().ToString("N"));
        ServerIdentityPath = Path.Combine(RootPath, "server", "test-identity");
        OxideDataPath = Path.Combine(RootPath, "oxide", "data");
        OxideConfigPath = Path.Combine(RootPath, "oxide", "config");
        OxidePluginsPath = Path.Combine(RootPath, "oxide", "plugins");
        OxideLangPath = Path.Combine(RootPath, "oxide", "lang");
        StoragePath = Path.Combine(RootPath, "snapshots");

        foreach (var path in new[]
                 {
                     ServerIdentityPath,
                     OxideDataPath,
                     OxideConfigPath,
                     OxidePluginsPath,
                     OxideLangPath
                 })
        {
            Directory.CreateDirectory(path);
        }

        File.WriteAllText(Path.Combine(ServerIdentityPath, "state.txt"), "original state");
        File.WriteAllText(Path.Combine(OxideDataPath, "data.json"), "original data");
        var pluginCachePath = Path.Combine(OxideDataPath, "MyPlugin", "cache");
        Directory.CreateDirectory(pluginCachePath);
        File.WriteAllText(Path.Combine(pluginCachePath, "state.log"), "persistent plugin state");
    }

    public string RootPath { get; }

    public string ServerIdentityPath { get; }

    public string OxideDataPath { get; }

    public string OxideConfigPath { get; }

    public string OxidePluginsPath { get; }

    public string OxideLangPath { get; }

    public string StoragePath { get; }

    public TestControlServerClient ControlServer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(FindRepositoryRoot(), "src", "RustServer.Snapshots.Api"));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SnapshotOptions>();
            services.AddSingleton(new SnapshotOptions
            {
                ServerIdentityPath = ServerIdentityPath,
                OxideDataPath = OxideDataPath,
                OxideConfigPath = OxideConfigPath,
                OxidePluginsPath = OxidePluginsPath,
                OxideLangPath = OxideLangPath,
                StoragePath = StoragePath,
                RetentionCount = 5,
                MinimumFreeSpaceBytes = 0
            });
            services.RemoveAll<IControlServerClient>();
            services.AddSingleton<IControlServerClient>(ControlServer);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RustServer.Snapshots.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the RustServer.Snapshots repository root.");
    }
}

public sealed class TestControlServerClient : IControlServerClient
{
    public bool IsRunning { get; set; }

    public Task<bool> IsServerRunningAsync(CancellationToken cancellationToken) =>
        Task.FromResult(IsRunning);
}
