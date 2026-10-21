using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Import;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Ports;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Ports;
using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

public static class DependencyInjection
{
    public static IServiceCollection AddPluginCatalogPostgreSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<PluginCatalogDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("PluginCatalog");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:PluginCatalog must be configured before the catalog database is used.");
            }

            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IExportStore, PostgreSqlExportStore>();
        services.AddScoped<IRestoreStore, PostgreSqlRestoreStore>();
        services.AddScoped<IImportStore, PostgreSqlImportStore>();
        services.AddScoped<ISiteStore, PostgreSqlSiteStore>();
        services.AddScoped<IOrganizationStore, PostgreSqlOrganizationStore>();
        services.AddScoped<IPluginCatalogStore, PostgreSqlPluginCatalogStore>();
        services.AddScoped<IPluginCatalogDatabaseReadinessProbe, PostgreSqlPluginCatalogReadinessProbe>();

        return services;
    }
}
