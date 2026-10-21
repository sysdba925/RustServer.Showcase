using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Domain;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Ports;
using System.Diagnostics.CodeAnalysis;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Sites.Commands;

public sealed class SiteHandler(ISiteStore store)
{
    private readonly ISiteStore _store = store;

    public Task<IReadOnlyList<Site>> ListAsync(CancellationToken ct)
    {
        return _store.ListAsync(ct);
    }

    public Task<Site?> FindAsync(Guid id, CancellationToken ct)
    {
        return _store.FindAsync(id, ct);
    }

    public static CatalogFailure NotFound()
    {
        return new(CatalogFailureKind.NotFound, "site_not_found", "Site was not found.", "No site has the requested ID.");
    }

    public async Task<CatalogResult<Site>> SaveAsync(Guid? id, string? name, string? baseUrl, CancellationToken ct)
    {
        var trimmedName = name?.Trim();
        if (ValidateData(baseUrl, trimmedName))
        {
            return CatalogResult<Site>.Fail(
                CatalogFailures.Validation("Name is required; baseUrl must be an absolute HTTP(S) URL when provided."));
        }

        var site = id.HasValue ? await _store.FindAsync(id.Value, ct) : new Site { Id = Guid.NewGuid() };
        if (site is null)
        {
            return CatalogResult<Site>.Fail(NotFound());
        }

        site.Name = trimmedName;
        site.BaseUrl = baseUrl is null ? null : new Uri(baseUrl.Trim()).AbsoluteUri;
        if (!id.HasValue)
        {
            _store.Add(site);
        }

        await _store.SaveAsync(ct);
        return CatalogResult<Site>.Success(site);
    }

    private static bool ValidateData(string? baseUrl, [NotNullWhen(false)] string? trimmedName)
    {
        return string.IsNullOrWhiteSpace(trimmedName) ||
                    trimmedName.Length > 200 ||
                    (baseUrl is not null && (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || uri.AbsoluteUri.Length > 2048));
    }

    public async Task<CatalogResult<bool>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var site = await _store.FindAsync(id, ct);
        if (site is null)
        {
            return CatalogResult<bool>.Fail(NotFound());
        }

        await _store.RemoveAsync(site, ct);
        await _store.SaveAsync(ct);
        return CatalogResult<bool>.Success(true);
    }
}
