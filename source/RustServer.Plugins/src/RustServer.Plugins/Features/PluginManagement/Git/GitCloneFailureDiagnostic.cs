namespace RustServer.Plugins.Features.PluginManagement.Git;

/// <summary>
/// Maps untrusted Git stderr to fixed, secret-free diagnostic codes.
/// Never put raw stderr into service logs: remote output can include credentials or URLs.
/// </summary>
internal static class GitCloneFailureDiagnostic
{
    public static string Classify(string? standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return "stderrEmpty";
        }

        static bool Has(string text, string value) =>
            text.Contains(value, StringComparison.OrdinalIgnoreCase);

        if ((Has(standardError, "cannot run") || Has(standardError, "cannot exec")) &&
            Has(standardError, "askpass"))
        {
            return "askpassExecutionFailed";
        }

        if (Has(standardError, "could not read Username") ||
            Has(standardError, "could not read Password") ||
            Has(standardError, "terminal prompts disabled"))
        {
            return "credentialPromptUnavailable";
        }

        if (Has(standardError, "Authentication failed") ||
            Has(standardError, "HTTP Basic: Access denied") ||
            Has(standardError, "The requested URL returned error: 401") ||
            Has(standardError, "The requested URL returned error: 403"))
        {
            return "authenticationRejected";
        }

        if (Has(standardError, "repository not found") ||
            Has(standardError, "does not appear to be a git repository"))
        {
            return "repositoryUnavailable";
        }

        if (Has(standardError, "Remote branch develop not found") ||
            Has(standardError, "Remote branch develop not found in upstream origin"))
        {
            return "developBranchUnavailable";
        }

        if (Has(standardError, "Could not resolve host") ||
            Has(standardError, "Name or service not known"))
        {
            return "dnsResolutionFailed";
        }

        if (Has(standardError, "SSL certificate problem") ||
            Has(standardError, "certificate verify failed"))
        {
            return "tlsValidationFailed";
        }

        if (Has(standardError, "Failed to connect") ||
            Has(standardError, "Connection timed out") ||
            Has(standardError, "Connection refused"))
        {
            return "networkConnectionFailed";
        }

        if (Has(standardError, "could not create work tree dir") ||
            Has(standardError, "could not create leading directories") ||
            Has(standardError, "destination path"))
        {
            return "workspaceCreationFailed";
        }

        if (Has(standardError, "Permission denied"))
        {
            return "permissionDenied";
        }

        return "unclassifiedGitFailure";
    }
}
