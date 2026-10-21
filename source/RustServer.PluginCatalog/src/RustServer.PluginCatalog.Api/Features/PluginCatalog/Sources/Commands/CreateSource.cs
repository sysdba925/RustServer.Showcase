using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Commands;

public sealed record CreateSourceCommand(
    Guid PluginId,
    Guid? SiteId,
    string? Url,
    string? Label);

public sealed class CreateSourceHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<PluginSource>> HandleAsync(
        CreateSourceCommand command,
        CancellationToken cancellationToken)
    {
        if (!await store.PluginExistsAsync(command.PluginId, cancellationToken))
        {
            return CatalogResult<PluginSource>.Fail(CatalogFailures.PluginNotFound());
        }

        if (command.SiteId is { } siteId && !await store.SiteExistsAsync(siteId, cancellationToken))
        {
            return CatalogResult<PluginSource>.Fail(new CatalogFailure(CatalogFailureKind.NotFound, "site_not_found", "Site was not found.", "No site has the requested ID."));
        }

        PluginSource source;
        try
        {
            source = new PluginSource(Guid.NewGuid(), command.PluginId, command.SiteId, command.Url!, command.Label);
        }
        catch (ArgumentException exception)
        {
            return CatalogResult<PluginSource>.Fail(CatalogFailures.Validation(exception.Message));
        }

        store.AddSource(source);
        await store.SaveChangesAsync(cancellationToken);
        return CatalogResult<PluginSource>.Success(source);
    }
}
