using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PluginMediaConfiguration : IEntityTypeConfiguration<PluginMedia>
{
    public void Configure(EntityTypeBuilder<PluginMedia> builder)
    {
        builder.ToTable("plugin_media", table =>
        {
            table.HasCheckConstraint(
                "ck_plugin_media_location",
                "(storage_key IS NOT NULL AND btrim(storage_key) <> '' AND external_url IS NULL) OR (storage_key IS NULL AND external_url IS NOT NULL AND btrim(external_url) <> '')");
            table.HasCheckConstraint("ck_plugin_media_sort_order", "sort_order >= 0");
        });

        builder.HasKey(media => media.Id)
            .HasName("pk_plugin_media");

        builder.HasAlternateKey(media => new { media.PluginId, media.Id })
            .HasName("ak_plugin_media_plugin_id_id");

        builder.Property(media => media.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(media => media.PluginId)
            .HasColumnName("plugin_id")
            .IsRequired();

        builder.Property(media => media.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(media => media.Caption)
            .HasColumnName("caption")
            .HasMaxLength(500);

        builder.Property(media => media.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(media => media.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(1024);

        builder.Property(media => media.ExternalUrl)
            .HasColumnName("external_url")
            .HasMaxLength(2048);

        builder.Property(media => media.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(255);

        builder.HasOne(media => media.Plugin)
            .WithMany(plugin => plugin.Media)
            .HasForeignKey(media => media.PluginId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_plugin_media_plugin");

        builder.HasIndex(media => new { media.PluginId, media.SortOrder })
            .HasDatabaseName("ix_plugin_media_plugin_id_sort_order");
    }
}
