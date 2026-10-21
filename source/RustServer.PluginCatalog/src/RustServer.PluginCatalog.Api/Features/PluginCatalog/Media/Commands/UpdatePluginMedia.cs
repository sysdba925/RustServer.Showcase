using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Commands;

public sealed record UpdatePluginMediaCommand(
    Guid PluginId,
    Guid MediaId,
    string? Caption,
    int SortOrder);

public sealed class UpdatePluginMediaHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<PluginMedia>> HandleAsync(
        UpdatePluginMediaCommand command,
        CancellationToken cancellationToken)
    {
        var media = await store.GetMediaForUpdateAsync(command.PluginId, command.MediaId, cancellationToken);
        if (media is null)
        {
            return CatalogResult<PluginMedia>.Fail(CatalogFailures.MediaNotFound());
        }

        try
        {
            media.UpdateMetadata(command.Caption, command.SortOrder);
        }
        catch (ArgumentException exception)
        {
            return CatalogResult<PluginMedia>.Fail(CatalogFailures.Validation(exception.Message));
        }

        await store.SaveChangesAsync(cancellationToken);
        return CatalogResult<PluginMedia>.Success(media);
    }
}
