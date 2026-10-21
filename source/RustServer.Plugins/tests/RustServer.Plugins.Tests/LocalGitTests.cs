using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class LocalGitTests
{
    [Fact]
    public async Task GetIgnoredPaths_RespectsGitNegationsTrackedPathsAndBatching()
    {
        using var repository = await GitTestRepository.CreateAsync();
        await repository.AddWorkspaceCommitAsync("data/Tracked.json", "{}"u8.ToArray());
        await repository.AddWorkspaceCommitAsync(
            ".gitignore",
            "data/*\n!data/Kits/\ndata/Kits/*\n!data/Kits/kits_data.json\n"u8.ToArray());

        var localGit = CreateLocalGit(repository.WorkspacePath);
        var candidates = new List<string>
        {
            "data/Tracked.json",
            "data/ActivityRewards.json",
            "data/Kits/player_data.json",
            "data/Kits/kits_data.json",
            "config/ActivityRewards.json"
        };
        // Span multiple git check-ignore batches without modifying Git state.
        for (var i = 0; i < 110; i++)
        {
            candidates.Add($"data/generated_{i:D3}.json");
        }

        var ignored = await localGit.GetIgnoredPathsAsync(
            RepositoryId.Configs, candidates, CancellationToken.None);

        Assert.DoesNotContain("data/Tracked.json", ignored);
        Assert.DoesNotContain("data/Kits/kits_data.json", ignored);
        Assert.DoesNotContain("config/ActivityRewards.json", ignored);
        Assert.Contains("data/ActivityRewards.json", ignored);
        Assert.Contains("data/Kits/player_data.json", ignored);
        Assert.Contains("data/generated_109.json", ignored);
        Assert.Equal(112, ignored.Count);
    }

    [Fact]
    public async Task ReadsCleanDevelopRepositoryWithTrackingBranch()
    {
        using var repository = await GitTestRepository.CreateAsync();
        var localGit = CreateLocalGit(repository.WorkspacePath);

        var state = await localGit.GetStateAsync(RepositoryId.Plugins, CancellationToken.None);

        Assert.True(state.IsRepository);
        Assert.Equal("develop", state.Branch);
        Assert.NotNull(state.Head);
        Assert.True(state.IsClean);
        Assert.Equal("origin/develop", state.TrackingBranch);
        Assert.Equal(0, state.Ahead);
        Assert.Equal(0, state.Behind);
        Assert.Equal(LocalGitRelation.Equal, state.Relation);
        Assert.Equal(LocalGitDiagnostic.None, state.Diagnostic);
    }

    [Fact]
    public async Task ReportsDirtyWorkingTree()
    {
        using var repository = await GitTestRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.WorkspacePath, "pending.txt"), "pending");
        var localGit = CreateLocalGit(repository.WorkspacePath);

        var state = await localGit.GetStateAsync(RepositoryId.Configs, CancellationToken.None);

        Assert.True(state.IsRepository);
        Assert.False(state.IsClean);
        Assert.Equal(LocalGitRelation.Equal, state.Relation);
        Assert.Equal(LocalGitDiagnostic.None, state.Diagnostic);
    }

    private static LocalGit CreateLocalGit(string workspacePath) =>
        new(
            Options.Create(new LocalGitOptions
            {
                Plugins = new WorkspaceOptions { WorkspacePath = workspacePath },
                Configs = new WorkspaceOptions { WorkspacePath = workspacePath }
            }),
            new GitProcess());

}
