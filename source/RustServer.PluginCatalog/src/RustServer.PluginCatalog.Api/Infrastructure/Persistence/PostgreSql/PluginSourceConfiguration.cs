using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PluginSourceConfiguration : IEntityTypeConfiguration<PluginSource>
{
    public void Configure(EntityTypeBuilder<PluginSource> builder)
    {
        builder.ToTable("plugin_sources");

        builder.HasKey(source => source.Id)
            .HasName("pk_plugin_sources");

        builder.Property(source => source.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(source => source.PluginId)
            .HasColumnName("plugin_id")
            .IsRequired();

        builder.Property(source => source.SiteId).HasColumnName("site_id");

        builder.HasOne<Site>().WithMany().HasForeignKey(source => source.SiteId)
            .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_plugin_sources_site");

        builder.Property(source => source.Url)
            .HasColumnName("url")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(source => source.Label)
            .HasColumnName("label")
            .HasMaxLength(200);

        builder.HasOne(source => source.Plugin)
            .WithMany(plugin => plugin.Sources)
            .HasForeignKey(source => source.PluginId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_plugin_sources_plugin");

        builder.HasIndex(source => new { source.SiteId, source.PluginId })
            .HasDatabaseName("ix_plugin_sources_site_id_plugin_id");
    }
}
