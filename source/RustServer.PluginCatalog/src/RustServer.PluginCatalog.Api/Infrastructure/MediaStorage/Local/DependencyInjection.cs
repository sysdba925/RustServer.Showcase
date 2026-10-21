using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;

namespace RustServer.PluginCatalog.Api.Infrastructure.MediaStorage.Local;

public static class DependencyInjection
{
    private const long MultipartOverheadBytes = 64 * 1024;

    public static IServiceCollection AddPluginCatalogLocalMediaStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredOptions = configuration
            .GetSection(MediaStorageOptions.SectionName)
            .Get<MediaStorageOptions>() ?? new MediaStorageOptions();
        var maxUploadBytes = configuredOptions.MaxUploadBytes > 0
            ? configuredOptions.MaxUploadBytes
            : MediaStorageOptions.DefaultMaxUploadBytes;
        var maxMultipartBytes = checked(maxUploadBytes + MultipartOverheadBytes);

        services.AddOptions<MediaStorageOptions>()
            .BindConfiguration(MediaStorageOptions.SectionName);

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxMultipartBytes;
            options.MemoryBufferThreshold = 64 * 1024;
        });
        services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = maxMultipartBytes);
        services.AddSingleton<IPluginMediaStorage, LocalPluginMediaStorage>();
        services.AddSingleton<IRestoreMediaStorage, LocalPluginMediaStorage>();
        services.AddSingleton<IPluginMediaStorageReadinessProbe, LocalPluginMediaStorageReadinessProbe>();

        return services;
    }
}
