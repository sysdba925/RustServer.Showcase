using RustServer.PluginCatalog.Api.Infrastructure.MediaStorage;
using RustServer.PluginCatalog.Api.Infrastructure.MediaStorage.Local;
using RustServer.PluginCatalog.Api.Infrastructure.Persistence;
using RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

namespace RustServer.PluginCatalog.Api.CompositionRoot;

public static class CatalogInfrastructureDependencyInjection
{
    public static IServiceCollection AddPluginCatalogInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName);

        RegisterDatabaseProvider(services, configuration["Database:Provider"]);
        RegisterMediaStorageProvider(services, configuration["MediaStorage:Provider"], configuration);

        return services;
    }

    private static void RegisterDatabaseProvider(IServiceCollection services, string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                "Database:Provider must be configured. Supported database providers: PostgreSql.");
        }

        if (string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            services.AddPluginCatalogPostgreSql();
            return;
        }

        throw new InvalidOperationException(
            $"Database provider '{provider}' is not supported. Supported database providers: PostgreSql.");
    }

    private static void RegisterMediaStorageProvider(
        IServiceCollection services,
        string? provider,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                "MediaStorage:Provider must be configured. Supported media storage providers: Local.");
        }

        if (string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            services.AddPluginCatalogLocalMediaStorage(configuration);
            return;
        }

        throw new InvalidOperationException(
            $"Media storage provider '{provider}' is not supported. Supported media storage providers: Local.");
    }
}
