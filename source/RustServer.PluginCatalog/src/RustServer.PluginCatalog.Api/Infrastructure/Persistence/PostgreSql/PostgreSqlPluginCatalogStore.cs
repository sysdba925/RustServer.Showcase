using Microsoft.EntityFrameworkCore;
using Npgsql;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlPluginCatalogStore(PluginCatalogDbContext dbContext) : IPluginCatalogStore
{
    public async Task<PagedPlugins> ListPluginsAsync(PluginListCriteria criteria, CancellationToken cancellationToken)
    {
        var query = dbContext.Plugins
            .AsNoTracking()
            .Include(plugin => plugin.IconMedia)
            .Include(plugin => plugin.Sources)
            .Include(plugin => plugin.Tags)
            .Include(plugin => plugin.Folders)
            .AsSplitQuery();

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.ToLowerInvariant();
            query = query.Where(plugin =>
                plugin.Name.ToLower().Contains(search)
                || plugin.Slug.ToLower().Contains(search)
                || (plugin.Description != null && plugin.Description.ToLower().Contains(search))
                || (plugin.Author != null && plugin.Author.ToLower().Contains(search)));
        }

        if (criteria.SiteId is { } siteId)
        {
            query = query.Where(plugin => plugin.Sources.Any(source => source.SiteId == siteId));
        }

        if (criteria.TagId is { } tagId)
        {
            query = query.Where(plugin => plugin.Tags.Any(tag => tag.Id == tagId));
        }

        if (criteria.FolderId is { } folderId)
        {
            query = query.Where(plugin => plugin.Folders.Any(folder => folder.Id == folderId));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(plugin => plugin.Name)
            .ThenBy(plugin => plugin.Id)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedPlugins(items, criteria.Page, criteria.PageSize, totalItems);
    }

    public Task<Plugin?> GetPluginDetailsAsync(Guid pluginId, CancellationToken cancellationToken) =>
        dbContext.Plugins
            .AsNoTracking()
            .Include(plugin => plugin.Sources)
            .Include(plugin => plugin.Media)
            .Include(plugin => plugin.IconMedia)
            .Include(plugin => plugin.Tags)
            .Include(plugin => plugin.Folders)
            .SingleOrDefaultAsync(plugin => plugin.Id == pluginId, cancellationToken);

    public Task<Plugin?> GetPluginForUpdateAsync(Guid pluginId, CancellationToken cancellationToken) =>
        dbContext.Plugins
            .Include(plugin => plugin.Sources)
            .Include(plugin => plugin.Media)
            .Include(plugin => plugin.IconMedia)
            .Include(plugin => plugin.Tags)
            .Include(plugin => plugin.Folders)
            .SingleOrDefaultAsync(plugin => plugin.Id == pluginId, cancellationToken);

    public async Task<IReadOnlyList<Tag>> GetTagsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.Set<Tag>().Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Folder>> GetFoldersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.Set<Folder>().Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken);

    public Task<bool> SiteExistsAsync(Guid siteId, CancellationToken cancellationToken) =>
        dbContext.Sites.AnyAsync(site => site.Id == siteId, cancellationToken);

    public Task<bool> PluginExistsAsync(Guid pluginId, CancellationToken cancellationToken) =>
        dbContext.Plugins.AnyAsync(plugin => plugin.Id == pluginId, cancellationToken);

    public Task<bool> SlugExistsAsync(Guid pluginId, string slug, CancellationToken cancellationToken) =>
        dbContext.Plugins.AnyAsync(plugin => plugin.Id != pluginId && plugin.Slug == slug, cancellationToken);

    public async Task<IReadOnlyList<PluginSource>> GetSourcesAsync(Guid pluginId, CancellationToken cancellationToken) =>
        await dbContext.PluginSources
            .AsNoTracking()
            .Where(source => source.PluginId == pluginId)
            .OrderBy(source => source.Id)
            .ToListAsync(cancellationToken);

    public Task<PluginSource?> GetSourceForUpdateAsync(Guid pluginId, Guid sourceId, CancellationToken cancellationToken) =>
        dbContext.PluginSources
            .SingleOrDefaultAsync(source => source.PluginId == pluginId && source.Id == sourceId, cancellationToken);

    public async Task<IReadOnlyList<PluginMedia>> GetMediaAsync(Guid pluginId, CancellationToken cancellationToken) =>
        await dbContext.PluginMedia
            .AsNoTracking()
            .Where(media => media.PluginId == pluginId)
            .OrderBy(media => media.SortOrder)
            .ThenBy(media => media.Id)
            .ToListAsync(cancellationToken);

    public Task<PluginMedia?> GetMediaForUpdateAsync(Guid pluginId, Guid mediaId, CancellationToken cancellationToken) =>
        dbContext.PluginMedia
            .SingleOrDefaultAsync(media => media.PluginId == pluginId && media.Id == mediaId, cancellationToken);

    public Task<PluginMedia?> GetMediaForReadAsync(Guid pluginId, Guid mediaId, CancellationToken cancellationToken) =>
        dbContext.PluginMedia
            .AsNoTracking()
            .SingleOrDefaultAsync(media => media.PluginId == pluginId && media.Id == mediaId, cancellationToken);

    public void AddPlugin(Plugin plugin) => dbContext.Plugins.Add(plugin);

    public void AddSource(PluginSource source) => dbContext.PluginSources.Add(source);

    public void AddMedia(PluginMedia media) => dbContext.PluginMedia.Add(media);

    public void RemoveSource(PluginSource source) => dbContext.PluginSources.Remove(source);

    public void RemoveMedia(PluginMedia media) => dbContext.PluginMedia.Remove(media);

    public async Task<CatalogSaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return CatalogSaveOutcome.Saved;
        }
        catch (DbUpdateException exception) when (IsSlugConflict(exception))
        {
            return CatalogSaveOutcome.SlugConflict;
        }
    }

    public async Task<IReadOnlyList<DeletedMedia>?> DeletePluginAsync(Guid pluginId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var plugin = await dbContext.Plugins.FromSqlInterpolated($"SELECT * FROM plugins WHERE id = {pluginId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (plugin is null)
        {
            return null;
        }

        var media = await dbContext.PluginMedia.Where(item => item.PluginId == pluginId)
            .Select(item => new DeletedMedia(item.Id, item.StorageKey)).ToListAsync(cancellationToken);
        plugin.SetIcon(null);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.Plugins.Remove(plugin);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return media;
    }

    private static bool IsSlugConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_plugins_slug"
        };

}
