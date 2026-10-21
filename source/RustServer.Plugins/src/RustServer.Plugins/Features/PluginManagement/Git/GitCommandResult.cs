namespace RustServer.Plugins.Features.PluginManagement.Git;

// Preserve the existing two-value positional contract and deconstruction shape.
public sealed record GitCommandResult(int ExitCode, string StandardOutput)
{
    // Captured in memory for safe categorization; never log this raw untrusted output.
    public string StandardError { get; init; } = string.Empty;
}
