using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Commands;

public sealed record UpdatePluginCommand(
    Guid PluginId,
    string? Name,
    string? Description,
    string? Author,
    string? Notes,
    string? SourceDescription,
    decimal? Price,
    string? Currency,
    string? CurrentKnownVersion,
    DateOnly? LastPublishedDate,
    List<PluginSourceInput>? Sources,
    List<Guid>? TagIds,
    List<Guid>? FolderIds);

public sealed class UpdatePluginHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<Plugin>> HandleAsync(
        UpdatePluginCommand command,
        CancellationToken cancellationToken)
    {
        var plugin = await store.GetPluginForUpdateAsync(command.PluginId, cancellationToken);
        if (plugin is null)
        {
            return CatalogResult<Plugin>.Fail(CatalogFailures.PluginNotFound());
        }

        try
        {
            plugin.Update(
                command.Name!,
                plugin.Slug,
                command.Description,
                command.CurrentKnownVersion,
                command.LastPublishedDate,
                DateTimeOffset.UtcNow);
            plugin.SetLibraryDetails(command.Author, command.Notes, command.SourceDescription, command.Price, command.Currency);
        }
        catch (ArgumentException exception)
        {
            return CatalogResult<Plugin>.Fail(CatalogFailures.Validation(exception.Message));
        }

        var associationFailure = await PluginCardAssociations.ApplyAsync(store, plugin, command.Sources, command.TagIds, command.FolderIds, cancellationToken);
        if (associationFailure is not null)
        {
            return CatalogResult<Plugin>.Fail(associationFailure);
        }

        var saveOutcome = await store.SaveChangesAsync(cancellationToken);
        return saveOutcome == CatalogSaveOutcome.SlugConflict
            ? CatalogResult<Plugin>.Fail(CatalogFailures.SlugConflict())
            : CatalogResult<Plugin>.Success(plugin);
    }
}
