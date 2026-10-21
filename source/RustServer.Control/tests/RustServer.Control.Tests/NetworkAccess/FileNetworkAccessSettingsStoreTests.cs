using RustServer.Control.Domain.NetworkAccess;
using RustServer.Control.Infrastructure.NetworkAccess;

namespace RustServer.Control.Tests.NetworkAccess;

public sealed class FileNetworkAccessSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"RustServer.Control.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void SavedSettings_SurviveStoreRecreation()
    {
        var options = new NetworkAccessOptions { StoragePath = Path.Combine(_directory, "network-access.json") };
        var settings = new NetworkAccessSettings(false, 50_016);

        var firstStore = new FileNetworkAccessSettingsStore(options);
        Assert.Equal(NetworkAccessSettings.Default, firstStore.Get());
        firstStore.Save(settings);

        var recreatedStore = new FileNetworkAccessSettingsStore(options);
        Assert.Equal(settings, recreatedStore.Get());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
