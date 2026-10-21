using RustServer.Control.Application.Wipe;
using RustServer.Control.Domain.Wipe;
using RustServer.Control.Infrastructure.Servers;
using RustServer.Control.Infrastructure.Wipe;

namespace RustServer.Control.Tests;

public sealed class WipeEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"RustServer.Control.WipeTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(WipeAction.Delete, WipeAction.Delete, false, false)]
    [InlineData(WipeAction.Delete, WipeAction.Keep, false, true)]
    [InlineData(WipeAction.Keep, WipeAction.Delete, true, false)]
    [InlineData(WipeAction.Keep, WipeAction.Keep, true, true)]
    public async Task ExecuteAsync_AppliesWorldAndBlueprintPoliciesIndependently(
        WipeAction world,
        WipeAction blueprints,
        bool expectWorldFiles,
        bool expectBlueprintFiles)
    {
        var layout = CreateLayout();
        var engine = CreateEngine();

        var report = await engine.ExecuteAsync(new WipePlan(world, blueprints, []));

        Assert.True(report.Succeeded);
        Assert.Equal(expectWorldFiles, File.Exists(layout.Map));
        Assert.Equal(expectWorldFiles, File.Exists(layout.Save));
        Assert.Equal(expectWorldFiles, File.Exists(layout.SaveBackup));
        Assert.Equal(expectBlueprintFiles, File.Exists(layout.Blueprints));
        Assert.Equal(expectBlueprintFiles, File.Exists(layout.BlueprintsWal));
        Assert.True(File.Exists(layout.UnknownRustFile));
        Assert.True(File.Exists(layout.PluginBinary));
        Assert.True(File.Exists(layout.PluginConfiguration));
        Assert.True(File.Exists(layout.UnknownPluginData));
        Assert.Equal(world == WipeAction.Delete ? WipeTargetOutcome.Deleted : WipeTargetOutcome.Kept,
            report.Targets.Single(target => target.Target == "world").Outcome);
        Assert.Equal(blueprints == WipeAction.Delete ? WipeTargetOutcome.Deleted : WipeTargetOutcome.Kept,
            report.Targets.Single(target => target.Target == "blueprints").Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesOnlyExplicitlyRegisteredPluginDataTarget()
    {
        var layout = CreateLayout();
        var engine = CreateEngine(new DeleteEconomicsHandler());
        var economicsPath = Path.Combine(Path.GetDirectoryName(layout.UnknownPluginData)!, "Economics.json");
        File.WriteAllText(economicsPath, "{}");

        var report = await engine.ExecuteAsync(new WipePlan(
            WipeAction.Keep,
            WipeAction.Keep,
            [new PluginDataWipeAction("economics", WipeAction.Delete)]));

        Assert.True(report.Succeeded);
        Assert.False(File.Exists(economicsPath));
        Assert.True(File.Exists(layout.UnknownPluginData));
        var target = Assert.Single(report.Targets, target => target.Target == "economics");
        Assert.Equal(WipeTargetOutcome.Deleted, target.Outcome);
        Assert.Equal(1, target.AffectedItems);
    }

    [Fact]
    public async Task ExecuteAsync_DispatchesTransformToRegisteredPluginHandler()
    {
        var handler = new TransformHandler();
        var engine = CreateEngine(handler);

        var report = await engine.ExecuteAsync(new WipePlan(
            WipeAction.Keep,
            WipeAction.Keep,
            [new PluginDataWipeAction("skillTree", WipeAction.Transform)]));

        Assert.Equal([WipeAction.Transform], handler.Actions);
        var target = Assert.Single(report.Targets, target => target.Target == "skillTree");
        Assert.Equal(WipeTargetOutcome.Transformed, target.Outcome);
        Assert.Equal(2, target.AffectedItems);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsTraversalInInfrastructureIdentityBeforeDeletion()
    {
        var layout = CreateLayout();
        var options = new RustServerControlOptions
        {
            RustServerDataPath = _root,
            RustServerIdentity = ".."
        };
        var engine = new WipeEngine(options, []);

        await Assert.ThrowsAsync<WipeConfigurationException>(() => engine.ExecuteAsync(new WipePlan(
            WipeAction.Delete,
            WipeAction.Delete,
            [])));

        Assert.True(File.Exists(layout.Map));
        Assert.True(File.Exists(layout.Blueprints));
    }

    [Fact]
    public void ValidatePlan_RejectsUnknownPluginTargetWithoutAcceptingAPath()
    {
        var engine = CreateEngine();

        var error = engine.ValidatePlan(new WipePlan(
            WipeAction.Keep,
            WipeAction.Keep,
            [new PluginDataWipeAction("../../outside", WipeAction.Delete)]));

        Assert.Equal("Plugin-data target '../../outside' is not registered.", error);
    }

    private WipeEngine CreateEngine(params IPluginDataWipeHandler[] handlers) =>
        new(
            new RustServerControlOptions
            {
                RustServerDataPath = _root,
                RustServerIdentity = "unit-server"
            },
            handlers);

    private TestLayout CreateLayout()
    {
        var identityDirectory = Path.Combine(_root, "server", "unit-server");
        var pluginDirectory = Path.Combine(_root, "oxide", "plugins");
        var configDirectory = Path.Combine(_root, "oxide", "config");
        var pluginDataDirectory = Path.Combine(_root, "oxide", "data");
        Directory.CreateDirectory(identityDirectory);
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(pluginDataDirectory);

        var map = Path.Combine(identityDirectory, "proceduralmap.3500.12345.map");
        var save = Path.Combine(identityDirectory, "proceduralmap.3500.12345.sav");
        var saveBackup = Path.Combine(identityDirectory, "proceduralmap.3500.12345.sav.1");
        var blueprints = Path.Combine(identityDirectory, "player.blueprints.5.db");
        var blueprintsWal = Path.Combine(identityDirectory, "player.blueprints.5.db-wal");
        var unknownRustFile = Path.Combine(identityDirectory, "player.states.5.db");
        var pluginBinary = Path.Combine(pluginDirectory, "Economics.cs");
        var pluginConfiguration = Path.Combine(configDirectory, "Economics.json");
        var unknownPluginData = Path.Combine(pluginDataDirectory, "UnknownPlugin.json");
        foreach (var path in new[]
        {
            map, save, saveBackup, blueprints, blueprintsWal, unknownRustFile,
            pluginBinary, pluginConfiguration, unknownPluginData
        })
        {
            File.WriteAllText(path, "data");
        }

        return new TestLayout(
            map,
            save,
            saveBackup,
            blueprints,
            blueprintsWal,
            unknownRustFile,
            pluginBinary,
            pluginConfiguration,
            unknownPluginData);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record TestLayout(
        string Map,
        string Save,
        string SaveBackup,
        string Blueprints,
        string BlueprintsWal,
        string UnknownRustFile,
        string PluginBinary,
        string PluginConfiguration,
        string UnknownPluginData);

    private sealed class DeleteEconomicsHandler : IPluginDataWipeHandler
    {
        public string Target => "economics";

        public Task<WipePluginDataHandlerResult> ExecuteAsync(
            WipeAction action,
            WipePluginDataContext context,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(WipeAction.Delete, action);
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(context.PluginDataDirectory, "Economics.json");
            var existed = File.Exists(path);
            if (existed)
            {
                File.Delete(path);
            }

            return Task.FromResult(new WipePluginDataHandlerResult(existed ? 1 : 0));
        }
    }

    private sealed class TransformHandler : IPluginDataWipeHandler
    {
        public string Target => "skillTree";

        public List<WipeAction> Actions { get; } = [];

        public Task<WipePluginDataHandlerResult> ExecuteAsync(
            WipeAction action,
            WipePluginDataContext context,
            CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.FromResult(new WipePluginDataHandlerResult(2));
        }
    }
}
