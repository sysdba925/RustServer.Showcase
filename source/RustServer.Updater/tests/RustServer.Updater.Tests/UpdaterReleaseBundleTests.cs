using System.Text.Json;
using Microsoft.Extensions.Options;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;

namespace RustServer.Updater.Tests;

public sealed class UpdaterReleaseBundleTests
{
    private const string Version = "v1.2.3-dev.4";
    private const string ApplicationName = "RustServer.Updater";

    [Fact]
    public async Task PreparedPackageLocator_RejectsSelfUpdateWithoutThePairedExecutor()
    {
        using var directory = new TemporaryDirectory();
        var stagingPath = Path.Combine(directory.Path, ApplicationName, Version);
        Directory.CreateDirectory(Path.Combine(stagingPath, "api"));
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "api", "RustServer.Updater.Api.dll"), "api");
        await WriteBundleManifestAsync(stagingPath);
        await WritePreparedManifestAsync(stagingPath);

        var locator = new PreparedPackageLocator(
            Options.Create(new PackagePreparationOptions { StoragePath = directory.Path }),
            Options.Create(new ApplicationRegistryOptions { UpdaterApplicationName = ApplicationName }));
        var application = CreateUpdaterRegistration();

        var exception = await Assert.ThrowsAsync<PreparedPackageNotFoundException>(() =>
            locator.GetAsync(application, Version, CancellationToken.None));

        Assert.Contains("paired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreparedPackageLocator_AcceptsOnlyAMatchingPairedSelfUpdateBundle()
    {
        using var directory = new TemporaryDirectory();
        var stagingPath = Path.Combine(directory.Path, ApplicationName, Version);
        Directory.CreateDirectory(Path.Combine(stagingPath, "api"));
        Directory.CreateDirectory(Path.Combine(stagingPath, "executor"));
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "api", "RustServer.Updater.Api.dll"), "api");
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "executor", "RustServer.Updater.Executor.dll"), "executor");
        await WriteBundleManifestAsync(stagingPath);
        await WritePreparedManifestAsync(stagingPath);

        var locator = new PreparedPackageLocator(
            Options.Create(new PackagePreparationOptions { StoragePath = directory.Path }),
            Options.Create(new ApplicationRegistryOptions { UpdaterApplicationName = ApplicationName }));

        var package = await locator.GetAsync(CreateUpdaterRegistration(), Version, CancellationToken.None);

        Assert.Equal(stagingPath, package.StagingPath);
    }

    private static ApplicationRegistration CreateUpdaterRegistration() => new(
        ApplicationName,
        "owner/RustServer.Updater",
        "/opt/rustserver/updater",
        "rustserver-updater.service",
        "RustServer.Updater.Api.dll");

    private static async Task WriteBundleManifestAsync(string stagingPath)
    {
        await File.WriteAllTextAsync(
            Path.Combine(stagingPath, UpdaterReleaseBundle.ManifestFileName),
            JsonSerializer.Serialize(UpdaterReleaseBundle.CreateManifest(Version)));
    }

    private static async Task WritePreparedManifestAsync(string stagingPath)
    {
        var contents = await PreparedPackageIntegrity.CreateAsync(stagingPath, CancellationToken.None);
        var manifest = new PreparedPackageManifest(
            ApplicationName,
            Version,
            new string('A', 64),
            DateTimeOffset.UtcNow,
            contents);
        await File.WriteAllTextAsync(
            Path.Combine(stagingPath, PreparedPackageIntegrity.ManifestFileName),
            JsonSerializer.Serialize(manifest));
    }
}
