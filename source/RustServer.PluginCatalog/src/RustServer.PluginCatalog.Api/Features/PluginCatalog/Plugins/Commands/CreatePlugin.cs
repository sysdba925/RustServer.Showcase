using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Commands;

public sealed record CreatePluginCommand(
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

public sealed class CreatePluginHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<Plugin>> HandleAsync(
        CreatePluginCommand command,
        CancellationToken cancellationToken)
    {
        Plugin plugin;
        try
        {
            var now = DateTimeOffset.UtcNow;
            plugin = new Plugin(
                Guid.NewGuid(),
                command.Name!,
                PluginSlug.FromName(command.Name),
                command.Description,
                command.CurrentKnownVersion,
                command.LastPublishedDate,
                now,
                now);
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

        if (await store.SlugExistsAsync(plugin.Id, plugin.Slug, cancellationToken))
        {
            plugin.SetSlug(PluginSlug.WithSuffix(plugin.Slug));
        }

        store.AddPlugin(plugin);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (await store.SaveChangesAsync(cancellationToken) == CatalogSaveOutcome.Saved)
            {
                return CatalogResult<Plugin>.Success(plugin);
            }

            plugin.SetSlug(PluginSlug.WithSuffix(PluginSlug.FromName(command.Name)));
        }

        return CatalogResult<Plugin>.Fail(CatalogFailures.SlugConflict());
    }
}
