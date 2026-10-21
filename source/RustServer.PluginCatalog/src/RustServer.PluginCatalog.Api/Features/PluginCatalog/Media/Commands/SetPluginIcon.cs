using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Commands;

public sealed record SetPluginIconCommand(Guid PluginId, Guid? MediaId);

public sealed class SetPluginIconHandler(IPluginCatalogStore store)
{
    public async Task<CatalogResult<Plugin>> HandleAsync(
        SetPluginIconCommand command,
        CancellationToken cancellationToken)
    {
        var plugin = await store.GetPluginForUpdateAsync(command.PluginId, cancellationToken);
        if (plugin is null)
        {
            return CatalogResult<Plugin>.Fail(CatalogFailures.PluginNotFound());
        }

        if (command.MediaId is null)
        {
            plugin.SetIcon(null);
        }
        else
        {
            var media = plugin.Media.SingleOrDefault(item => item.Id == command.MediaId.Value);
            if (media is null)
            {
                return CatalogResult<Plugin>.Fail(CatalogFailures.MediaNotFound());
            }

            if (media.Kind != PluginMediaKind.Icon)
            {
                return CatalogResult<Plugin>.Fail(CatalogFailures.MediaIsNotIcon());
            }

            plugin.SetIcon(media);
        }

        await store.SaveChangesAsync(cancellationToken);
        return CatalogResult<Plugin>.Success(plugin);
    }
}
