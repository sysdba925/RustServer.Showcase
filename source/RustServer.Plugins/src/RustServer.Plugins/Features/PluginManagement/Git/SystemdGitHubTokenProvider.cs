namespace RustServer.Plugins.Features.PluginManagement.Git;

/// <summary>
/// Reads Plugins' GitHub token from systemd's private credential copy.
/// Never writes the credential into settings, process arguments or diagnostic messages.
/// </summary>
public sealed class SystemdGitHubTokenProvider : IGitHubTokenProvider
{
    public const string CredentialPathEnvironmentVariable = "RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE";

    private readonly Func<string?> credentialPathProvider;

    public SystemdGitHubTokenProvider()
    : this(() => global::System.Environment.GetEnvironmentVariable(
        CredentialPathEnvironmentVariable))
    {
    }

    // Allows deterministic tests without changing process-wide environment variables.
    public SystemdGitHubTokenProvider(Func<string?> credentialPathProvider)
    {
        this.credentialPathProvider = credentialPathProvider ??
            throw new ArgumentNullException(nameof(credentialPathProvider));
    }

    public string GetToken()
    {
        var path = credentialPathProvider();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GitHubCredentialUnavailableException("Plugins GitHub credential is not configured.");
        }

        string token;
        try
        {
            token = File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Do not expose filesystem paths or underlying OS errors.
            throw new GitHubCredentialUnavailableException("Plugins GitHub credential is unavailable.");
        }

        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace) || token.Contains('\0'))
        {
            throw new GitHubCredentialUnavailableException("Plugins GitHub credential is empty or invalid.");
        }

        return token;
    }
}

/// <summary>Failure to read a usable credential, with no sensitive file or token information.</summary>
public sealed class GitHubCredentialUnavailableException(string message) : Exception(message);
