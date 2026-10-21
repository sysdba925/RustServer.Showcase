using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;
using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PluginCatalogDbContext(DbContextOptions<PluginCatalogDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Plugin> Plugins => Set<Plugin>();

    public DbSet<PluginSource> PluginSources => Set<PluginSource>();

    public DbSet<PluginMedia> PluginMedia => Set<PluginMedia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SiteConfiguration());
        modelBuilder.ApplyConfiguration(new PluginSourceConfiguration());
        modelBuilder.ApplyConfiguration(new PluginMediaConfiguration());
        modelBuilder.ApplyConfiguration(new PluginConfiguration());
        modelBuilder.ConfigureOrganization();
    }
}
