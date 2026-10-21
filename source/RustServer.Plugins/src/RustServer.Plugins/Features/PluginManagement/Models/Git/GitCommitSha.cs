namespace RustServer.Plugins.Features.PluginManagement.Models.Git;

public sealed record GitCommitSha
{
    public GitCommitSha(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Git commit SHA must contain text.", nameof(value));
        }

        if (value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Git commit SHA must be hexadecimal.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}
