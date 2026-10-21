using RustServer.Plugins.Features.PluginManagement.Baseline;
using RustServer.Plugins.Features.PluginManagement.Commands.ApplySource;
using RustServer.Plugins.Features.PluginManagement.Commands.CommitRuntime;
using RustServer.Plugins.Features.PluginManagement.Commands.FetchGit;
using RustServer.Plugins.Features.PluginManagement.Commands.GitSync;
using RustServer.Plugins.Features.PluginManagement.Commands.RevertRuntime;
using RustServer.Plugins.Features.PluginManagement.Commands.SwitchEnvironment;
using RustServer.Plugins.Features.PluginManagement.Git;
using RustServer.Plugins.Features.PluginManagement.Guards;
using RustServer.Plugins.Features.PluginManagement.LocalGit;
using RustServer.Plugins.Features.PluginManagement.Operations;
using RustServer.Plugins.Features.PluginManagement.ReleaseDiscovery;
using RustServer.Plugins.Features.PluginManagement.RemoteGit;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using RustServer.Plugins.Features.PluginManagement.RuntimeChanges;
using RustServer.Plugins.Features.PluginManagement.SourcePreview;
using RustServer.Plugins.Features.PluginManagement.SystemState;
using RustServer.Plugins.Infrastructure.EnvironmentState;
using LocalGitAdapter = RustServer.Plugins.Features.PluginManagement.LocalGit.LocalGit;
using RemoteGitAdapter = RustServer.Plugins.Features.PluginManagement.RemoteGit.RemoteGit;

namespace RustServer.Plugins.Features.PluginManagement;

public static class Registration
{
    public static IServiceCollection AddPluginManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services
            .AddOptions(configuration)
            .AddEnvironment()
            .AddGit()
            .AddBaseline()
            .AddRuntime()
            .AddOrchestrationSupport()
            .AddReadModels()
            .AddCommands();
    }

    private static IServiceCollection AddOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<PluginManagementStorageOptions>()
            .Bind(configuration.GetSection(PluginManagementStorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.StateRootPath),
                $"{PluginManagementStorageOptions.SectionName}:StateRootPath must contain a value.")
            .ValidateOnStart();

        services
            .AddOptions<LocalGitOptions>()
            .Bind(configuration.GetSection(LocalGitOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.Plugins.WorkspacePath) &&
                    !string.IsNullOrWhiteSpace(options.Configs.WorkspacePath),
                "PluginManagement:Repositories workspace paths must contain values.")
            .ValidateOnStart();

        services
            .AddOptions<GitWorkspaceInitializationOptions>()
            .Bind(configuration.GetSection(GitWorkspaceInitializationOptions.SectionName));

        services
            .AddOptions<GitCommitAuthorOptions>()
            .Bind(configuration.GetSection(GitCommitAuthorOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.Name) &&
                    !string.IsNullOrWhiteSpace(options.Email),
                $"{GitCommitAuthorOptions.SectionName}:Name and {GitCommitAuthorOptions.SectionName}:Email must contain values.")
            .ValidateOnStart();

        services
            .AddOptions<RuntimeOptions>()
            .Bind(configuration.GetSection(RuntimeOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ServerRootPath),
                $"{RuntimeOptions.SectionName}:ServerRootPath must contain a value.")
            .ValidateOnStart();

        services
            .AddOptions<OperationHistoryOptions>()
            .Bind(configuration.GetSection(OperationHistoryOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.DatabasePath))
                {
                    options.DatabasePath = Path.Combine(
                        configuration["PluginManagement:StateRootPath"]!,
                        "operations.db");
                }
            })
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DatabasePath),
                $"{OperationHistoryOptions.SectionName}:DatabasePath must contain a value.")
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddEnvironment(this IServiceCollection services)
    {
        services.AddSingleton<IEnvironmentStore, FileEnvironmentStore>();

        return services;
    }

    private static IServiceCollection AddGit(this IServiceCollection services)
    {
        services.AddSingleton<IGitHubTokenProvider, SystemdGitHubTokenProvider>();
        services.AddSingleton<GitProcess>();
        services.AddSingleton<ILocalGit, LocalGitAdapter>();
        services.AddSingleton<IRemoteGit, RemoteGitAdapter>();
        services.AddSingleton<LastFetchState>();
        services.AddSingleton<GitStatusReader>();
        services.AddSingleton<GitWorkspaceInitializer>();
        services.AddHostedService<GitWorkspaceInitializationService>();

        return services;
    }

    private static IServiceCollection AddBaseline(this IServiceCollection services)
    {
        services.AddSingleton<IBaselineStore, FileBaselineStore>();

        return services;
    }

    private static IServiceCollection AddRuntime(this IServiceCollection services)
    {
        services.AddSingleton<ManagedFileMapping>();
        services.AddSingleton<IRuntimeFiles, RuntimeFiles>();
        services.AddSingleton<ManagedRuntimeFileReader>();
        services.AddSingleton<RuntimeChangeNotifier>();
        services.AddHostedService<RuntimeChangeWatcher>();

        return services;
    }

    private static IServiceCollection AddOrchestrationSupport(this IServiceCollection services)
    {
        services.AddSingleton<IExclusiveMutationGuard, ExclusiveMutationGuard>();

        // Operations owns persistent reports and their restart recovery.
        services.AddSingleton<IOperationHistoryStore, SqliteOperationHistoryStore>();
        services.AddSingleton<IOperationReports, PersistentOperationReports>();
        services.AddHostedService<OperationHistoryStartupService>();

        return services;
    }

    private static IServiceCollection AddReadModels(this IServiceCollection services)
    {
        services.AddSingleton<ReleaseDiscoveryReader>();
        services.AddSingleton<RuntimeDiffReader>();
        services.AddSingleton<RuntimeDiffContentReader>();
        services.AddSingleton<SourcePreviewReader>();
        services.AddSingleton<SystemStateReader>();

        return services;
    }

    private static IServiceCollection AddCommands(this IServiceCollection services)
    {
        services.AddTransient<SwitchEnvironmentHandler>();
        services.AddTransient<FetchGitHandler>();
        services.AddTransient<GitSyncHandler>();
        services.AddTransient<ApplySourceHandler>();
        services.AddTransient<CommitRuntimeHandler>();
        services.AddTransient<RevertRuntimeHandler>();

        return services;
    }
}
