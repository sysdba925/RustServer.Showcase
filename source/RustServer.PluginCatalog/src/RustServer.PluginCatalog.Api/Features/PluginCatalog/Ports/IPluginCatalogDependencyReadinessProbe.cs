namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Ports;

public interface IPluginCatalogDatabaseReadinessProbe
{
    Task VerifyReadyAsync(CancellationToken cancellationToken);
}

public interface IPluginMediaStorageReadinessProbe
{
    Task VerifyReadyAsync(CancellationToken cancellationToken);
}
