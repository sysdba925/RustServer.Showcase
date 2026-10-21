using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Commands;

public sealed record CreateExternalPluginMediaCommand(
    Guid PluginId,
    PluginMediaKind Kind,
    string? ExternalUrl,
    string? Caption,
    int SortOrder);

public sealed class CreateExternalPluginMediaHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<PluginMedia>> HandleAsync(
        CreateExternalPluginMediaCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Kind is not (PluginMediaKind.Video or PluginMediaKind.Preview or PluginMediaKind.Technical))
        {
            return CatalogResult<PluginMedia>.Fail(
                CatalogFailures.Validation("kind must be Video, Preview or Technical for external media."));
        }

        if (!await store.PluginExistsAsync(command.PluginId, cancellationToken))
        {
            return CatalogResult<PluginMedia>.Fail(CatalogFailures.PluginNotFound());
        }

        PluginMedia media;
        try
        {
            media = new PluginMedia(
                Guid.NewGuid(),
                command.PluginId,
                command.Kind,
                command.Caption,
                command.SortOrder,
                null,
                command.ExternalUrl,
                null);
        }
        catch (ArgumentException exception)
        {
            return CatalogResult<PluginMedia>.Fail(CatalogFailures.Validation(exception.Message));
        }

        store.AddMedia(media);
        await store.SaveChangesAsync(cancellationToken);

        return CatalogResult<PluginMedia>.Success(media);
    }
}
