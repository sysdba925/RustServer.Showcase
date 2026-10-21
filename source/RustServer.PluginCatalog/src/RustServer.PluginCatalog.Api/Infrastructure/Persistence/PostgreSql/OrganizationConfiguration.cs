using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public static class OrganizationConfiguration
{
    public static void ConfigureOrganization(this ModelBuilder model)
    {
        model.Entity<Tag>(b =>
        {
            b.ToTable("tags");
            b.HasKey(x => x.Id).HasName("pk_tags");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Color).HasColumnName("color").HasMaxLength(7).IsRequired();
        });
        model.Entity<Folder>(b =>
        {
            b.ToTable("folders");
            b.HasKey(x => x.Id).HasName("pk_folders");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        });
        model.Entity<Plugin>().HasMany(x => x.Tags).WithMany().UsingEntity<Dictionary<string, object>>("plugin_tags",
            r => r.HasOne<Tag>().WithMany().HasForeignKey("tag_id").OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_plugin_tags_tag"),
            l => l.HasOne<Plugin>().WithMany().HasForeignKey("plugin_id").OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_plugin_tags_plugin"),
            j =>
            {
                j.ToTable("plugin_tags");
                j.HasKey("plugin_id", "tag_id").HasName("pk_plugin_tags");
                j.HasIndex("tag_id", "plugin_id").HasDatabaseName("ix_plugin_tags_tag_id_plugin_id");
        });
        model.Entity<Plugin>().HasMany(x => x.Folders).WithMany().UsingEntity<Dictionary<string, object>>("plugin_folders",
            r => r.HasOne<Folder>().WithMany().HasForeignKey("folder_id").OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_plugin_folders_folder"),
            l => l.HasOne<Plugin>().WithMany().HasForeignKey("plugin_id").OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_plugin_folders_plugin"),
            j =>
            {
                j.ToTable("plugin_folders");
                j.HasKey("plugin_id", "folder_id").HasName("pk_plugin_folders");
                j.HasIndex("folder_id", "plugin_id").HasDatabaseName("ix_plugin_folders_folder_id_plugin_id");
        });
    }
}
