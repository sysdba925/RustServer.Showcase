using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Plugins.Features.PluginManagement;
using RustServer.Plugins.Features.PluginManagement.Git;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class GitHubCredentialTests
{
    [Fact]
    public void ValidCredential_IsReadFromConfiguredFile()
    {
        WithTemporaryCredential("  github-test-value  \n", path =>
        {
            var provider = new SystemdGitHubTokenProvider(() => path);

            Assert.Equal("github-test-value", provider.GetToken());
        });
    }

    [Fact]
    public void MissingCredentialPath_ReturnsNonSecretDiagnostic()
    {
        var provider = new SystemdGitHubTokenProvider(() => null);

        var exception = Assert.Throws<GitHubCredentialUnavailableException>(provider.GetToken);

        Assert.Contains("not configured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableCredential_ReturnsNonSecretDiagnostic()
    {
        var missingFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "credential");
        var provider = new SystemdGitHubTokenProvider(() => missingFile);

        var exception = Assert.Throws<GitHubCredentialUnavailableException>(provider.GetToken);

        Assert.DoesNotContain(missingFile, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("unavailable", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n  ")]
    [InlineData("secret-part-1\nsecret-part-2")]
    public void InvalidCredential_ReturnsNonSecretDiagnostic(string contents)
    {
        WithTemporaryCredential(contents, path =>
        {
            var provider = new SystemdGitHubTokenProvider(() => path);

            var exception = Assert.Throws<GitHubCredentialUnavailableException>(provider.GetToken);

            Assert.DoesNotContain(path, exception.ToString(), StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(contents))
            {
                Assert.DoesNotContain(contents.Trim(), exception.ToString(), StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public void GitHubProvider_IsRegisteredInPluginManagementDI()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddPluginManagement(config);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SystemdGitHubTokenProvider>(provider.GetRequiredService<IGitHubTokenProvider>());
    }

    private static void WithTemporaryCredential(string contents, Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RustServer.Plugins.CredentialTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "github-token");

        try
        {
            File.WriteAllText(path, contents);
            test(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
