using System.Text.RegularExpressions;
using RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Domain;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Plugins.Commands;

internal static class PluginSlug
{
    public static string FromName(string? name)
    {
        var slug = Regex.Replace(name?.Trim().ToLowerInvariant() ?? "", "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > Plugin.SlugMaxLength - 33)
        {
            slug = slug[..(Plugin.SlugMaxLength - 33)].TrimEnd('-');
        }

        return slug.Length == 0 ? "plugin" : slug;
    }

    public static string WithSuffix(string slug) => $"{slug}-{Guid.NewGuid():N}";
}
