using Microsoft.Extensions.Options;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;

namespace RustServer.Updater.Tests;

public sealed class FileApplicationRegistryTests
{
    [Fact]
    public void Parse_ResolvesDefaultsIntoOneRegistration()
    {
        var registration = Assert.Single(ApplicationRegistrationIniParser.Parse(ValidIni()));

        Assert.Equal("RustServer.Updater", registration.Name);
        Assert.Equal(ApplicationType.DotNet, registration.Type);
        Assert.Equal("sysdba925/RustServer.Updater", registration.Repository);
        Assert.Equal("/opt/rustserver/updater", registration.InstallationPath);
        Assert.Equal("rustserver-updater.service", registration.ServiceName);
        Assert.Equal("RustServer.Updater.Api.dll", registration.Entrypoint);
        Assert.Equal("http://127.0.0.1:8080/health", registration.Readiness!.Url);
    }

    [Fact]
    public void Parse_ResolvesAuthProductionDeploymentContract()
    {
        var registration = Assert.Single(ApplicationRegistrationIniParser.Parse("""
            [Defaults]
            RepositoryOwner=sysdba925
            InstallRoot=/opt/rustserver
            HealthScheme=http
            HealthHost=127.0.0.1
            HealthPath=/health

            [Application:RustServer.Auth]
            Repository=RustServer.Auth
            Directory=auth
            Service=rustserver-auth
            Entrypoint=RustServer.Auth.dll
            HealthPort=5286
            HealthPath=/health/ready
            """));

        Assert.Equal("RustServer.Auth", registration.Name);
        Assert.Equal("sysdba925/RustServer.Auth", registration.Repository);
        Assert.Equal("/opt/rustserver/auth", registration.InstallationPath);
        Assert.Equal("rustserver-auth.service", registration.ServiceName);
        Assert.Equal("RustServer.Auth.dll", registration.Entrypoint);
        Assert.Equal("http://127.0.0.1:5286/health/ready", registration.Readiness!.Url);
    }

    [Fact]
    public void Parse_OptInFirstInstallHook_IsStrictlyValidated()
    {
        var registration = Assert.Single(ApplicationRegistrationIniParser.Parse(
            ValidIni().Replace("HealthPort=8080",
                "HealthPort=8080\nInstallHook=provisioning/provision.sh", StringComparison.Ordinal)));
        Assert.Equal("provisioning/provision.sh", registration.InstallHook);

        Assert.Throws<ApplicationRegistryConfigurationException>(() =>
            ApplicationRegistrationIniParser.Parse(
                ValidIni().Replace("HealthPort=8080",
                    "HealthPort=8080\nInstallHook=../../etc/passwd", StringComparison.Ordinal)));

        Assert.Throws<ApplicationRegistryConfigurationException>(() =>
            ApplicationRegistrationIniParser.Parse(
                ValidStaticWebIni() + "\nInstallHook=provisioning/provision.sh\n"));
    }

    [Fact]
    public void Parse_AcceptsStaticWebAndRejectsItsServiceField()
    {
        var registration = Assert.Single(ApplicationRegistrationIniParser.Parse(ValidStaticWebIni()));

        Assert.Equal(ApplicationType.StaticWeb, registration.Type);
        Assert.Null(registration.ServiceName);
        Assert.Null(registration.Entrypoint);
        Assert.Equal("index.html", registration.EntryFile);

        var invalid = ValidStaticWebIni().Replace(
            "EntryFile=index.html",
            "EntryFile=index.html\nService=unexpected-service",
            StringComparison.Ordinal);
        Assert.Throws<ApplicationRegistryConfigurationException>(() => ApplicationRegistrationIniParser.Parse(invalid));
    }

    [Theory]
    [InlineData("InstallRoot=/opt//rustserver", "double separators")]
    [InlineData("Directory=../updater", "directory traversal")]
    [InlineData("Directory=/updater", "absolute directory")]
    [InlineData("Service=rustserver-updater.service", "repeated suffix")]
    [InlineData("HealthPort=0", "invalid port")]
    [InlineData("HealthPath=/health?unexpected=true", "query string")]
    [InlineData("HealthPath=/../health", "health traversal")]
    [InlineData("HealthHost=${HOST}", "environment expansion")]
    public void Parse_RejectsUnsafeConfiguration(string replacement, string _)
    {
        var ini = replacement.StartsWith("InstallRoot", StringComparison.Ordinal)
            ? ValidIni().Replace("InstallRoot=/opt/rustserver", replacement, StringComparison.Ordinal)
            : replacement.StartsWith("Directory", StringComparison.Ordinal)
                ? ValidIni().Replace("Directory=updater", replacement, StringComparison.Ordinal)
                : replacement.StartsWith("Service", StringComparison.Ordinal)
                    ? ValidIni().Replace("Service=rustserver-updater", replacement, StringComparison.Ordinal)
                    : replacement.StartsWith("HealthPort", StringComparison.Ordinal)
                        ? ValidIni().Replace("HealthPort=8080", replacement, StringComparison.Ordinal)
                        : replacement.StartsWith("HealthPath", StringComparison.Ordinal)
                            ? ValidIni().Replace("HealthPath=/health", replacement, StringComparison.Ordinal)
                            : ValidIni().Replace("HealthHost=127.0.0.1", replacement, StringComparison.Ordinal);

        Assert.Throws<ApplicationRegistryConfigurationException>(() => ApplicationRegistrationIniParser.Parse(ini));
    }

    [Fact]
    public void Parse_BuildsIpv6ReadinessUrlWithUriBuilder()
    {
        var registration = Assert.Single(ApplicationRegistrationIniParser.Parse(
            ValidIni().Replace("HealthHost=127.0.0.1", "HealthHost=::1", StringComparison.Ordinal)));

        Assert.Equal("http://[::1]:8080/health", registration.Readiness!.Url);
    }

