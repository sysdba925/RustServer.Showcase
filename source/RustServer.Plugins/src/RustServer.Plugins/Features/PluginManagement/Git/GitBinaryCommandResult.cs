namespace RustServer.Plugins.Features.PluginManagement.Git;

public sealed record GitBinaryCommandResult(int ExitCode, byte[] StandardOutput);
