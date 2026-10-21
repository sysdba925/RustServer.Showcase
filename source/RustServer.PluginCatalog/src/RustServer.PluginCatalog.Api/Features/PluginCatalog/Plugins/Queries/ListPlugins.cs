using RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Queries;

public sealed record ListPluginsQuery(
    int Page,
    int PageSize,
    string? Search,
    Guid? SiteId,
    Guid? TagId = null,
    Guid? FolderId = null);

public sealed class ListPluginsHandler(IPluginCatalogStore store)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPage = 100000;
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 200;

    public async Task<CatalogResult<PagedPlugins>> HandleAsync(
        ListPluginsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Page is < 1 or > MaxPage)
        {
            return CatalogResult<PagedPlugins>.Fail(
                CatalogFailures.Validation($"page must be between 1 and {MaxPage}."));
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return CatalogResult<PagedPlugins>.Fail(
                CatalogFailures.Validation($"pageSize must be between 1 and {MaxPageSize}."));
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search?.Length > SearchMaxLength)
        {
            return CatalogResult<PagedPlugins>.Fail(
                CatalogFailures.Validation($"search must not exceed {SearchMaxLength} characters."));
        }

        var plugins = await store.ListPluginsAsync(
            new PluginListCriteria(query.Page, query.PageSize, search, query.SiteId, query.TagId, query.FolderId),
            cancellationToken);

        return CatalogResult<PagedPlugins>.Success(plugins);
    }
}
