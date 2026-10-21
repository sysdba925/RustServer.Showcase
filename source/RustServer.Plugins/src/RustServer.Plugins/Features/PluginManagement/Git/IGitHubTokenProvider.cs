namespace RustServer.Plugins.Features.PluginManagement.Git;

/// <summary>Provides the Plugins-only GitHub credential when remote operations need it.</summary>
public interface IGitHubTokenProvider
{
    string GetToken();
}
