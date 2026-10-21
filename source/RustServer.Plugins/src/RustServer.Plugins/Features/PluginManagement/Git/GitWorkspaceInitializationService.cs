using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement.Git;

public sealed class GitWorkspaceInitializationOptions
{
    public const string SectionName = "PluginManagement:WorkspaceInitialization";
    public bool Enabled { get; set; }
}

/// <summary>
/// Initializes missing Git checkouts after host startup without blocking local status APIs.
/// Errors remain visible through missing-workspace Git diagnostics and a non-secret log entry.
/// </summary>
public sealed class GitWorkspaceInitializationService(
    IOptions<GitWorkspaceInitializationOptions> options,
    GitWorkspaceInitializer initializer,
    IExclusiveMutationGuard mutationGuard,
    ILogger<GitWorkspaceInitializationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        await Task.Yield();
        try
        {
            var lease = mutationGuard.TryAcquire();
            if (lease is null)
            {
                logger.LogWarning("Plugins workspace initialization skipped because another Git operation is active.");
                return;
            }

            await using (lease)
            {
                await initializer.InitializeAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        catch (GitWorkspaceInitializationException exception)
        {
            // The initializer emits fixed non-secret failure descriptions, not Git stderr.
            logger.LogError("Plugins Git workspace initialization failed: {Reason}", exception.Message);
        }
        catch (Exception)
        {
            // Don't print arbitrary exception details: they may contain paths or auth context.
            logger.LogError("Plugins Git workspace initialization failed; check Git status and credential provisioning.");
        }
    }
}
