namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Provides the newest release version available for a registered application.
/// </summary>
public interface IAvailableVersionProvider
{
    /// <summary>
    /// Gets the newest version in the application's configured release channel.
    /// </summary>
    /// <param name="application">The application whose releases should be inspected.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The full GitHub release tag, or <see langword="null"/> when the channel has no releases.</returns>
    Task<string?> GetAvailableVersionAsync(ApplicationRegistration application, CancellationToken cancellationToken);

    /// <summary>
    /// Gets every release version available in the application's configured release channel, newest first.
    /// </summary>
    /// <param name="application">The application whose releases should be inspected.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Full GitHub release tags ordered from newest to oldest.</returns>
    async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        ApplicationRegistration application,
        CancellationToken cancellationToken)
    {
        var version = await GetAvailableVersionAsync(application, cancellationToken);
        return version is null ? [] : [version];
    }
}
