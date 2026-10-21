using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Runtime;

namespace RustServer.Plugins.Features.PluginManagement.RuntimeChanges;

/// <summary>
/// Watches the Runtime root and reports only managed-path invalidations. It never
/// reads a diff, so filesystem event loss cannot affect Runtime Diff correctness.
/// </summary>
public sealed class RuntimeChangeWatcher(
    IOptions<RuntimeOptions> options,
    ManagedFileMapping mapping,
    RuntimeChangeNotifier notifier,
    ILogger<RuntimeChangeWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan WatcherRefreshInterval = TimeSpan.FromSeconds(5);
    private readonly object watcherSync = new();
    private readonly string serverRootPath = options.Value.ServerRootPath;
    private FileSystemWatcher? watcher;
    private bool watcherFaulted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            EnsureWatcher();

            try
            {
                await Task.Delay(WatcherRefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (watcherSync)
        {
            watcher?.Dispose();
            watcher = null;
        }

        return base.StopAsync(cancellationToken);
    }

    private void EnsureWatcher()
    {
        lock (watcherSync)
        {
            if (watcher is not null && !watcherFaulted)
            {
                return;
            }

            watcher?.Dispose();
            watcher = null;
            watcherFaulted = false;

            if (!TryGetSafeServerRootPath(out var serverRootPath))
            {
                return;
            }

            try
            {
                watcher = new FileSystemWatcher(serverRootPath)
                {
                    IncludeSubdirectories = true,
                    Filter = "*",
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size |
                                   NotifyFilters.CreationTime
                };
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogWarning(exception, "Runtime change watcher could not observe the Runtime root.");
                watcher?.Dispose();
                watcher = null;
            }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs eventArgs)
    {
        ObservePath(eventArgs.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        ObservePath(eventArgs.OldFullPath);
        ObservePath(eventArgs.FullPath);
    }

    internal void ObservePath(string fullPath)
    {
        if (IsManagedOrManagedDirectory(fullPath))
        {
            notifier.NotifyRuntimeFilesMayHaveChanged();
        }
    }

    private void OnError(object sender, ErrorEventArgs eventArgs)
    {
        ObserveWatcherError(eventArgs.GetException());
    }

    internal void ObserveWatcherError(Exception exception)
    {
        // An overflow means individual changes may have been lost. Notify clients to
        // refresh the authoritative diff, then recreate the watcher on the next tick.
        logger.LogWarning(exception, "Runtime change watcher reported an error.");
        lock (watcherSync)
        {
            watcherFaulted = true;
        }

        notifier.NotifyRuntimeFilesMayHaveChanged();
    }

    private bool IsManagedOrManagedDirectory(string fullPath)
    {
        if (!TryGetSafeServerRootPath(out var serverRootPath))
        {
            return false;
        }

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(serverRootPath, Path.GetFullPath(fullPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }

        if (Path.IsPathRooted(relativePath) ||
            relativePath is "." or ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        var logicalPath = relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        if (mapping.FromRuntimePath(logicalPath) is not null)
        {
            return true;
        }

        return logicalPath is "oxide/plugins" or "oxide/config" or "oxide/data" or "oxide/lang" or "oxide/discord" ||
               logicalPath.StartsWith("oxide/config/", StringComparison.Ordinal) ||
               logicalPath.StartsWith("oxide/data/", StringComparison.Ordinal) ||
               logicalPath.StartsWith("oxide/lang/", StringComparison.Ordinal) ||
               logicalPath.StartsWith("oxide/discord/", StringComparison.Ordinal);
    }

    private bool TryGetSafeServerRootPath(out string fullServerRootPath)
    {
        fullServerRootPath = string.Empty;
        if (string.IsNullOrWhiteSpace(serverRootPath))
        {
            return false;
        }

        try
        {
            fullServerRootPath = Path.GetFullPath(serverRootPath);
            return Directory.Exists(fullServerRootPath) &&
                   !File.GetAttributes(fullServerRootPath).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
