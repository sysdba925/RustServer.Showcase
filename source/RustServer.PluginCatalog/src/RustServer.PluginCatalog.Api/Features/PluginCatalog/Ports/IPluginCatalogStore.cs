using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

public sealed record PluginListCriteria(
    int Page,
    int PageSize,
    string? Search,
    Guid? SiteId,
    Guid? TagId = null,
    Guid? FolderId = null);

public sealed record PagedPlugins(
    IReadOnlyList<Plugin> Items,
    int Page,
    int PageSize,
    int TotalItems);

public enum CatalogSaveOutcome
{
    Saved,
    SlugConflict
}

public sealed record DeletedMedia(Guid Id, string? StorageKey);

public interface IPluginCatalogStore
{
    Task<PagedPlugins> ListPluginsAsync(PluginListCriteria criteria, CancellationToken cancellationToken);

    Task<Plugin?> GetPluginDetailsAsync(Guid pluginId, CancellationToken cancellationToken);

    Task<Plugin?> GetPluginForUpdateAsync(Guid pluginId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tag>> GetTagsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<Folder>> GetFoldersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> SiteExistsAsync(Guid siteId, CancellationToken cancellationToken);

    Task<bool> PluginExistsAsync(Guid pluginId, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(Guid pluginId, string slug, CancellationToken cancellationToken);

    Task<IReadOnlyList<PluginSource>> GetSourcesAsync(Guid pluginId, CancellationToken cancellationToken);

    Task<PluginSource?> GetSourceForUpdateAsync(Guid pluginId, Guid sourceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PluginMedia>> GetMediaAsync(Guid pluginId, CancellationToken cancellationToken);

    Task<PluginMedia?> GetMediaForUpdateAsync(Guid pluginId, Guid mediaId, CancellationToken cancellationToken);

    Task<PluginMedia?> GetMediaForReadAsync(Guid pluginId, Guid mediaId, CancellationToken cancellationToken);

    void AddPlugin(Plugin plugin);

    void AddSource(PluginSource source);

    void AddMedia(PluginMedia media);

    void RemoveSource(PluginSource source);

    void RemoveMedia(PluginMedia media);

    Task<CatalogSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DeletedMedia>?> DeletePluginAsync(Guid pluginId, CancellationToken cancellationToken);
}
