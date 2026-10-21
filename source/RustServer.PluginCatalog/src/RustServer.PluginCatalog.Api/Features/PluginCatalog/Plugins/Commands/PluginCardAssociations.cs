using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Commands;

internal static class PluginCardAssociations
{
    public static async Task<CatalogFailure?> ApplyAsync(IPluginCatalogStore store, Plugin plugin,
        List<PluginSourceInput>? inputs, List<Guid>? tagIds, List<Guid>? folderIds, CancellationToken ct)
    {
        var sources = new List<PluginSource>();
        try
        {
            foreach (var input in inputs ?? [])
            {
                if (input is null)
                {
                    return CatalogFailures.Validation("Source must be an object.");
                }

                if (input.SiteId is { } siteId && !await store.SiteExistsAsync(siteId, ct))
                {
                    return CatalogFailures.Validation($"Site {siteId} does not exist.");
                }

                sources.Add(new PluginSource(Guid.NewGuid(), plugin.Id, input.SiteId, input.Url!, input.Label));
            }
        }
        catch (ArgumentException e) { return CatalogFailures.Validation(e.Message); }
        var requestedTags = (tagIds ?? []).Distinct().ToArray();
        var requestedFolders = (folderIds ?? []).Distinct().ToArray();
        var tags = await store.GetTagsAsync(requestedTags, ct);
        var folders = await store.GetFoldersAsync(requestedFolders, ct);
        if (tags.Count != requestedTags.Length)
        {
            return CatalogFailures.Validation("One or more tag IDs do not exist.");
        }

        if (folders.Count != requestedFolders.Length)
        {
            return CatalogFailures.Validation("One or more folder IDs do not exist.");
        }

        foreach (var source in plugin.Sources.ToArray())
        {
            plugin.Sources.Remove(source); store.RemoveSource(source);
        }

        foreach (var source in sources)
        {
            plugin.AddSource(source); store.AddSource(source);
        }

        plugin.Tags.Clear();
        foreach (var tag in tags)
        {
            plugin.Tags.Add(tag);
        }

        plugin.Folders.Clear();
        foreach (var folder in folders)
        {
            plugin.Folders.Add(folder);
        }

        return null;
    }
}
