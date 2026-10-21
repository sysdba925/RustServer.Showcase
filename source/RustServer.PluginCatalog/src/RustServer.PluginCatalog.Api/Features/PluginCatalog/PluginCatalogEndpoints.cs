using RustServer.PluginCatalog.Api.Features.PluginCatalog.Export;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Import;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Media.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Transport;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Restore;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Transport;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog;

public static class PluginCatalogEndpoints
{
    public static IEndpointRouteBuilder MapPluginCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapExportEndpoints();
        endpoints.MapRestorePreviewEndpoints();
        endpoints.MapRestoreCommitEndpoints();
        endpoints.MapImportEndpoints();
        endpoints.MapSiteEndpoints();
        endpoints.MapOrganizationEndpoints();
        endpoints.MapPluginEndpoints();
        endpoints.MapPluginMediaEndpoints();
        return endpoints;
    }
}
