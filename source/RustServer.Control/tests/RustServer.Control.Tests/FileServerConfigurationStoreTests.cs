using RustServer.Control.Domain.Servers;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Tests;

public sealed class FileServerConfigurationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"RustServer.Control.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void DesiredAndEffectiveConfiguration_SurviveStoreRecreation()
    {
        var storagePath = Path.Combine(_directory, "configuration.json");
        var initial = new ServerConfiguration("Initial", string.Empty, 10, 30, 600, 1, 3500);
        var desired = new ServerConfiguration("Desired", "Description", 20, 10, 900, 2, 4200);
        var effective = new ServerConfiguration("Effective", "Description", 20, 10, 900, 1, 4200);
        var options = new ServerConfigurationOptions
        {
            StoragePath = storagePath,
            InitialDesired = new InitialServerConfigurationOptions
            {
                Hostname = initial.Hostname,
                Description = initial.Description,
                MaxPlayers = initial.MaxPlayers,
                TickRate = initial.TickRate,
                SaveInterval = initial.SaveInterval,
                Seed = initial.Seed,
                WorldSize = initial.WorldSize
            }
        };

        var firstStore = new FileServerConfigurationStore(options);
        Assert.Equal(new ServerConfigurationState(initial, null), firstStore.Get());

        firstStore.SetDesired(desired);
        firstStore.RecordEffective(effective);

        var recreatedStore = new FileServerConfigurationStore(options);
        Assert.Equal(new ServerConfigurationState(desired, effective), recreatedStore.Get());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
