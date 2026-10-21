using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlRestoreStore(PluginCatalogDbContext db) : IRestoreStore
{
    public async Task RestoreAsync(ExportCatalog catalog, Func<CancellationToken, Task> writeMedia, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE plugins, sites, tags, folders, plugin_sources, plugin_media, plugin_tags, plugin_folders IN SHARE ROW EXCLUSIVE MODE", ct);
            if (await db.Plugins.AnyAsync(ct) || await db.Sites.AnyAsync(ct) || await db.Set<Tag>().AnyAsync(ct) ||
                await db.Set<Folder>().AnyAsync(ct) || await db.PluginSources.AnyAsync(ct) || await db.PluginMedia.AnyAsync(ct))
                throw new RestoreConflictException();

            var tags = catalog.Tags.Select(t => new Tag { Id = t.Id, Name = t.Name, Color = t.Color }).ToDictionary(t => t.Id);
            var folders = catalog.Folders.Select(f => new Folder { Id = f.Id, Name = f.Name }).ToDictionary(f => f.Id);
            db.AddRange(tags.Values); db.AddRange(folders.Values);
            db.Sites.AddRange(catalog.Sites.Select(s => new Site { Id = s.Id, Name = s.Name, BaseUrl = s.BaseUrl }));
            var plugins = new Dictionary<Guid, Plugin>();
            foreach (var item in catalog.Plugins)
            {
                var plugin = new Plugin(item.Id, item.Name, item.Slug, item.Description, item.CurrentKnownVersion, item.LastPublishedDate, item.CreatedAt, item.UpdatedAt);
                plugin.SetLibraryDetails(item.Author, item.Notes, item.SourceDescription, item.Price, item.Currency);
                foreach (var id in item.TagIds) plugin.Tags.Add(tags[id]);
                foreach (var id in item.FolderIds) plugin.Folders.Add(folders[id]);
                plugins.Add(item.Id, plugin);
                db.Plugins.Add(plugin);
            }
            await db.SaveChangesAsync(ct);
            db.PluginSources.AddRange(catalog.Sources.Select(s => new PluginSource(s.Id, s.PluginId, s.SiteId, s.Url, s.Label)));
            var media = new Dictionary<Guid, PluginMedia>();
            foreach (var item in catalog.Media)
            {
                if (!Enum.TryParse<PluginMediaKind>(item.Kind, false, out var kind) || !Enum.IsDefined(kind)) throw new InvalidDataException();
                var value = new PluginMedia(item.Id, item.PluginId, kind, item.Caption, item.SortOrder, item.StorageKey, item.ExternalUrl, item.ContentType);
                media.Add(item.Id, value);
                db.PluginMedia.Add(value);
            }
            await db.SaveChangesAsync(ct);
            foreach (var item in catalog.Plugins.Where(p => p.IconMediaId is not null)) plugins[item.Id].SetIcon(media[item.IconMediaId!.Value]);
            await db.SaveChangesAsync(ct);
            await writeMedia(ct);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new RestoreCommitUncertainException(new AggregateException(error, rollback)); }
            throw;
        }
        try { await transaction.CommitAsync(CancellationToken.None); }
        catch (Exception error) { throw new RestoreCommitUncertainException(error); }
    }
}
