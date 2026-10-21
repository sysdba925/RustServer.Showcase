using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Commands;

public sealed record UpdateSourceCommand(
    Guid PluginId,
    Guid SourceId,
    Guid? SiteId,
    string? Url,
    string? Label);

public sealed class UpdateSourceHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<PluginSource>> HandleAsync(
        UpdateSourceCommand command,
        CancellationToken cancellationToken)
    {
        var source = await store.GetSourceForUpdateAsync(command.PluginId, command.SourceId, cancellationToken);
        if (source is null)
        {
            return CatalogResult<PluginSource>.Fail(CatalogFailures.SourceNotFound());
        }

        if (command.SiteId is { } siteId && !await store.SiteExistsAsync(siteId, cancellationToken))
        {
            return CatalogResult<PluginSource>.Fail(new CatalogFailure(CatalogFailureKind.NotFound, "site_not_found", "Site was not found.", "No site has the requested ID."));
        }

        try
        {
            source.Update(command.SiteId, command.Url!, command.Label);
        }
        catch (ArgumentException exception)
        {
            return CatalogResult<PluginSource>.Fail(CatalogFailures.Validation(exception.Message));
        }

        await store.SaveChangesAsync(cancellationToken);
        return CatalogResult<PluginSource>.Success(source);
    }
}
