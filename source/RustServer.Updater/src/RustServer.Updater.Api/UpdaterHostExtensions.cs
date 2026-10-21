using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Emergency;
using RustServer.Updater.Api.Health;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api;

public static class UpdaterHostExtensions
{
    public static IServiceCollection AddUpdaterServices(this IServiceCollection services, IConfiguration configuration)
    {
        AddInfrastructure(services, configuration);
        AddApplications(services);
        AddOperations(services);
        AddDeployments(services);
        AddEmergency(services);
        AddDeploymentHostedServices(services);
        AddOperationHandlers(services);

        return services;
    }

    public static WebApplication MapUpdaterEndpoints(this WebApplication app)
    {
        // systemd need not start the process in the release directory. Prefer the
        // console shipped beside the executable unless a web root was configured.
        var publishedConsole = Path.Combine(AppContext.BaseDirectory, "wwwroot", "console");
        IFileProvider consoleFiles;
        var indexPath = "index.html";
        if (string.IsNullOrEmpty(app.Configuration[WebHostDefaults.WebRootKey]) && Directory.Exists(publishedConsole))
        {
            var provider = new PhysicalFileProvider(publishedConsole);
            app.Lifetime.ApplicationStopped.Register(provider.Dispose);
            consoleFiles = provider;
            app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, RequestPath = "/console" });
        }
        else
        {
            consoleFiles = app.Environment.WebRootFileProvider;
            indexPath = "console/index.html";
        }
        app.UseStaticFiles();
        app.MapGet("/emergency", () =>
        {
            var file = consoleFiles.GetFileInfo(indexPath);
            return file.Exists
                ? Results.Stream(file.CreateReadStream(), "text/html; charset=utf-8")
                : Results.NotFound();
        });
        app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
            .WithName("GetHealth");

        return app;
    }

    private static void AddInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.Configure<ApplicationRegistryOptions>(
            configuration.GetSection(ApplicationRegistryOptions.SectionName));
        services.Configure<ReleaseChannelOptions>(
            configuration.GetSection(ReleaseChannelOptions.SectionName));
        services.Configure<OperationStoreOptions>(
            configuration.GetSection(OperationStoreOptions.SectionName));
        services.Configure<PackagePreparationOptions>(
            configuration.GetSection(PackagePreparationOptions.SectionName));
        services.Configure<DeploymentExecutorOptions>(
            configuration.GetSection(DeploymentExecutorOptions.SectionName));
        services.AddSingleton<IApplicationRegistry, FileApplicationRegistry>();
        services.AddSingleton<IReleaseChannelProvider, FileReleaseChannelProvider>();
        services.AddSingleton<IGitHubTokenProvider, SystemdGitHubTokenProvider>();
        services.AddSingleton<IInstalledVersionProvider, FileInstalledVersionProvider>();
        services.AddSingleton<ISystemdServiceStatusReader, SystemdServiceStatusReader>();
        services.AddSingleton<IApplicationStatusProvider, SystemdApplicationStatusProvider>();
        services.AddHttpClient("GitHubReleases", httpClient =>
        {
            httpClient.BaseAddress = new Uri("https://api.github.com/");
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RustServer.Updater/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("ApplicationReadiness", httpClient =>
        {
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IAvailableVersionProvider>(serviceProvider => new GitHubReleaseVersionProvider(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("GitHubReleases"),
            serviceProvider.GetRequiredService<IReleaseChannelProvider>(),
            serviceProvider.GetRequiredService<IGitHubTokenProvider>()));
    }

    private static void AddApplications(IServiceCollection services)
    {
        services.AddTransient<GetApplicationsHandler>();
        services.AddTransient<GetApplicationHandler>();
        services.AddTransient<GetApplicationRegistrationsHandler>();
        services.AddSingleton<RefreshLocalApplicationStateHandler>();
        services.AddSingleton<RefreshApplicationsHandler>();
        services.AddHostedService<ApplicationSnapshotInitializer>();
    }

    private static void AddOperations(IServiceCollection services)
    {
        services.AddSingleton<IOperationStore, FileOperationStore>();
        services.AddSingleton<IOperationManager, OperationManager>();
        services.AddHostedService<OperationRecoveryService>();
    }

    private static void AddDeployments(IServiceCollection services)
    {
        services.AddSingleton<IGitHubReleaseClient>(serviceProvider => new GitHubReleaseClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            serviceProvider.GetRequiredService<IGitHubTokenProvider>()));
        services.AddSingleton<IPackagePreparationService, PackagePreparationService>();
        services.AddSingleton<IPreparedPackageLocator, PreparedPackageLocator>();
        services.AddSingleton<IDeploymentExecutorClient, FileDeploymentExecutorClient>();
        services.AddSingleton<IDeploymentOperationReconciler, DeploymentOperationReconciler>();
        services.AddSingleton<BackgroundOperationQueue>();
        services.AddSingleton<IBackgroundOperationQueue>(serviceProvider => serviceProvider.GetRequiredService<BackgroundOperationQueue>());
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<BackgroundOperationQueue>());
        services.AddTransient<PrepareApplicationHandler>();
        services.AddTransient<DeploymentApplicationHandler>();
    }

    private static void AddEmergency(IServiceCollection services)
    {
        services.AddTransient<GetEmergencyStateHandler>();
        services.AddTransient<EmergencyUpdateApplicationHandler>();
    }

    private static void AddDeploymentHostedServices(IServiceCollection services)
    {
        services.AddHostedService<DeploymentOperationReconciliationService>();
    }

    private static void AddOperationHandlers(IServiceCollection services)
    {
        services.AddTransient<GetOperationHandler>();
        services.AddTransient<GetOperationsHandler>();
    }
}
