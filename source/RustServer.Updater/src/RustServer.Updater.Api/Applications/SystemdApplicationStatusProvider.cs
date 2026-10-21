using System.Net;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Provides type-specific application status without modifying services.
/// </summary>
public sealed class SystemdApplicationStatusProvider(
    ISystemdServiceStatusReader statusReader,
    IHttpClientFactory? httpClientFactory = null,
    IInstalledVersionProvider? installedVersionProvider = null)
    : IApplicationStatusProvider
{
    /// <inheritdoc />
    public async Task<ApplicationStatus> GetStatusAsync(
        ApplicationRegistration application,
        bool isInstalled,
        CancellationToken cancellationToken)
    {
        if (!isInstalled)
        {
            return ApplicationStatus.NotInstalled;
        }

        if (application.Type == ApplicationType.StaticWeb)
        {
            return await GetStaticWebStatusAsync(
                application,
                httpClientFactory,
                installedVersionProvider,
                cancellationToken);
        }

        if (application.Type != ApplicationType.DotNet)
        {
            return ApplicationStatus.Unknown;
        }

        try
        {
            return await statusReader.GetActivityAsync(application.SystemdServiceName, cancellationToken) switch
            {
                SystemdServiceActivity.Active => ApplicationStatus.Running,
                SystemdServiceActivity.Inactive => ApplicationStatus.Stopped,
                _ => ApplicationStatus.Unknown
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ApplicationStatus.Unknown;
        }
    }

    private static async Task<ApplicationStatus> GetStaticWebStatusAsync(
        ApplicationRegistration application,
        IHttpClientFactory? httpClientFactory,
        IInstalledVersionProvider? installedVersionProvider,
        CancellationToken cancellationToken)
    {
        var installedVersion = installedVersionProvider?.GetInstalledVersion(application);
        if (httpClientFactory is null || application.Readiness is null || installedVersion is null)
        {
            return ApplicationStatus.Unknown;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(application.Readiness.TimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Get, application.Readiness.Url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await httpClientFactory
                .CreateClient("ApplicationReadiness")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (response.StatusCode != HttpStatusCode.OK
                || !response.Headers.TryGetValues("X-RustServer-Release", out var releaseHeaders)
                || releaseHeaders.Count() != 1)
            {
                return ApplicationStatus.Unknown;
            }

            return string.Equals(releaseHeaders.Single(), installedVersion, StringComparison.Ordinal)
                ? ApplicationStatus.Running
                : ApplicationStatus.Unknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ApplicationStatus.Unknown;
        }
    }
}
