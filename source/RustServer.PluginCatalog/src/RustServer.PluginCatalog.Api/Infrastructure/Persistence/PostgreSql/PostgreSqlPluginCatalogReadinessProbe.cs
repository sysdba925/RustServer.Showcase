using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlPluginCatalogReadinessProbe(PluginCatalogDbContext dbContext)
    : IPluginCatalogDatabaseReadinessProbe
{
    public async Task VerifyReadyAsync(CancellationToken cancellationToken)
    {
        await dbContext.Plugins.AsNoTracking()
            .Select(plugin => new { plugin.Id, plugin.Author, plugin.Description, plugin.Notes, plugin.SourceDescription, plugin.Price, plugin.Currency })
            .Take(1)
            .ToListAsync(cancellationToken);
        await dbContext.PluginSources.AsNoTracking()
            .Select(source => new { source.Id, source.SiteId })
            .Take(1)
            .ToListAsync(cancellationToken);
        await dbContext.Sites.AsNoTracking().Select(site => new { site.Id, site.Name, site.BaseUrl }).Take(1).ToListAsync(cancellationToken);
        await dbContext.Set<Tag>().AsNoTracking().Select(tag => new { tag.Id, tag.Name, tag.Color }).Take(1).ToListAsync(cancellationToken);
        await dbContext.Set<Folder>().AsNoTracking().Select(folder => new { folder.Id, folder.Name }).Take(1).ToListAsync(cancellationToken);
        await dbContext.Set<Dictionary<string, object>>("plugin_tags").AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await dbContext.Set<Dictionary<string, object>>("plugin_folders").AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await dbContext.PluginMedia.AsNoTracking()
            .Select(media => media.Id)
            .Take(1)
            .ToListAsync(cancellationToken);
    }
}
