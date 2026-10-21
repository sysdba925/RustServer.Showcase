namespace RustServer.Updater.Api.Applications;

/// <summary>Obtains the Updater-only GitHub credential from systemd's private credential directory.</summary>
public interface IGitHubTokenProvider
{
    string GetToken();
}

/// <summary>
/// Reads a token from the file pointed to by the service's credential environment variable.
/// The credential file is never added to configuration, command lines, responses, or logs.
/// </summary>
public sealed class SystemdGitHubTokenProvider : IGitHubTokenProvider
{
    public const string CredentialPathEnvironmentVariable = "RUSTSERVER_UPDATER_GITHUB_TOKEN_FILE";

    /// <inheritdoc />
    public string GetToken()
    {
        var path = Environment.GetEnvironmentVariable(CredentialPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GitHubCredentialUnavailableException("Updater GitHub credential is not configured.");
        }

        try
        {
            var token = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new GitHubCredentialUnavailableException("Updater GitHub credential is empty.");
            }

            return token;
        }
        catch (GitHubCredentialUnavailableException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new GitHubCredentialUnavailableException("Updater GitHub credential is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new GitHubCredentialUnavailableException("Updater GitHub credential is unavailable.");
        }
    }
}

/// <summary>Raised when a GitHub refresh cannot authenticate without revealing credential details.</summary>
public sealed class GitHubCredentialUnavailableException(string message) : Exception(message);

/// <summary>Raised after a refresh preserves snapshots but cannot query GitHub due to a missing credential.</summary>
public sealed class GitHubRefreshUnavailableException : Exception
{
    public GitHubRefreshUnavailableException() : base("GitHub refresh is unavailable because the updater credential is missing or unreadable.")
    {
    }
}
