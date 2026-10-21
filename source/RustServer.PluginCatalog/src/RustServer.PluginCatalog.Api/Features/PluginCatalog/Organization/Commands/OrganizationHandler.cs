using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Ports;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;
using System.Text.RegularExpressions;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Commands;

public sealed class OrganizationHandler(IOrganizationStore store, IPluginCatalogStore plugins)
{
    public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct) => store.ListTagsAsync(ct);
    public Task<IReadOnlyList<Folder>> ListFoldersAsync(CancellationToken ct) => store.ListFoldersAsync(ct);
    public Task<Tag?> GetTagAsync(Guid id, CancellationToken ct) => store.FindTagAsync(id, ct);
    public Task<Folder?> GetFolderAsync(Guid id, CancellationToken ct) => store.FindFolderAsync(id, ct);

    public async Task<CatalogResult<Tag>> SaveTagAsync(Guid? id, string? name, string? color, CancellationToken ct)
    {
        if (!ValidName(name) || color is null || !Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
        {
            return CatalogResult<Tag>.Fail(CatalogFailures.Validation("Name must be 1–200 characters and color must be #RRGGBB."));
        }

        var tag = id.HasValue ? await store.FindTagAsync(id.Value, ct) : new Tag { Id = Guid.NewGuid() };
        if (tag is null)
        {
            return CatalogResult<Tag>.Fail(NotFound("tag"));
        }

        tag.Name = name!.Trim();
        tag.Color = color.ToUpperInvariant();
        if (!id.HasValue)
        {
            store.AddTag(tag);
        }

        await store.SaveAsync(ct);
        return CatalogResult<Tag>.Success(tag);
    }

    public async Task<CatalogResult<Folder>> SaveFolderAsync(Guid? id, string? name, CancellationToken ct)
    {
        if (!ValidName(name))
        {
            return CatalogResult<Folder>.Fail(CatalogFailures.Validation("Name must be 1–200 characters."));
        }

        var folder = id.HasValue ? await store.FindFolderAsync(id.Value, ct) : new Folder { Id = Guid.NewGuid() };
        if (folder is null)
        {
            return CatalogResult<Folder>.Fail(NotFound("folder"));
        }

        folder.Name = name!.Trim();
        if (!id.HasValue)
        {
            store.AddFolder(folder);
        }

        await store.SaveAsync(ct);
        return CatalogResult<Folder>.Success(folder);
    }

    public async Task<CatalogResult<bool>> DeleteAsync(string kind, Guid id, CancellationToken ct)
    {
        if (kind == "tags")
        {
            var tag = await store.FindTagAsync(id, ct);
            if (tag is null)
            {
                return CatalogResult<bool>.Fail(NotFound("tag"));
            }

            store.RemoveTag(tag);
        }
        else
        {
            var folder = await store.FindFolderAsync(id, ct);
            if (folder is null)
            {
                return CatalogResult<bool>.Fail(NotFound("folder"));
            }

            store.RemoveFolder(folder);
        }
        await store.SaveAsync(ct);
        return CatalogResult<bool>.Success(true);
    }

    public async Task<CatalogResult<bool>> AssignAsync(Guid pluginId, string kind, Guid id, bool assign, CancellationToken ct)
    {
        var plugin = await plugins.GetPluginForUpdateAsync(pluginId, ct);
        if (plugin is null)
        {
            return CatalogResult<bool>.Fail(CatalogFailures.PluginNotFound());
        }

        if (kind == "tags")
        {
            var tag = await store.FindTagAsync(id, ct);
            if (tag is null)
            {
                return CatalogResult<bool>.Fail(NotFound("tag"));
            }

            var existing = plugin.Tags.FirstOrDefault(item => item.Id == id);
            if (assign && existing is null)
            {
                plugin.Tags.Add(tag);
            }

            if (!assign && existing is not null)
            {
                plugin.Tags.Remove(existing);
            }
        }
        else
        {
            var folder = await store.FindFolderAsync(id, ct);
            if (folder is null)
            {
                return CatalogResult<bool>.Fail(NotFound("folder"));
            }

            var existing = plugin.Folders.FirstOrDefault(item => item.Id == id);
            if (assign && existing is null)
            {
                plugin.Folders.Add(folder);
            }

            if (!assign && existing is not null)
            {
                plugin.Folders.Remove(existing);
            }
        }
        await store.SaveAsync(ct);
        return CatalogResult<bool>.Success(true);
    }
    public static CatalogFailure NotFound(string kind) => new(CatalogFailureKind.NotFound, kind + "_not_found", "Library item was not found.", "No " + kind + " has the requested ID.");
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200;
}
