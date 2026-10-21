using RustServer.Plugins.Features.PluginManagement.Models;
using DomainSource = RustServer.Plugins.Features.PluginManagement.Models.Source;

namespace RustServer.Plugins.Features.PluginManagement.Controllers;

public sealed record TargetSourceRequest(string? Source, string? Release)
{
    public bool TryToDomain(out DomainSource? source)
    {
        try
        {
            switch (Source)
            {
                case "development" when Release is null:
                    source = DomainSource.Development;
                    return true;
                case "release" when !string.IsNullOrWhiteSpace(Release):
                    source = DomainSource.ForRelease(new Release(Release));
                    return true;
                default:
                    source = null;
                    return false;
            }
        }
        catch (ArgumentException)
        {
            source = null;
            return false;
        }
    }
}
