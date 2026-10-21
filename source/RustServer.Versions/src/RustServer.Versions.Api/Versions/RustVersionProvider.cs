using Microsoft.Extensions.Options;

namespace RustServer.Versions.Api.Versions;

public sealed class RustVersionProvider(
    IOptions<RustVersionsOptions> options,
    IInstalledBuildIdReader installedBuildIdReader,
    ISteamAvailableBuildIdReader availableBuildIdReader,
    IRustRunningVersionReader runningVersionReader,
    ILogger<RustVersionProvider> logger) : IRustVersionProvider
{
    public async Task<RustVersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken)
    {
        var branch = GetBranch(options.Value);
        var installedBuildId = ReadInstalledBuildId(options.Value.AppManifestPath);
        var runningVersion = await ReadRunningVersionAsync(cancellationToken);
        var availableBuildId = await ReadAvailableBuildIdAsync(branch, cancellationToken);

        return new RustVersionInfo(
            branch,
            installedBuildId,
            runningVersion,
            availableBuildId,
            installedBuildId != availableBuildId);
    }

    private ulong? ReadInstalledBuildId(string appManifestPath)
    {
        try
        {
            return installedBuildIdReader.ReadBuildId(appManifestPath);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read the installed Rust BuildID from the Steam app manifest.");
            throw;
        }
    }

    private async Task<ulong> ReadAvailableBuildIdAsync(string branch, CancellationToken cancellationToken)
    {
        try
        {
            return await availableBuildIdReader.ReadBuildIdAsync(branch, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read the available Rust BuildID from Steam PICS metadata.");
            throw;
        }
    }

    private async Task<RustRunningVersion?> ReadRunningVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await runningVersionReader.ReadVersionAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read the running Rust version through the RCON service.");
            throw;
        }
    }

    private static string GetBranch(RustVersionsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Branch))
        {
            throw new InvalidOperationException("RustVersions:Branch must be configured.");
        }

        return options.Branch.Trim();
    }
}
