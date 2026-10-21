using RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Domain;

namespace RustServer.PluginCatalog.Api.Features.PluginCatalog.Organization.Ports;

public interface IOrganizationStore
{
    Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct);
    Task<IReadOnlyList<Folder>> ListFoldersAsync(CancellationToken ct);
    Task<Tag?> FindTagAsync(Guid id, CancellationToken ct);
    Task<Folder?> FindFolderAsync(Guid id, CancellationToken ct);
    void AddTag(Tag tag);
    void AddFolder(Folder folder);
    void RemoveTag(Tag tag);
    void RemoveFolder(Folder folder);
    Task SaveAsync(CancellationToken ct);
}
