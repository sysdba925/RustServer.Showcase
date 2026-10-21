using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Import;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Queries;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Queries;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Commands;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sources.Queries;
using System.Text.Json.Serialization;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog;

public static class PluginCatalogFeatureRegistration
{
    public static IServiceCollection AddPluginCatalogFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

        services.AddScoped<ExportHandler>();
        services.AddScoped<RestorePreviewHandler>();
        services.AddScoped<RestoreCommitHandler>();
        services.AddScoped<ImportHandler>();
        services.AddScoped<SiteHandler>();
        services.AddScoped<OrganizationHandler>();
        services.AddScoped<CreatePluginHandler>();
        services.AddScoped<UpdatePluginHandler>();
        services.AddScoped<DeletePluginHandler>();
        services.AddScoped<GetPluginDetailsHandler>();
        services.AddScoped<ListPluginsHandler>();
        services.AddScoped<CreateSourceHandler>();
        services.AddScoped<UpdateSourceHandler>();
        services.AddScoped<DeleteSourceHandler>();
        services.AddScoped<GetSourcesHandler>();
        services.AddScoped<UploadPluginMediaHandler>();
        services.AddScoped<CreateExternalPluginMediaHandler>();
        services.AddScoped<UpdatePluginMediaHandler>();
        services.AddScoped<DeletePluginMediaHandler>();
        services.AddScoped<SetPluginIconHandler>();
        services.AddScoped<GetPluginMediaHandler>();
        services.AddScoped<GetPluginMediaContentHandler>();

        return services;
    }
}
