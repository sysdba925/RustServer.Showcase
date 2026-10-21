using RustServer.Control.Domain.StartupTracking;

namespace RustServer.Control.Tests;

public sealed class StartupMilestoneAnalyzerTests
{
    [Fact]
    public void SchemaVersion1_HasExactOrderedMilestones()
    {
        var schema = StartupTrackingSchema.Version1;

        Assert.Equal(1, schema.Version);
        Assert.Equal(
            [
                "engine",
                "mono",
                "bootstrap",
                "scene",
                "oxide",
                "steamInitialized",
                "serverStartup",
                "bootstrapCompleted",
                "steamConnected"
            ],
            schema.Milestones.Select(milestone => milestone.Id));
        Assert.Equal(
            [
                "Unity Engine",
                "Mono Runtime",
                "Bootstrap",
                "Scene Loading",
                "Oxide Core",
                "Steam Server Initialized",
                "Server Startup Complete",
                "Bootstrap Completed",
                "Steam Server Connected"
            ],
            schema.Milestones.Select(milestone => milestone.Label));
    }

    [Theory]
    [InlineData("Initialize engine version: 6000.3.15x1-11 (d4a0e1288074)", 0)]
    [InlineData("Begin MonoManager ReloadAssembly", 1)]
    [InlineData("Bootstrap Startup", 2)]
    [InlineData("Loading Scene", 3)]
    [InlineData("Loading Oxide Core v2.0.7143...", 4)]
    [InlineData("SteamServer Initialized", 5)]
    [InlineData("Server startup complete", 6)]
    [InlineData("[Bootstrap] completed in 17.40s", 7)]
    [InlineData("SteamServer Connected", 8)]
    public void ProcessLine_RecognizesEachBaselineMilestone(string line, int expectedIndex)
    {
        var analyzer = new StartupMilestoneAnalyzer();

        analyzer.ProcessLine(line);

        Assert.Equal(StartupMilestoneState.Reached, analyzer.States[expectedIndex]);
    }

    [Fact]
    public void InitialState_IsPendingForEveryMilestone()
    {
        var analyzer = new StartupMilestoneAnalyzer();

        Assert.Equal(
            Enumerable.Repeat(StartupMilestoneState.Pending, 9),
            analyzer.States);
        Assert.False(analyzer.Started);
    }

    [Fact]
    public void NormalOrderedStartup_ReachesAllMilestonesAndStarts()
    {
        var analyzer = new StartupMilestoneAnalyzer();

        foreach (var line in StartupLines)
        {
            analyzer.ProcessLine(line);
        }

        Assert.Equal(
            Enumerable.Repeat(StartupMilestoneState.Reached, 9),
            analyzer.States);
        Assert.True(analyzer.Started);
    }

    [Fact]
    public void LaterMilestone_SkipsUnobservedOxideWithoutUsingTime()
    {
        var analyzer = new StartupMilestoneAnalyzer();

        foreach (var line in StartupLines.Where((_, index) => index is not 4 and not 6 and not 7 and not 8))
        {
            analyzer.ProcessLine(line);
        }

        Assert.Equal(
            [
                StartupMilestoneState.Reached,
                StartupMilestoneState.Reached,
                StartupMilestoneState.Reached,
                StartupMilestoneState.Reached,
                StartupMilestoneState.Skipped,
                StartupMilestoneState.Reached,
                StartupMilestoneState.Pending,
                StartupMilestoneState.Pending,
                StartupMilestoneState.Pending
            ],
            analyzer.States);
        Assert.False(analyzer.Started);
    }

    [Fact]
    public void LaterMilestone_SkipsEveryEarlierUnresolvedMilestone()
    {
        var analyzer = new StartupMilestoneAnalyzer();

        analyzer.ProcessLine("Bootstrap Startup");

        Assert.Equal(
            [
                StartupMilestoneState.Skipped,
                StartupMilestoneState.Skipped,
                StartupMilestoneState.Reached,
                StartupMilestoneState.Pending,
                StartupMilestoneState.Pending,
                StartupMilestoneState.Pending,
                StartupMilestoneState.Pending,
                StartupMilestoneState.Pending,
                StartupMilestoneState.Pending
            ],
            analyzer.States);
    }

    [Fact]
    public void RepeatedMilestones_AreIdempotent()
    {
        var analyzer = new StartupMilestoneAnalyzer();

        analyzer.ProcessLine("Loading Scene");
        var expected = analyzer.States;
        analyzer.ProcessLine("Loading Scene");

        Assert.Equal(expected, analyzer.States);
    }

    [Fact]
    public void LateEarlierMilestone_DoesNotReviseSkippedState()
    {
        var analyzer = new StartupMilestoneAnalyzer();

        analyzer.ProcessLine("SteamServer Initialized");
        analyzer.ProcessLine("Loading Oxide Core v2.0.7143...");

        Assert.Equal(StartupMilestoneState.Skipped, analyzer.States[4]);
        Assert.Equal(StartupMilestoneState.Reached, analyzer.States[5]);
    }

    [Fact]
    public void FinalMilestone_IsNotInferredAndStartsOnlyWhenObserved()
    {
        var analyzer = new StartupMilestoneAnalyzer();

        analyzer.ProcessLine("[Bootstrap] completed in 17.40s");

        Assert.Equal(StartupMilestoneState.Pending, analyzer.States[8]);
        Assert.False(analyzer.Started);

        analyzer.ProcessLine("SteamServer Connected");

        Assert.Equal(StartupMilestoneState.Reached, analyzer.States[8]);
        Assert.True(analyzer.Started);
    }

    [Theory]
    [InlineData("initialize engine version: 6000.3.15x1-11")]
    [InlineData("[bootstrap] completed in 17.40s")]
    [InlineData("steamserver connected")]
    [InlineData("irrelevant log line")]
    public void Matching_IsCaseSensitiveAndIgnoresIrrelevantLines(string line)
    {
        var analyzer = new StartupMilestoneAnalyzer();

        analyzer.ProcessLine(line);

        Assert.Equal(Enumerable.Repeat(StartupMilestoneState.Pending, 9), analyzer.States);
    }

    private static readonly string[] StartupLines =
    [
        "Initialize engine version: 6000.3.15x1-11 (d4a0e1288074)",
        "Begin MonoManager ReloadAssembly",
        "Bootstrap Startup",
        "Loading Scene",
        "Loading Oxide Core v2.0.7143...",
        "SteamServer Initialized",
        "Server startup complete",
        "[Bootstrap] completed in 17.40s",
        "SteamServer Connected"
    ];
}
