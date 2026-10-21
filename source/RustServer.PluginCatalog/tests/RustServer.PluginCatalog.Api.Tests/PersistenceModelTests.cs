using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;
using RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

namespace RustServer.PluginCatalog.Api.Tests;

public sealed class PersistenceModelTests
{
    [Fact]
    public async Task ModelStoresAndReadsLinkedCatalogEntities()
    {
        var options = new DbContextOptionsBuilder<PluginCatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var pluginId = Guid.NewGuid();
        var media = new PluginMedia(
            Guid.NewGuid(),
            pluginId,
            PluginMediaKind.Icon,
            "Primary icon",
            0,
            $"media/{pluginId}/icon/original.png",
            null,
            "image/png");
        var plugin = new Plugin(
            pluginId,
            "Sample Plugin",
            "Sample-Plugin",
            "Sample catalog entry.",
            "1.2.3",
            new DateOnly(2026, 10, 1),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
        var source = new PluginSource(
            Guid.NewGuid(),
            pluginId,
            null,
            "https://github.com/example/sample-plugin",
            null);

        plugin.AddMedia(media);
        plugin.SetIcon(media);
        plugin.AddSource(source);

        await using var context = new PluginCatalogDbContext(options);
        context.Plugins.Add(plugin);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var stored = await context.Plugins
            .Include(item => item.Sources)
            .Include(item => item.Media)
            .SingleAsync(item => item.Id == pluginId);

        Assert.Equal("sample-plugin", stored.Slug);
        Assert.Equal(media.Id, stored.IconMediaId);
        Assert.Single(stored.Sources);
        Assert.Single(stored.Media);

        var postgreSqlOptions = new DbContextOptionsBuilder<PluginCatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=not_used")
            .Options;
        await using var postgreSqlContext = new PluginCatalogDbContext(postgreSqlOptions);
        var schemaSql = postgreSqlContext.Database.GenerateCreateScript();

        Assert.Contains("CREATE TABLE plugins", schemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE plugin_sources", schemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE plugin_media", schemaSql, StringComparison.Ordinal);
        var initialSql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "provisioning", "database", "postgresql", "initial-schema-current.sql"));
        foreach (var column in new[] { "author", "notes", "source_description", "price", "currency", "plugin_tags", "plugin_folders", "tag_id", "folder_id" })
        {
            Assert.Contains(column, schemaSql, StringComparison.Ordinal);
            Assert.Contains(column, initialSql, StringComparison.Ordinal);
        }
    }
}
