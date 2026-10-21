using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class GitWorkspaceInitializationTests
{
    [Fact]
    public async Task MissingDirectories_AreCreatedAndClonedIncludingNestedPlugins()
    {
        using var source = await GitTestRepository.CreateAsync();
        var root = Path.Combine(source.RootPath, "managed");
        var configs = Path.Combine(root, "oxide");
        var plugins = Path.Combine(configs, "plugins");
        var initializer = CreateInitializer(configs, plugins);

        await initializer.InitializeOneAsync(configs, source.OriginPath, null, CancellationToken.None);
        await initializer.InitializeOneAsync(plugins, source.OriginPath, null, CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(configs, ".git")));
        Assert.True(Directory.Exists(Path.Combine(plugins, ".git")));
        Assert.Equal("initial", await File.ReadAllTextAsync(Path.Combine(configs, "README.md")));
        Assert.Equal("initial", await File.ReadAllTextAsync(Path.Combine(plugins, "README.md")));
    }

    [Fact]
    public async Task EmptyDirectoryProvisionedByBootstrap_IsCloned()
    {
        using var source = await GitTestRepository.CreateAsync();
        var target = Path.Combine(source.RootPath, "empty-workspace");
        Directory.CreateDirectory(target);
        var initializer = CreateInitializer(target, target);

        await initializer.InitializeOneAsync(target, source.OriginPath, null, CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(target, ".git")));
    }

    [Fact]
    public async Task ExistingCheckout_DoesNotOverwriteUncommittedChanges()
    {
        using var source = await GitTestRepository.CreateAsync();
        var target = Path.Combine(source.RootPath, "managed-checkout");
        var initializer = CreateInitializer(target, target);
        await initializer.InitializeOneAsync(target, source.OriginPath, null, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(target, "README.md"), "operator change");

        await initializer.InitializeOneAsync(target, source.OriginPath, null, CancellationToken.None);

        Assert.Equal("operator change", await File.ReadAllTextAsync(Path.Combine(target, "README.md")));
    }

    [Fact]
    public async Task NonEmptyUnrecognizedDirectory_IsPreserved()
    {
        using var source = await GitTestRepository.CreateAsync();
        var target = Path.Combine(source.RootPath, "operator-files");
        Directory.CreateDirectory(target);
        var file = Path.Combine(target, "important.txt");
        await File.WriteAllTextAsync(file, "keep me");
        var initializer = CreateInitializer(target, target);

        await Assert.ThrowsAsync<GitWorkspaceInitializationException>(() =>
            initializer.InitializeOneAsync(target, source.OriginPath, null, CancellationToken.None));

        Assert.Equal("keep me", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task ExistingCheckout_WithDifferentOrigin_IsRejectedWithoutMutation()
    {
        using var source = await GitTestRepository.CreateAsync();
        var target = Path.Combine(source.RootPath, "managed-checkout");
        var initializer = CreateInitializer(target, target);
        await initializer.InitializeOneAsync(target, source.OriginPath, null, CancellationToken.None);

        await Assert.ThrowsAsync<GitWorkspaceInitializationException>(() =>
            initializer.InitializeOneAsync(target, source.OriginPath + "-wrong", null, CancellationToken.None));

        Assert.True(Directory.Exists(Path.Combine(target, ".git")));
    }

    [Theory]
    [InlineData("fatal: cannot run /tmp/git-askpass: Permission denied", "askpassExecutionFailed")]
    [InlineData("fatal: could not read Username for https://github.com", "credentialPromptUnavailable")]
    [InlineData("fatal: Authentication failed for private repository", "authenticationRejected")]
    [InlineData("remote: Repository not found.", "repositoryUnavailable")]
    [InlineData("fatal: Remote branch develop not found in upstream origin", "developBranchUnavailable")]
    [InlineData("fatal: Could not resolve host: github.com", "dnsResolutionFailed")]
    [InlineData("fatal: SSL certificate problem", "tlsValidationFailed")]
    [InlineData("fatal: could not create work tree dir", "workspaceCreationFailed")]
    [InlineData("unexpected github_pat_example_sensitive_value", "unclassifiedGitFailure")]
    public void CloneFailureDiagnostic_UsesOnlyFixedSafeCodes(string stderr, string expected)
    {
        var diagnostic = GitCloneFailureDiagnostic.Classify(stderr);

        Assert.Equal(expected, diagnostic);
        Assert.DoesNotContain("github_pat_", diagnostic, StringComparison.Ordinal);
        Assert.Equal("stderrEmpty", GitCloneFailureDiagnostic.Classify(""));
    }

    [Fact]
    public async Task FailedClone_ReportsExitCodeAndSafeDiagnostic_WithoutRawGitError()
    {
        using var source = await GitTestRepository.CreateAsync();
        var target = Path.Combine(source.RootPath, "missing-source-checkout");
        var initializer = CreateInitializer(target, target);

        var failure = await Assert.ThrowsAsync<GitWorkspaceInitializationException>(() =>
            initializer.InitializeOneAsync(
                target, source.OriginPath + "-does-not-exist", null, CancellationToken.None));

        Assert.Contains("exitCode=", failure.Message, StringComparison.Ordinal);
        Assert.Contains("diagnostic=", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(source.OriginPath, failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    private static GitWorkspaceInitializer CreateInitializer(string configs, string plugins) =>
        new(Options.Create(new LocalGitOptions
        {
            Configs = new WorkspaceOptions { WorkspacePath = configs },
            Plugins = new WorkspaceOptions { WorkspacePath = plugins }
        }), new GitProcess());
}
