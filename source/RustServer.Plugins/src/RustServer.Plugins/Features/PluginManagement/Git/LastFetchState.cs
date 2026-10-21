namespace RustServer.Plugins.Features.PluginManagement.Git;

public sealed class LastFetchState
{
    private readonly object syncRoot = new();
    private DateTimeOffset? lastFetchAt;

    public DateTimeOffset? Get()
    {
        lock (syncRoot)
        {
            return lastFetchAt;
        }
    }

    public void Set(DateTimeOffset value)
    {
        lock (syncRoot)
        {
            lastFetchAt = value;
        }
    }
}
