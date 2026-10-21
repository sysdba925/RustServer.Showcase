namespace RustServer.Plugins.Features.PluginManagement.Guards;

public sealed record ServerStatusSnapshot(bool IsRunning, bool IsReady);
