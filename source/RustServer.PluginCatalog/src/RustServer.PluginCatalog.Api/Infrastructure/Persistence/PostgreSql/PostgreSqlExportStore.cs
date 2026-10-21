using System.Data;
using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlExportStore(PluginCatalogDbContext db) : IExportStore
{
    public async Task<ExportCatalog> ReadSnapshotAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var plugins = await db.Plugins.AsNoTracking().Include(p => p.Tags).Include(p => p.Folders).AsSplitQuery().ToListAsync(ct);
        var sites = await db.Sites.AsNoTracking().ToListAsync(ct);
        var tags = await db.Set<Tag>().AsNoTracking().ToListAsync(ct);
        var folders = await db.Set<Folder>().AsNoTracking().ToListAsync(ct);
        var sources = await db.PluginSources.AsNoTracking().ToListAsync(ct);
        var media = await db.PluginMedia.AsNoTracking().ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return new ExportCatalog(
            plugins.Select(p => new ExportPlugin(p.Id, p.Name, p.Slug, p.Description, p.Author, p.Notes, p.SourceDescription, p.Price, p.Currency, p.CurrentKnownVersion, p.LastPublishedDate, p.IconMediaId, p.CreatedAt, p.UpdatedAt, p.Tags.Select(t => t.Id).ToArray(), p.Folders.Select(f => f.Id).ToArray())).ToArray(),
            sites.Select(s => new ExportSite(s.Id, s.Name, s.BaseUrl)).ToArray(),
            tags.Select(t => new ExportTag(t.Id, t.Name, t.Color)).ToArray(),
            folders.Select(f => new ExportFolder(f.Id, f.Name)).ToArray(),
            sources.Select(s => new ExportSource(s.Id, s.PluginId, s.SiteId, s.Url, s.Label)).ToArray(),
            media.Select(m => new ExportMedia(m.Id, m.PluginId, m.Kind.ToString(), m.Caption, m.SortOrder, m.StorageKey, m.ExternalUrl, m.ContentType, m.StorageKey is null ? null : $"media/{m.Id:D}")).ToArray());
    }
}
