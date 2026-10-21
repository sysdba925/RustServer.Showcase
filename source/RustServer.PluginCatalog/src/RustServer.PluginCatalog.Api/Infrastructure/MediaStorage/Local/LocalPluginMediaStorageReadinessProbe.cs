using Microsoft.Extensions.Options;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Infrastructure.MediaStorage.Local;

public sealed class LocalPluginMediaStorageReadinessProbe(IOptions<MediaStorageOptions> options)
    : IPluginMediaStorageReadinessProbe
{
    public async Task VerifyReadyAsync(CancellationToken cancellationToken)
    {
        var rootPath = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("Local media storage root path is not configured.");
        }

        var fullRootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRootPath)
            || File.GetAttributes(fullRootPath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("The configured media root is unavailable.");
        }

        var probePath = Path.Combine(fullRootPath, $".readiness-{Guid.NewGuid():N}.tmp");
        await using var probe = new FileStream(
            probePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await probe.FlushAsync(cancellationToken);
    }
}
