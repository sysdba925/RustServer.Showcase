using Microsoft.Extensions.Options;

namespace RustServer.Versions.Api.Versions;

public sealed class OxideVersionProvider(
    IOptions<OxideVersionsOptions> options,
    IOxideInstalledVersionReader installedVersionReader,
    IOxideRunningVersionReader runningVersionReader,
    IOxideAvailableVersionReader availableVersionReader,
    ILogger<OxideVersionProvider> logger) : IOxideVersionProvider
{
    public async Task<OxideVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken)
    {
        var installedVersion = ReadInstalledVersion(options.Value.AssemblyPath);
        var runningVersion = await ReadRunningVersionAsync(cancellationToken);
        var availableVersion = await ReadAvailableVersionAsync(cancellationToken);

        return new OxideVersionInfo(
            installedVersion?.ToString(),
            runningVersion?.ToString(),
            availableVersion.ToString(),
            installedVersion is null || installedVersion.CompareTo(availableVersion) != 0);
    }

    private OxideVersion? ReadInstalledVersion(string assemblyPath)
    {
        try
        {
            return installedVersionReader.ReadVersion(assemblyPath);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read the installed Oxide version from assembly metadata.");
            throw;
        }
    }

    private async Task<OxideVersion?> ReadRunningVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await runningVersionReader.ReadVersionAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read the running Oxide version through the RCON service.");
            throw;
        }
    }

    private async Task<OxideVersion> ReadAvailableVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await availableVersionReader.ReadVersionAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read the available Oxide version from GitHub Releases.");
            throw;
        }
    }
}
