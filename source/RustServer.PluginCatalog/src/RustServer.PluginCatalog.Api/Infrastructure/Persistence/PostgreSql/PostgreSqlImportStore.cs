using System.Data;
using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Import;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlImportStore(PluginCatalogDbContext db) : IImportStore
{
    public async Task<ImportResult> ExecuteAsync(ImportRequest request, bool commit, CancellationToken ct)
    {
        await using var transaction = commit ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        if (commit) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724637591)", ct);
        var plugins = await db.Plugins.Include(p => p.Sources).ToListAsync(ct);
        var sites = await db.Sites.ToListAsync(ct);
        var tags = await db.Set<Tag>().ToListAsync(ct);
        var folders = await db.Set<Folder>().ToListAsync(ct);
        var errors = new List<string>();
        if (request.SchemaVersion != 2) errors.Add("schemaVersion must be 2.");
        if (request.Plugins is null || request.Plugins.Count == 0) errors.Add("plugins must contain at least one entry.");
        var entries = new List<ImportEntry>();
        var pending = new List<(Plugin Plugin, ImportPlugin Input)>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (input, index) in (request.Plugins ?? []).Select((p, i) => (p, i)))
        {
            var entryErrors = new List<string>();
            var duplicates = new List<string>();
            var unknownSites = new List<string>();
            var unknownTags = new List<string>();
            var unknownFolders = new List<string>();
            Plugin? plugin = null;
            var urls = new List<string>();
            if (input is null) entryErrors.Add("Entry must be an object.");
            else
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    plugin = new Plugin(Guid.NewGuid(), input.Name!, PluginSlug.FromName(input.Name), input.Description, input.CurrentKnownVersion, input.LastPublishedDate, now, now);
                    plugin.SetLibraryDetails(input.Author, input.Notes, input.SourceDescription, input.Price?.Amount, input.Price?.Currency);
                    if (input.Price is not null && input.Price.Amount is null) throw new ArgumentException("Price amount and currency are required together.");
                }
                catch (ArgumentException e) { entryErrors.Add(e.Message); }
                foreach (var source in input.Sources ?? [])
                {
                    try
                    {
                        if (source is null) throw new ArgumentException("Source must be an object.");
                        var normalized = new PluginSource(Guid.NewGuid(), plugin?.Id ?? Guid.NewGuid(), null, source.Url!, source.Label).Url;
                        if (urls.Contains(normalized, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Repeated source URL in entry.");
                        urls.Add(normalized);
                        CheckName(source.Site, sites.Select(s => s.Name), unknownSites, entryErrors, optional: true);
                    }
                    catch (ArgumentException e) { entryErrors.Add(e.Message); }
                }
                foreach (var tag in input.Tags ?? []) CheckName(tag, tags.Select(t => t.Name), unknownTags, entryErrors);
                foreach (var folder in input.Folders ?? []) CheckName(folder, folders.Select(f => f.Name), unknownFolders, entryErrors);
                if (plugin is not null)
                {
                    if (!seenNames.Add(plugin.Name) || plugins.Any(p => Same(p.Name, plugin.Name))) duplicates.Add("name: " + plugin.Name);
                    foreach (var url in urls)
                        if (!seenUrls.Add(url) || plugins.Any(p => p.Sources.Any(s => Same(s.Url, url)))) duplicates.Add("url: " + url);
                    if (entryErrors.Count == 0 && duplicates.Count == 0) pending.Add((plugin, input));
                }
            }
            entries.Add(new(index, plugin?.Name ?? input?.Name, input, entryErrors, duplicates, unknownSites.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), unknownTags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), unknownFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        }
        var preview = new ImportPreview(errors, entries);
        if (!commit || errors.Count > 0 || entries.Any(e => e.Errors.Count > 0) || (!request.CreateMissing && entries.Any(e => e.Duplicates.Count == 0 && (e.UnknownSites.Count + e.UnknownTags.Count + e.UnknownFolders.Count > 0))))
            return new(preview, 0, 0, false);
        foreach (var (plugin, input) in pending)
        {
            if (plugins.Any(p => Same(p.Slug, plugin.Slug))) plugin.SetSlug(PluginSlug.WithSuffix(plugin.Slug));
            foreach (var source in input.Sources ?? [])
            {
                Site? site = null;
                if (!string.IsNullOrWhiteSpace(source!.Site))
                {
                    site = sites.FirstOrDefault(s => Same(s.Name, source.Site));
                    if (site is null) { site = new Site { Id = Guid.NewGuid(), Name = source.Site.Trim() }; sites.Add(site); db.Sites.Add(site); }
                }
                plugin.AddSource(new PluginSource(Guid.NewGuid(), plugin.Id, site?.Id, source!.Url!, source.Label));
            }
            foreach (var name in (input.Tags ?? []).Select(n => n!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var tag = tags.FirstOrDefault(t => Same(t.Name, name));
                if (tag is null) { tag = new Tag { Id = Guid.NewGuid(), Name = name, Color = "#808080" }; tags.Add(tag); db.Set<Tag>().Add(tag); }
                plugin.Tags.Add(tag);
            }
            foreach (var name in (input.Folders ?? []).Select(n => n!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var folder = folders.FirstOrDefault(f => Same(f.Name, name));
                if (folder is null) { folder = new Folder { Id = Guid.NewGuid(), Name = name }; folders.Add(folder); db.Set<Folder>().Add(folder); }
                plugin.Folders.Add(folder);
            }
            plugins.Add(plugin);
            db.Plugins.Add(plugin);
        }
        await db.SaveChangesAsync(ct);
        await transaction!.CommitAsync(ct);
        return new(preview, pending.Count, entries.Count - pending.Count, true);
    }
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static void CheckName(string? name, IEnumerable<string> existing, List<string> unknown, List<string> errors, bool optional = false)
    {
        if (optional && name is null) return;
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) { errors.Add("Names must be 1–200 characters."); return; }
        if (!existing.Any(value => Same(value, name))) unknown.Add(name.Trim());
    }
}
