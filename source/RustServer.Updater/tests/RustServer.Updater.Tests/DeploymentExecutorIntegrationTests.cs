using System.Text.Json;
using System.Text.Json.Serialization;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;
using RustServer.Updater.Executor;

namespace RustServer.Updater.Tests;

public sealed class DeploymentExecutorIntegrationTests
{
    private const string UpdaterName = "RustServer.Updater";
    private const string PreviousVersion = "v1.0.0";
    private const string TargetVersion = "v2.0.0";
    private static readonly string[] SystemdUnits =
    [
        "rustserver-updater.service",
        "rustserver-updater-executor.socket",
        "rustserver-updater-executor@.service",
        "rustserver-updater-recovery.service"
    ];

    [Fact]
    public async Task Prepare_SecuresVerifiedPackageWithoutActivation()
    {
        using var directory = new TemporaryDirectory();
        var installationPath = Path.Combine(directory.Path, "catalog");
        var stagingPath = Path.Combine(directory.Path, "staging", "RustServer.PluginCatalog", TargetVersion);
        Directory.CreateDirectory(Path.Combine(stagingPath, "provisioning"));
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "RustServer.PluginCatalog.Api.dll"), "catalog");
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "provisioning", "provision.sh"), "#!/usr/bin/env bash\n");
        await WritePreparedManifestAsync(stagingPath, "RustServer.PluginCatalog", TargetVersion);
        var links = new InMemoryReleaseLinkManager(null);
        var systemd = new LockAwareSystemdController(Path.Combine(directory.Path, "deployment.lock"), links);
        var executor = new DeploymentExecutor(
            CreateOptions(directory.Path),
            systemd,
            new AlwaysFailReadinessVerifier(),
            new SingleApplicationRegistry(new ApplicationRegistration(
                "RustServer.PluginCatalog",
                "owner/RustServer.PluginCatalog",
                installationPath,
                "rustserver-plugin-catalog.service",
                "RustServer.PluginCatalog.Api.dll",
                new ApplicationReadinessProbe("http://127.0.0.1:1/health/ready", Attempts: 1, TimeoutSeconds: 1))),
            links);

        var result = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), "RustServer.PluginCatalog", OperationType.Prepare, TargetVersion),
            CancellationToken.None);

        Assert.Equal(DeploymentTransactionStatus.Succeeded, result.Status);
        Assert.Equal("Verified package secured", result.Step);
        Assert.True(File.Exists(Path.Combine(installationPath, "prepared", TargetVersion, "provisioning", "provision.sh")));
        Assert.False(Directory.Exists(Path.Combine(installationPath, "releases", TargetVersion)));
        Assert.Null(links.CurrentVersion);
        Assert.Empty(systemd.Events);
    }

    [Fact]
    public async Task Recovery_InterruptedCatalogProvisioning_MarksFailureWithoutStoppingMissingService()
    {
        using var directory = new TemporaryDirectory();
        var application = new ApplicationRegistration(
            "RustServer.PluginCatalog", "owner/RustServer.PluginCatalog",
            Path.Combine(directory.Path, "plugin-catalog"),
            "rustserver-plugin-catalog.service", "RustServer.PluginCatalog.Api.dll",
            new ApplicationReadinessProbe("http://127.0.0.1:1/health/ready"),
            installHook: "provisioning/provision.sh");
        var links = new InMemoryReleaseLinkManager(null);
        var systemd = await SystemdCommandRecorder.CreateAsync(directory.Path);
        var executor = new DeploymentExecutor(
            CreateOptions(directory.Path), systemd.CreateController(),
            new AlwaysFailReadinessVerifier(), new SingleApplicationRegistry(application), links);

        var operationId = Guid.NewGuid();
        var interrupted = new DeploymentTransaction(
            operationId, application.Name, DeploymentTransactionStatus.Activating,
            "Provisioning first-install dependencies", null, TargetVersion, null,
            null, DateTimeOffset.UtcNow);
        var directoryPath = Path.Combine(directory.Path, "transactions");
        Directory.CreateDirectory(directoryPath);
        var transactionPath = Path.Combine(directoryPath, $"{operationId:N}.json");
        await File.WriteAllTextAsync(transactionPath, JsonSerializer.Serialize(interrupted));

        await executor.RecoverAsync(CancellationToken.None);

        var recovered = JsonSerializer.Deserialize<DeploymentTransaction>(
            await File.ReadAllTextAsync(transactionPath),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });
        Assert.NotNull(recovered);
        Assert.Equal(DeploymentTransactionStatus.Failed, recovered.Status);
        Assert.Equal("Recovery: pre-activation interrupted", recovered.Step);
        Assert.Null(links.CurrentVersion);
        Assert.Empty(await systemd.ReadCommandsAsync());
    }

    [Fact]
    public async Task FirstInstall_RunsProtectedCatalogHook_AndReusesMatchingImmutableRelease()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var directory = new TemporaryDirectory();
        var installationPath = Path.Combine(directory.Path, "plugin-catalog");
        var stagingPath = Path.Combine(directory.Path, "staging", "RustServer.PluginCatalog", TargetVersion);
        var marker = Path.Combine(directory.Path, "hook-runs.log");
        Directory.CreateDirectory(Path.Combine(stagingPath, "provisioning"));
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "RustServer.PluginCatalog.Api.dll"), "catalog");
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "provisioning", "provision.sh"),
            "#!/usr/bin/env bash\nprintf 'run\\n' >> '" + marker + "'\n");
        await WritePreparedManifestAsync(stagingPath, "RustServer.PluginCatalog", TargetVersion);

        var application = new ApplicationRegistration(
            "RustServer.PluginCatalog", "owner/RustServer.PluginCatalog", installationPath,
            "rustserver-plugin-catalog.service", "RustServer.PluginCatalog.Api.dll",
            new ApplicationReadinessProbe("http://127.0.0.1:1/health/ready"),
            installHook: "provisioning/provision.sh");
        var recorder = await SystemdCommandRecorder.CreateAsync(directory.Path);
        var initialLinks = new InMemoryReleaseLinkManager(null);
        var executor = new DeploymentExecutor(CreateOptions(directory.Path), recorder.CreateController(),
            new ProcessBackedReadinessVerifier(Path.Combine(directory.Path, "deployment.lock"),
                recorder, "start rustserver-plugin-catalog.service"),
            new SingleApplicationRegistry(application), initialLinks);

        var prepare = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), application.Name, OperationType.Prepare, TargetVersion),
            CancellationToken.None);
        Assert.Equal(DeploymentTransactionStatus.Succeeded, prepare.Status);

        var first = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), application.Name, OperationType.Install, TargetVersion),
            CancellationToken.None);
        Assert.Equal(DeploymentTransactionStatus.Succeeded, first.Status);
        Assert.Equal(TargetVersion, initialLinks.CurrentVersion);

        var immutableRelease = Path.Combine(installationPath, "releases", TargetVersion);
        var initialManifest = await File.ReadAllBytesAsync(
            Path.Combine(immutableRelease, PreparedPackageIntegrity.ManifestFileName));

        // Emulate the missing current link after an interrupted first installation.
        var retryLinks = new InMemoryReleaseLinkManager(null);
        var retry = new DeploymentExecutor(CreateOptions(directory.Path), recorder.CreateController(),
            new ProcessBackedReadinessVerifier(Path.Combine(directory.Path, "deployment.lock"),
                recorder, "start rustserver-plugin-catalog.service"),
            new SingleApplicationRegistry(application), retryLinks);
        var second = await retry.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), application.Name, OperationType.Install, TargetVersion),
            CancellationToken.None);

        Assert.Equal(DeploymentTransactionStatus.Succeeded, second.Status);
        Assert.Equal(2, (await File.ReadAllLinesAsync(marker)).Length);
        Assert.Equal(initialManifest, await File.ReadAllBytesAsync(
            Path.Combine(immutableRelease, PreparedPackageIntegrity.ManifestFileName)));
    }

    [Fact]
    public async Task FirstInstall_FailurePersistsSafeHookDiagnostic()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TemporaryDirectory();
        var installationPath = Path.Combine(directory.Path, "plugin-catalog");
        var stagingPath = Path.Combine(directory.Path, "staging", "RustServer.PluginCatalog", TargetVersion);
        Directory.CreateDirectory(Path.Combine(stagingPath, "provisioning"));
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "RustServer.PluginCatalog.Api.dll"), "catalog");
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "provisioning", "provision.sh"),
            "#!/bin/bash\nprintf '%s' '{\"version\":1,\"stage\":\"database\",\"category\":\"external_command\",\"reason\":\"postgresql_command_failed\"}' > \"$RUSTSERVER_PROVISIONING_DIAGNOSTIC\"\nprintf 'secret' >&2\nexit 3\n");
        await WritePreparedManifestAsync(stagingPath, "RustServer.PluginCatalog", TargetVersion);
        var application = new ApplicationRegistration("RustServer.PluginCatalog", "owner/RustServer.PluginCatalog", installationPath,
            "rustserver-plugin-catalog.service", "RustServer.PluginCatalog.Api.dll",
            new ApplicationReadinessProbe("http://127.0.0.1:1/health/ready"), installHook: "provisioning/provision.sh");
        var links = new InMemoryReleaseLinkManager(null);
        var recorder = await SystemdCommandRecorder.CreateAsync(directory.Path);
        var executor = new DeploymentExecutor(CreateOptions(directory.Path), recorder.CreateController(),
            new AlwaysFailReadinessVerifier(), new SingleApplicationRegistry(application), links);
        Assert.Equal(DeploymentTransactionStatus.Succeeded, (await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), application.Name, OperationType.Prepare, TargetVersion), CancellationToken.None)).Status);
        var id = Guid.NewGuid();
        var result = await executor.ExecuteAsync(new DeploymentExecutorRequest(id, application.Name, OperationType.Install, TargetVersion), CancellationToken.None);
        Assert.Equal(DeploymentTransactionStatus.Failed, result.Status);
        Assert.Contains("stage database; category external_command; reason postgresql_command_failed; exit code 3", result.Error);
        Assert.DoesNotContain("secret", result.Error);
        Assert.Contains(result.Error!, await File.ReadAllTextAsync(Path.Combine(directory.Path, "transactions", $"{id:N}.json")));
        Assert.Null(links.CurrentVersion);
        Assert.Empty(await recorder.ReadCommandsAsync());
    }

    [Fact]
    public async Task FirstInstall_MissingProvisioningScript_FailsBeforeSystemdAndCanRetry()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var directory = new TemporaryDirectory();
        var installationPath = Path.Combine(directory.Path, "plugin-catalog");
        var stagingPath = Path.Combine(directory.Path, "staging", "RustServer.PluginCatalog", TargetVersion);
        Directory.CreateDirectory(stagingPath);
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "RustServer.PluginCatalog.Api.dll"), "old");
        await WritePreparedManifestAsync(stagingPath, "RustServer.PluginCatalog", TargetVersion);

        var application = new ApplicationRegistration(
            "RustServer.PluginCatalog", "owner/RustServer.PluginCatalog", installationPath,
            "rustserver-plugin-catalog.service", "RustServer.PluginCatalog.Api.dll",
            new ApplicationReadinessProbe("http://127.0.0.1:1/health/ready"),
            installHook: "provisioning/provision.sh");
        var links = new InMemoryReleaseLinkManager(null);
        var recorder = await SystemdCommandRecorder.CreateAsync(directory.Path);
        var executor = new DeploymentExecutor(CreateOptions(directory.Path), recorder.CreateController(),
            new AlwaysFailReadinessVerifier(), new SingleApplicationRegistry(application), links);
        var prepared = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), application.Name, OperationType.Prepare, TargetVersion),
            CancellationToken.None);
        Assert.Equal(DeploymentTransactionStatus.Succeeded, prepared.Status);

        foreach (var attempt in Enumerable.Range(0, 2))
        {
            var result = await executor.ExecuteAsync(
                new DeploymentExecutorRequest(Guid.NewGuid(), application.Name, OperationType.Install, TargetVersion),
                CancellationToken.None);
            Assert.Equal(DeploymentTransactionStatus.Failed, result.Status);
            Assert.Equal("Pre-activation failed", result.Step);
            Assert.Contains("does not contain its required first-install", result.Error);
            Assert.Null(links.CurrentVersion);
        }

        Assert.Empty(await recorder.ReadCommandsAsync());
        Assert.True(Directory.Exists(Path.Combine(installationPath, "releases", TargetVersion)));
    }

    [Fact]
    public async Task SelfUpdate_StartsAndPassesReadinessWhileExecutorHoldsDeploymentLock()
    {
        using var environment = await UpdaterEnvironment.CreateAsync();
        var systemd = await SystemdCommandRecorder.CreateAsync(environment.RootPath);
        var readiness = new ProcessBackedReadinessVerifier(
            environment.LockPath,
            systemd,
            "start --ignore-dependencies rustserver-updater.service");
        var executor = environment.CreateExecutor(systemd.CreateController(), readiness);

        var transaction = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), UpdaterName, OperationType.SelfUpdate, TargetVersion),
            CancellationToken.None);

        Assert.True(transaction.Status == DeploymentTransactionStatus.Succeeded, $"{transaction.Step}: {transaction.Error}");
        Assert.Equal(TargetVersion, transaction.ActiveVersion);
        Assert.True(readiness.ObservedDeploymentLock);
        Assert.Equal(TargetVersion, readiness.VerifiedVersions.Single());
        Assert.Equal(TargetVersion, environment.Links.CurrentVersion);
        Assert.Contains("stop rustserver-updater.service", await systemd.ReadCommandsAsync());
        Assert.Contains("daemon-reload", await systemd.ReadCommandsAsync());
        Assert.Contains("start --ignore-dependencies rustserver-updater.service", await systemd.ReadCommandsAsync());
        Assert.DoesNotContain("enable rustserver-updater.service", await systemd.ReadCommandsAsync());
    }

    [Fact]
    public async Task SelfUpdate_ReadinessFailureStopsNewUpdaterThenRestoresPreviousBundleAndUnits()
    {
        using var environment = await UpdaterEnvironment.CreateAsync();
        var systemd = new LockAwareSystemdController(environment.LockPath, environment.Links);
        var readiness = new TrackingReadinessVerifier(systemd, failVersion: TargetVersion);
        var executor = environment.CreateExecutor(systemd, readiness);

        var transaction = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), UpdaterName, OperationType.SelfUpdate, TargetVersion),
            CancellationToken.None);

        Assert.True(transaction.Status == DeploymentTransactionStatus.RolledBack, $"{transaction.Step}: {transaction.Error}");
        Assert.Equal(PreviousVersion, transaction.ActiveVersion);
        Assert.Equal(PreviousVersion, environment.Links.CurrentVersion);
        Assert.Contains(systemd.Stops, stop => stop.Version == TargetVersion);
        Assert.Contains(systemd.Starts, start => start.Version == PreviousVersion
            && start.Mode == SystemdStartMode.SelfUpdateOrRecovery);
        Assert.Equal([TargetVersion, PreviousVersion], readiness.VerifiedVersions);
        foreach (var unit in SystemdUnits)
        {
            Assert.Equal($"{PreviousVersion}:{unit}", await File.ReadAllTextAsync(Path.Combine(environment.SystemdPath, unit)));
        }

        var newUpdaterStop = systemd.Events.FindIndex(@event => @event == $"stop:{TargetVersion}");
        var previousUnitRestore = systemd.Events.FindLastIndex(@event => @event == "reload");
        Assert.True(newUpdaterStop >= 0);
        Assert.True(previousUnitRestore > newUpdaterStop);
    }

    [Fact]
    public async Task Recovery_StopsInterruptedNewUpdaterAndRestoresPreviousBundleBeforeReadiness()
    {
        using var environment = await UpdaterEnvironment.CreateAsync(
            currentVersion: TargetVersion,
            installedUnitVersion: TargetVersion,
            targetReleaseInstalled: true);
        var systemd = new LockAwareSystemdController(environment.LockPath, environment.Links);
        var readiness = new TrackingReadinessVerifier(systemd);
        var executor = environment.CreateExecutor(systemd, readiness);
        var operationId = Guid.NewGuid();
        var interrupted = new DeploymentTransaction(
            operationId,
            UpdaterName,
            DeploymentTransactionStatus.Activating,
            "Activating paired Updater and Executor bundle",
            PreviousVersion,
            TargetVersion,
            PreviousVersion,
            null,
            DateTimeOffset.UtcNow);
        Directory.CreateDirectory(environment.TransactionsPath);
        await File.WriteAllTextAsync(
            Path.Combine(environment.TransactionsPath, $"{operationId:N}.json"),
            JsonSerializer.Serialize(interrupted));

        await executor.RecoverAsync(CancellationToken.None);

        var recovered = JsonSerializer.Deserialize<DeploymentTransaction>(
            await File.ReadAllTextAsync(Path.Combine(environment.TransactionsPath, $"{operationId:N}.json")),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });
        Assert.NotNull(recovered);
        Assert.Equal(DeploymentTransactionStatus.RolledBack, recovered.Status);
        Assert.Equal(PreviousVersion, environment.Links.CurrentVersion);
        Assert.Contains(systemd.Stops, stop => stop.Version == TargetVersion);
        Assert.Contains(systemd.Starts, start => start.Version == PreviousVersion
            && start.Mode == SystemdStartMode.SelfUpdateOrRecovery);
        Assert.Equal([PreviousVersion], readiness.VerifiedVersions);
        foreach (var unit in SystemdUnits)
        {
            Assert.Equal($"{PreviousVersion}:{unit}", await File.ReadAllTextAsync(Path.Combine(environment.SystemdPath, unit)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryDeployment_EnablesServiceOnFirstInstallAndUpdate(bool hasPreviousVersion)
    {
        using var directory = new TemporaryDirectory();
        var installationPath = Path.Combine(directory.Path, "example");
        var previousRelease = Path.Combine(installationPath, "releases", PreviousVersion);
        if (hasPreviousVersion)
        {
            Directory.CreateDirectory(previousRelease);
        }
        var stagingPath = Path.Combine(directory.Path, "staging", "Example.Api", TargetVersion);
        Directory.CreateDirectory(stagingPath);
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "Example.Api.dll"), "example");
        await WritePreparedManifestAsync(stagingPath, "Example.Api", TargetVersion);
        var application = new ApplicationRegistration(
            "Example.Api",
            "owner/Example.Api",
            installationPath,
            "example-api.service",
            "Example.Api.dll",
            new ApplicationReadinessProbe("http://127.0.0.1:1/health", Attempts: 1, TimeoutSeconds: 1));
        var links = new InMemoryReleaseLinkManager(hasPreviousVersion ? PreviousVersion : null);
        var systemd = await SystemdCommandRecorder.CreateAsync(directory.Path);
        var readiness = new ProcessBackedReadinessVerifier(
            Path.Combine(directory.Path, "deployment.lock"),
            systemd,
            "start example-api.service");
        var executor = new DeploymentExecutor(
            CreateOptions(directory.Path),
            systemd.CreateController(),
            readiness,
            new SingleApplicationRegistry(application),
            links);

        var transaction = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(
                Guid.NewGuid(),
                application.Name,
                hasPreviousVersion ? OperationType.Update : OperationType.Install,
                TargetVersion),
            CancellationToken.None);

        Assert.Equal(DeploymentTransactionStatus.Succeeded, transaction.Status);
        Assert.True(readiness.ObservedDeploymentLock);
        var commands = await systemd.ReadCommandsAsync();
        Assert.Contains("start example-api.service", commands);
        Assert.Contains("enable example-api.service", commands);
        Assert.True(
            Array.IndexOf(commands.ToArray(), "enable example-api.service") >
            Array.IndexOf(commands.ToArray(), "start example-api.service"));
        Assert.DoesNotContain("start --ignore-dependencies example-api.service", commands);
    }

    [Fact]
    public async Task FirstControlInstall_RegistersAndEnablesHelperBeforeControlIsReady()
    {
        using var directory = new TemporaryDirectory();
        var installationPath = Path.Combine(directory.Path, "control");
        await StageControlReleaseAsync(directory.Path);
        var links = new InMemoryReleaseLinkManager(null);
        var systemd = await SystemdCommandRecorder.CreateAsync(directory.Path);
        var executor = new DeploymentExecutor(
            CreateOptions(directory.Path),
            systemd.CreateController(),
            new ProcessBackedReadinessVerifier(
                Path.Combine(directory.Path, "deployment.lock"), systemd,
                "start rustserver-control.service"),
            new SingleApplicationRegistry(ControlApplication(installationPath)),
            links);

        var result = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), "RustServer.Control", OperationType.Install, TargetVersion),
            CancellationToken.None);

        Assert.Equal(DeploymentTransactionStatus.Succeeded, result.Status);
        Assert.Equal(TargetVersion, links.CurrentVersion);
        var unitPath = Path.Combine(directory.Path, "systemd", ControlNetworkAccessUnitInstaller.UnitName);
        Assert.Equal(
            Path.Combine(installationPath, "current", "network-access", "systemd", ControlNetworkAccessUnitInstaller.UnitName),
            new FileInfo(unitPath).LinkTarget);
        var dockerDropIn = Path.Combine(directory.Path, "systemd", "docker.service.d", "rustserver-network-access.conf");
        Assert.True(File.Exists(dockerDropIn));
        Assert.Contains("ExecStartPre=/usr/sbin/nft -f /opt/rustserver/control/current/network-access/nftables/rustserver-network-access-guard.nft", await File.ReadAllTextAsync(dockerDropIn));

        var commands = (await systemd.ReadCommandsAsync()).ToArray();
        Assert.True(Array.IndexOf(commands, "daemon-reload") < Array.IndexOf(commands, "start rustserver-control.service"));
        Assert.Contains("enable " + ControlNetworkAccessUnitInstaller.UnitName, commands);
        Assert.True(Array.IndexOf(commands, "enable " + ControlNetworkAccessUnitInstaller.UnitName) >
                    Array.IndexOf(commands, "start rustserver-control.service"));
        Assert.Contains("enable rustserver-control.service", commands);
        Assert.DoesNotContain("start --ignore-dependencies rustserver-control.service", commands);
    }

    [Fact]
    public async Task FailedFirstControlInstall_CleansUpPrivilegedHelperRegistration()
    {
        using var directory = new TemporaryDirectory();
        var installationPath = Path.Combine(directory.Path, "control");
        await StageControlReleaseAsync(directory.Path);
        var links = new InMemoryReleaseLinkManager(null);
        var systemd = await SystemdCommandRecorder.CreateAsync(directory.Path);
        var executor = new DeploymentExecutor(
            CreateOptions(directory.Path), systemd.CreateController(),
            new AlwaysFailReadinessVerifier(),
            new SingleApplicationRegistry(ControlApplication(installationPath)), links);

        var result = await executor.ExecuteAsync(
            new DeploymentExecutorRequest(Guid.NewGuid(), "RustServer.Control", OperationType.Install, TargetVersion),
            CancellationToken.None);

        Assert.Equal(DeploymentTransactionStatus.RolledBack, result.Status);
        Assert.Null(links.CurrentVersion);
        Assert.Null(new FileInfo(Path.Combine(directory.Path, "systemd", ControlNetworkAccessUnitInstaller.UnitName)).LinkTarget);
        Assert.False(File.Exists(Path.Combine(directory.Path, "systemd", "docker.service.d", "rustserver-network-access.conf")));
        var commands = (await systemd.ReadCommandsAsync()).ToArray();
        Assert.Contains("stop " + ControlNetworkAccessUnitInstaller.UnitName, commands);
        Assert.Contains("disable " + ControlNetworkAccessUnitInstaller.UnitName, commands);
        Assert.True(commands.Count(c => c == "daemon-reload") >= 2);
        Assert.DoesNotContain("enable rustserver-control.service", commands);
    }

    private static ApplicationRegistration ControlApplication(string installationPath) => new(
        "RustServer.Control",
        "owner/RustServer.Control",
        installationPath,
        "rustserver-control.service",
        "RustServer.Control.Api.dll",
        new ApplicationReadinessProbe("http://127.0.0.1:1/health/ready", Attempts: 1, TimeoutSeconds: 1));

    private static async Task StageControlReleaseAsync(string rootPath)
    {
        var directory = Path.Combine(rootPath, "staging", "RustServer.Control", TargetVersion);
        var unitPath = Path.Combine(directory, "network-access", "systemd", ControlNetworkAccessUnitInstaller.UnitName);
        var guardPath = Path.Combine(directory, "network-access", "nftables", "rustserver-network-access-guard.nft");
        Directory.CreateDirectory(Path.GetDirectoryName(unitPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(guardPath)!);
        await File.WriteAllTextAsync(Path.Combine(directory, "RustServer.Control.Api.dll"), "control");
        await File.WriteAllTextAsync(unitPath, "[Service]\nUser=root\n");
        await File.WriteAllTextAsync(guardPath, "add table inet rsc_network_access\n");
        await WritePreparedManifestAsync(directory, "RustServer.Control", TargetVersion);
    }

    private sealed class AlwaysFailReadinessVerifier : IApplicationReadinessVerifier
    {
        public Task VerifyAsync(ApplicationRegistration application, string expectedReleaseVersion, CancellationToken cancellationToken) =>
            throw new DeploymentExecutionException("Control readiness failed.");
    }

    private static RustServer.Updater.Executor.DeploymentExecutorOptions CreateOptions(string root) => new()
    {
        RequestStoragePath = Path.Combine(root, "requests"),
        TransactionStoragePath = Path.Combine(root, "transactions"),
        PackageStoragePath = Path.Combine(root, "staging"),
        ApplicationRegistryPath = Path.Combine(root, "runtime", "applications.ini"),
        LockPath = Path.Combine(root, "deployment.lock"),
        SystemdUnitDirectory = Path.Combine(root, "systemd"),
        UpdaterApplicationName = UpdaterName
    };

    private static async Task CreateUpdaterReleaseAsync(string releasePath, string version)
    {
        Directory.CreateDirectory(Path.Combine(releasePath, "api"));
        Directory.CreateDirectory(Path.Combine(releasePath, "executor"));
        Directory.CreateDirectory(Path.Combine(releasePath, "systemd"));
        await File.WriteAllTextAsync(Path.Combine(releasePath, "api", "RustServer.Updater.Api.dll"), version);
        await File.WriteAllTextAsync(Path.Combine(releasePath, "executor", "RustServer.Updater.Executor.dll"), version);
        await File.WriteAllTextAsync(
            Path.Combine(releasePath, UpdaterReleaseBundle.ManifestFileName),
            JsonSerializer.Serialize(UpdaterReleaseBundle.CreateManifest(version)));
        foreach (var unit in SystemdUnits)
        {
            await File.WriteAllTextAsync(Path.Combine(releasePath, "systemd", unit), $"{version}:{unit}");
        }
    }

    private static async Task WritePreparedManifestAsync(string stagingPath, string applicationName, string version)
    {
        var contents = await PreparedPackageIntegrity.CreateAsync(stagingPath, CancellationToken.None);
        var manifest = new PreparedPackageManifest(
            applicationName,
            version,
            new string('A', 64),
            DateTimeOffset.UtcNow,
            contents);
        await File.WriteAllTextAsync(
            Path.Combine(stagingPath, PreparedPackageIntegrity.ManifestFileName),
            JsonSerializer.Serialize(manifest));
    }

    private sealed class UpdaterEnvironment : IDisposable
    {
        private UpdaterEnvironment(TemporaryDirectory directory, string installationPath)
        {
            TemporaryDirectory = directory;
            InstallationPath = installationPath;
            RootPath = directory.Path;
        }

        public TemporaryDirectory TemporaryDirectory { get; }
        public string RootPath { get; }
        public string InstallationPath { get; }
        public InMemoryReleaseLinkManager Links { get; private set; } = null!;
        public string LockPath => Path.Combine(RootPath, "deployment.lock");
        public string SystemdPath => Path.Combine(RootPath, "systemd");
        public string TransactionsPath => Path.Combine(RootPath, "transactions");

        public static async Task<UpdaterEnvironment> CreateAsync(
            string currentVersion = PreviousVersion,
            string installedUnitVersion = PreviousVersion,
            bool targetReleaseInstalled = false)
        {
            var directory = new TemporaryDirectory();
            try
            {
                var installationPath = Path.Combine(directory.Path, "updater");
                var environment = new UpdaterEnvironment(directory, installationPath);
                await CreateUpdaterReleaseAsync(Path.Combine(installationPath, "releases", PreviousVersion), PreviousVersion);
                if (targetReleaseInstalled)
                {
                    await CreateUpdaterReleaseAsync(Path.Combine(installationPath, "releases", TargetVersion), TargetVersion);
                }
                var stagingPath = Path.Combine(directory.Path, "staging", UpdaterName, TargetVersion);
                await CreateUpdaterReleaseAsync(stagingPath, TargetVersion);
                await WritePreparedManifestAsync(stagingPath, UpdaterName, TargetVersion);
                environment.Links = new InMemoryReleaseLinkManager(currentVersion);
                Directory.CreateDirectory(environment.SystemdPath);
                foreach (var unit in SystemdUnits)
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(environment.SystemdPath, unit),
                        $"{installedUnitVersion}:{unit}");
                }

                return environment;
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }

        public DeploymentExecutor CreateExecutor(
            ISystemdController systemd,
            IApplicationReadinessVerifier readinessVerifier) =>
            new(
                CreateOptions(RootPath),
                systemd,
                readinessVerifier,
                new SingleApplicationRegistry(new ApplicationRegistration(
                    UpdaterName,
                    "owner/RustServer.Updater",
                    InstallationPath,
                    "rustserver-updater.service",
                    "RustServer.Updater.Api.dll",
                    new ApplicationReadinessProbe("http://127.0.0.1:1/health", Attempts: 1, TimeoutSeconds: 1))),
                Links);

        public void Dispose() => TemporaryDirectory.Dispose();
    }

    private sealed class SingleApplicationRegistry(ApplicationRegistration application) : IDeploymentApplicationRegistry
    {
        public ApplicationRegistration GetRequired(string applicationName) =>
            string.Equals(applicationName, application.Name, StringComparison.OrdinalIgnoreCase)
                ? application
                : throw new DeploymentExecutionException("Deployment request does not name a registered application.");
    }

    private sealed class InMemoryReleaseLinkManager(string? currentVersion) : IReleaseLinkManager
    {
        public string? CurrentVersion { get; private set; } = currentVersion;
        public string? PreviousVersion { get; private set; }

        public string? GetCurrentReleaseVersion(ApplicationRegistration application) => CurrentVersion;

        public string? GetPreviousReleaseVersion(ApplicationRegistration application) => PreviousVersion;

        public void PointCurrentAt(ApplicationRegistration application, string releasePath) =>
            CurrentVersion = Path.GetFileName(releasePath);

        public void PointPreviousAt(ApplicationRegistration application, string releasePath) =>
            PreviousVersion = Path.GetFileName(releasePath);

        public void RemoveCurrentLink(ApplicationRegistration application) => CurrentVersion = null;
    }

    private sealed class LockAwareSystemdController(string lockPath, InMemoryReleaseLinkManager links) : ISystemdController
    {
        public List<string> Events { get; } = [];
        public List<(string Version, SystemdStartMode Mode)> Starts { get; } = [];
        public List<(string Version, SystemdStartMode? Mode)> Stops { get; } = [];
        public bool ObservedDeploymentLockDuringStart { get; private set; }

        public Task StopAsync(string unitName, CancellationToken cancellationToken)
        {
            var version = Assert.IsType<string>(links.CurrentVersion);
            Stops.Add((version, null));
            Events.Add($"stop:{version}");
            return Task.CompletedTask;
        }

        public Task StartAsync(string unitName, SystemdStartMode mode, CancellationToken cancellationToken)
        {
            var version = Assert.IsType<string>(links.CurrentVersion);
            Starts.Add((version, mode));
            Events.Add($"start:{version}");
            try
            {
                using var ignored = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                ObservedDeploymentLockDuringStart = true;
            }

            return Task.CompletedTask;
        }

        public Task EnableAsync(string unitName, CancellationToken cancellationToken)
        {
            Events.Add($"enable:{links.CurrentVersion}");
            return Task.CompletedTask;
        }

        public Task DisableAsync(string unitName, CancellationToken cancellationToken)
        {
            Events.Add($"disable:{links.CurrentVersion}");
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken cancellationToken)
        {
            Events.Add("reload");
            return Task.CompletedTask;
        }
    }

    private sealed class ProcessBackedReadinessVerifier(
        string lockPath,
        SystemdCommandRecorder systemd,
        string expectedStartCommand) : IApplicationReadinessVerifier
    {
        public List<string> VerifiedVersions { get; } = [];
        public bool ObservedDeploymentLock { get; private set; }

        public async Task VerifyAsync(
            ApplicationRegistration application,
            string expectedReleaseVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var ignored = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                ObservedDeploymentLock = true;
            }

            Assert.True(ObservedDeploymentLock);
            Assert.Contains(expectedStartCommand, await systemd.ReadCommandsAsync());
            VerifiedVersions.Add(expectedReleaseVersion);
        }
    }

    private sealed class SystemdCommandRecorder
    {
        private SystemdCommandRecorder(string executablePath, IReadOnlyList<string> prefixArguments, string logPath)
        {
            ExecutablePath = executablePath;
            PrefixArguments = prefixArguments;
            LogPath = logPath;
        }

        private string ExecutablePath { get; }
        private IReadOnlyList<string> PrefixArguments { get; }
        private string LogPath { get; }

        public static async Task<SystemdCommandRecorder> CreateAsync(string rootPath)
        {
            var logPath = Path.Combine(rootPath, "systemctl.log");
            if (OperatingSystem.IsWindows())
            {
                var scriptPath = Path.Combine(rootPath, "systemctl-recorder.ps1");
                await File.WriteAllTextAsync(scriptPath, """
                    param(
                        [string] $LogPath,
                        [Parameter(ValueFromRemainingArguments = $true)]
                        [string[]] $SystemctlArguments
                    )
                    [System.IO.File]::AppendAllText($LogPath, (($SystemctlArguments -join ' ') + [Environment]::NewLine))
                    exit 0
                    """);
                return new SystemdCommandRecorder(
                    Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                    ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath, logPath],
                    logPath);
            }

            var shellScriptPath = Path.Combine(rootPath, "systemctl-recorder.sh");
            await File.WriteAllTextAsync(shellScriptPath, """
                #!/bin/sh
                log_path="$1"
                shift
                printf '%s\n' "$*" >> "$log_path"
                """);
            File.SetUnixFileMode(
                shellScriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return new SystemdCommandRecorder("/bin/sh", [shellScriptPath, logPath], logPath);
        }

        public SystemdController CreateController() => new(ExecutablePath, PrefixArguments);

        public async Task<IReadOnlyList<string>> ReadCommandsAsync()
        {
            if (!File.Exists(LogPath))
            {
                return [];
            }

            return await File.ReadAllLinesAsync(LogPath);
        }
    }

    private sealed class TrackingReadinessVerifier(
        LockAwareSystemdController systemd,
        string? failVersion = null) : IApplicationReadinessVerifier
    {
        public List<string> VerifiedVersions { get; } = [];

        public Task VerifyAsync(
            ApplicationRegistration application,
            string expectedReleaseVersion,
            CancellationToken cancellationToken)
        {
            Assert.True(systemd.ObservedDeploymentLockDuringStart);
            Assert.Contains(systemd.Starts, start => start.Version == expectedReleaseVersion);
            VerifiedVersions.Add(expectedReleaseVersion);
            if (string.Equals(expectedReleaseVersion, failVersion, StringComparison.Ordinal))
            {
                throw new DeploymentExecutionException("Readiness failed for the newly started Updater.");
            }
            return Task.CompletedTask;
        }
    }
}
