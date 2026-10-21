using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public sealed class PluginConfiguration : IEntityTypeConfiguration<Plugin>
{
    public void Configure(EntityTypeBuilder<Plugin> builder)
    {
        builder.ToTable("plugins");

        builder.HasKey(plugin => plugin.Id)
            .HasName("pk_plugins");

        builder.Property(plugin => plugin.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(plugin => plugin.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(plugin => plugin.Slug)
            .HasColumnName("slug")
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(plugin => plugin.Description)
            .HasColumnName("description")
            .HasMaxLength(8000);

        builder.Property(plugin => plugin.Author).HasColumnName("author").HasMaxLength(200);
        builder.Property(plugin => plugin.Notes).HasColumnName("notes");
        builder.Property(plugin => plugin.SourceDescription).HasColumnName("source_description");
        builder.Property(plugin => plugin.Price).HasColumnName("price").HasPrecision(18, 2);
        builder.Property(plugin => plugin.Currency).HasColumnName("currency").HasMaxLength(3);

        builder.Property(plugin => plugin.CurrentKnownVersion)
            .HasColumnName("current_known_version")
            .HasMaxLength(100);

        builder.Property(plugin => plugin.LastPublishedDate)
            .HasColumnName("last_published_date")
            .HasColumnType("date");

        builder.Property(plugin => plugin.IconMediaId)
            .HasColumnName("icon_media_id");

        builder.Property(plugin => plugin.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(plugin => plugin.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(plugin => plugin.Slug)
            .IsUnique()
            .HasDatabaseName("ux_plugins_slug");

        builder.HasOne(plugin => plugin.IconMedia)
            .WithMany()
            .HasForeignKey(plugin => new { plugin.Id, plugin.IconMediaId })
            .HasPrincipalKey(media => new { media.PluginId, media.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_plugins_icon_media");
    }
}