    [Theory]
    [InlineData("RustServer.Updater", "updater-two", "rustserver-updater-two", "updater-two")]
    [InlineData("RustServer.Other", "updater", "rustserver-other", "other")]
    [InlineData("RustServer.Other", "other", "rustserver-updater", "other")]
    public void Parse_RejectsDuplicateNamesPathsAndUnits(string name, string directory, string service, string entrypoint)
    {
        var duplicate = $"""

            [Application:{name}]
            Repository=RustServer.Other
            Directory={directory}
            Service={service}
            Entrypoint=RustServer.{entrypoint}.Api.dll
            HealthPort=8081
            """;

        Assert.Throws<ApplicationRegistryConfigurationException>(() => ApplicationRegistrationIniParser.Parse(ValidIni() + duplicate));
    }

    [Fact]
    public void Reload_PublishesValidChangesAndRetainsLastGoodConfigurationWhenInvalid()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "applications.ini");
        File.WriteAllText(path, ValidIni());
        var registry = CreateRegistry(path);
        registry.ReplaceSnapshots([new ApplicationSnapshot("RustServer.Updater", "v1.0.0", "v1.0.1", ApplicationStatus.Running)]);

        File.WriteAllText(path, ValidIni().Replace("HealthPort=8080", "HealthPort=8081", StringComparison.Ordinal));
        registry.Reload();

        Assert.Equal("http://127.0.0.1:8081/health", registry.GetByName("RustServer.Updater")!.Readiness!.Url);
        Assert.Equal("v1.0.0", registry.GetSnapshotByName("RustServer.Updater")!.InstalledVersion);

        File.WriteAllText(path, ValidIni().Replace("HealthPort=8080", "HealthPort=70000", StringComparison.Ordinal));
        Assert.Throws<ApplicationRegistryConfigurationException>(() => registry.EnsureConfigurationIsCurrent());
        Assert.Equal("http://127.0.0.1:8081/health", registry.GetByName("RustServer.Updater")!.Readiness!.Url);
    }

    [Fact]
    public void RuntimeRegistry_LoadsWithoutAnyBootstrapOrInstallerFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "runtime", "applications.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ValidIni());

        var registry = CreateRegistry(path);

        Assert.NotNull(registry.GetByName("RustServer.Updater"));
        Assert.DoesNotContain("bootstrap", new ApplicationRegistryOptions().StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("installer", new ApplicationRegistryOptions().StoragePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_ReloadsChangedIniBeforeCollectingApplicationState()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "applications.ini");
        File.WriteAllText(path, ValidIni());
        var registry = CreateRegistry(path);
        var handler = new RefreshApplicationsHandler(
            registry,
            new RefreshLocalApplicationStateHandler(registry, new NotInstalledVersionProvider(), new NotInstalledStatusProvider()),
            new NoAvailableVersionProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RefreshApplicationsHandler>.Instance);

        File.WriteAllText(path, ValidIni().Replace("HealthPort=8080", "HealthPort=8081", StringComparison.Ordinal));
        await handler.HandleAsync(CancellationToken.None);

        Assert.Equal("http://127.0.0.1:8081/health", registry.GetByName("RustServer.Updater")!.Readiness!.Url);
    }

    [Fact]
    public async Task DeploymentAdmission_RejectsAnInvalidIniInsteadOfUsingCachedRegistration()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "applications.ini");
        File.WriteAllText(path, ValidIni());
        var registry = CreateRegistry(path);
        File.WriteAllText(path, ValidIni().Replace("HealthPort=8080", "HealthPort=70000", StringComparison.Ordinal));
        var handler = new DeploymentApplicationHandler(
            registry,
            null!,
            null!,
            null!,
            null!,
            null!,
            Options.Create(new ApplicationRegistryOptions()));

        await Assert.ThrowsAsync<ApplicationRegistryConfigurationException>(() => handler.InstallAsync(
            "RustServer.Updater",
            new DeploymentApplicationRequest("v1.0.0"),
            CancellationToken.None));
    }

    private static FileApplicationRegistry CreateRegistry(string path) => new(
        Options.Create(new ApplicationRegistryOptions { StoragePath = path }));

    internal static string ValidIni() => """
        [Defaults]
        RepositoryOwner=sysdba925
        InstallRoot=/opt/rustserver
        HealthScheme=http
        HealthHost=127.0.0.1
        HealthPath=/health

        [Application:RustServer.Updater]
        Repository=RustServer.Updater
        Directory=updater
        Service=rustserver-updater
        Entrypoint=RustServer.Updater.Api.dll
        HealthPort=8080
        """;

    private static string ValidStaticWebIni() => """
        [Defaults]
        RepositoryOwner=sysdba925
        InstallRoot=/opt/rustserver
        HealthScheme=http
        HealthHost=127.0.0.1
        HealthPath=/

        [Application:RustServer.Web]
        Type=StaticWeb
        Repository=RustServer.Web
        Directory=web
        EntryFile=index.html
        HealthPort=80
        """;

    private sealed class NotInstalledVersionProvider : IInstalledVersionProvider
    {
        public string? GetInstalledVersion(ApplicationRegistration application) => null;
    }

    private sealed class NotInstalledStatusProvider : IApplicationStatusProvider
    {
        public Task<ApplicationStatus> GetStatusAsync(
            ApplicationRegistration application,
            bool isInstalled,
            CancellationToken cancellationToken) => Task.FromResult(ApplicationStatus.NotInstalled);
    }

    private sealed class NoAvailableVersionProvider : IAvailableVersionProvider
    {
        public Task<string?> GetAvailableVersionAsync(ApplicationRegistration application, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
