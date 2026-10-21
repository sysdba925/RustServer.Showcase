using System.Diagnostics;

namespace RustServer.Plugins.Features.PluginManagement.Git;

/// <summary>A token-free temporary Git askpass helper for the Linux service.</summary>
internal sealed class GitHubAskPassSession : IDisposable
{
    private const string Script = """
#!/bin/sh
set -eu
case "${1:-}" in
    Username*) printf '%s\n' 'x-access-token' ;;
    Password*)
        [ -n "${RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE:-}" ] || exit 1
        [ -r "$RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE" ] || exit 1
        IFS= read -r token < "$RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE" || [ -n "$token" ]
        printf '%s\n' "$token"
        ;;
    *) exit 1 ;;
esac
""";

    private readonly string directory;
    private readonly string scriptPath;

    internal static string NormalizeScriptLineEndings(string script) => script.ReplaceLineEndings("\n");

    public GitHubAskPassSession()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new GitProcessException("Plugins GitHub credential transport requires Linux.");
        }

        directory = Path.Combine(Path.GetTempPath(), "rustserver-plugins-askpass-" + Guid.NewGuid().ToString("N"));
        scriptPath = Path.Combine(directory, "askpass.sh");

        try
        {
            Directory.CreateDirectory(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // C# raw string literals can preserve CRLF from Windows source checkouts.
            // The Linux shebang must use LF or execve looks for /bin/sh\r (ENOENT).
            File.WriteAllText(scriptPath, NormalizeScriptLineEndings(Script));
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Dispose();
            throw new GitProcessException("Could not prepare secure GitHub authentication.");
        }
    }

    public void Configure(ProcessStartInfo startInfo)
    {
        startInfo.Environment["GIT_ASKPASS"] = scriptPath;
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        // Ignore system/global URL rewrites and credential helpers for token-bearing requests.
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException) { /* The helper never contains the credential. */ }
        catch (UnauthorizedAccessException) { /* Cleanup is best effort. */ }
    }
}
