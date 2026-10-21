using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Executor;

/// <summary>Proves that a restarted application is serving its registered readiness endpoint.</summary>
public interface IApplicationReadinessVerifier
{
    /// <summary>Waits for the configured readiness check or throws an executor failure.</summary>
    Task VerifyAsync(ApplicationRegistration application, string expectedReleaseVersion, CancellationToken cancellationToken);
}

/// <summary>Performs bounded HTTP readiness checks for the root executor.</summary>
public sealed class HttpApplicationReadinessVerifier(HttpMessageHandler? handler = null) : IApplicationReadinessVerifier, IDisposable
{
    private readonly HttpClient _client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: handler is null);

    /// <inheritdoc />
    public async Task VerifyAsync(
        ApplicationRegistration application,
        string expectedReleaseVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var probe = application.Readiness
            ?? throw new DeploymentExecutionException("A registered readiness check is required for deployment.");

        for (var attempt = 1; attempt <= probe.Attempts; attempt++)
        {
            try
            {
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestCancellation.CancelAfter(TimeSpan.FromSeconds(probe.TimeoutSeconds));
                using var response = await _client.GetAsync(probe.Url, requestCancellation.Token);
                if (response.StatusCode == HttpStatusCode.OK
                    && HasExpectedStaticWebReleaseHeader(application, response, expectedReleaseVersion))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                // A process which is still starting is expected to refuse connections.
            }
            catch (OperationCanceledException)
            {
                // The per-attempt timeout is retried below.
            }

            if (attempt < probe.Attempts && probe.DelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(probe.DelaySeconds), cancellationToken);
            }
        }

        throw new DeploymentExecutionException("The application did not pass its registered readiness check.");
    }

    private static bool HasExpectedStaticWebReleaseHeader(
        ApplicationRegistration application,
        HttpResponseMessage response,
        string expectedReleaseVersion)
    {
        if (application.Type != ApplicationType.StaticWeb)
        {
            return true;
        }

        return response.Headers.TryGetValues("X-RustServer-Release", out var values)
            && values.Count() == 1
            && string.Equals(values.Single(), expectedReleaseVersion, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Root-owned executor for ordinary deployments, paired Updater self-updates,
/// and crash recovery. It only consumes durable operation IDs and fixed local
/// configuration; no HTTP-supplied path, command, or systemd unit is trusted.
/// </summary>
public sealed class DeploymentExecutor
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] UpdaterSystemdUnits =
    [
        "rustserver-updater.service",
        "rustserver-updater-executor.socket",
        "rustserver-updater-executor@.service",
        "rustserver-updater-recovery.service"
    ];

    private readonly string _requestStoragePath;
    private readonly string _transactionStoragePath;
    private readonly string _packageStoragePath;
    private readonly string _lockPath;
    private readonly string _systemdUnitDirectory;
    private readonly string _updaterApplicationName;
    private readonly ISystemdController _systemd;
    private readonly ControlNetworkAccessUnitInstaller _controlHelper;
    private readonly IApplicationReadinessVerifier _readinessVerifier;
    private readonly IDeploymentApplicationRegistry _applicationRegistry;
    private readonly IReleaseLinkManager _releaseLinks;

    /// <summary>Creates a production executor using the fixed runtime paths.</summary>
    public DeploymentExecutor(
        DeploymentExecutorOptions options,
        ISystemdController systemd,
        IApplicationReadinessVerifier readinessVerifier)
        : this(
            options,
            systemd,
            readinessVerifier,
            new FileDeploymentApplicationRegistry(options?.ApplicationRegistryPath
                ?? throw new ArgumentNullException(nameof(options))),
            new FileReleaseLinkManager())
    {
    }

    /// <summary>Creates an executor with an explicit runtime registry for integration tests.</summary>
    internal DeploymentExecutor(
        DeploymentExecutorOptions options,
        ISystemdController systemd,
        IApplicationReadinessVerifier readinessVerifier,
        IDeploymentApplicationRegistry applicationRegistry)
        : this(options, systemd, readinessVerifier, applicationRegistry, new FileReleaseLinkManager())
    {
    }

    /// <summary>Creates an executor with explicit runtime-registry and release-link boundaries for integration tests.</summary>
    internal DeploymentExecutor(
        DeploymentExecutorOptions options,
        ISystemdController systemd,
        IApplicationReadinessVerifier readinessVerifier,
        IDeploymentApplicationRegistry applicationRegistry,
        IReleaseLinkManager releaseLinks)
    {
        ArgumentNullException.ThrowIfNull(options);
        _systemd = systemd ?? throw new ArgumentNullException(nameof(systemd));
        _readinessVerifier = readinessVerifier ?? throw new ArgumentNullException(nameof(readinessVerifier));
        _applicationRegistry = applicationRegistry ?? throw new ArgumentNullException(nameof(applicationRegistry));
        _releaseLinks = releaseLinks ?? throw new ArgumentNullException(nameof(releaseLinks));
        _requestStoragePath = RequireAbsolutePath(options.RequestStoragePath, nameof(options.RequestStoragePath));
        _transactionStoragePath = RequireAbsolutePath(options.TransactionStoragePath, nameof(options.TransactionStoragePath));
        _packageStoragePath = RequireAbsolutePath(options.PackageStoragePath, nameof(options.PackageStoragePath));
        _ = RequireAbsolutePath(options.ApplicationRegistryPath, nameof(options.ApplicationRegistryPath));
        _lockPath = RequireAbsolutePath(options.LockPath, nameof(options.LockPath));
        _systemdUnitDirectory = RequireAbsolutePath(options.SystemdUnitDirectory, nameof(options.SystemdUnitDirectory));
        _controlHelper = new ControlNetworkAccessUnitInstaller(_systemdUnitDirectory, _systemd);
        _updaterApplicationName = RequireSimpleName(options.UpdaterApplicationName, nameof(options.UpdaterApplicationName));
    }

    /// <summary>Loads the request identified by a socket payload and executes it once.</summary>
    public async Task<DeploymentTransaction> ExecuteRequestAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("Operation identifier must not be empty.", nameof(operationId));
        }

        var request = await ReadRequestAsync(operationId, cancellationToken);
        return await ExecuteAsync(request, cancellationToken);
    }

    /// <summary>Executes an already validated request. Intended for the socket host and tests.</summary>
    public async Task<DeploymentTransaction> ExecuteAsync(
        DeploymentExecutorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        await using var deploymentLock = await TryAcquireLockAsync(cancellationToken);
        if (deploymentLock is null)
        {
            return await WriteTransactionAsync(new DeploymentTransaction(
                request.OperationId,
                request.ApplicationName,
                DeploymentTransactionStatus.Failed,
                "Deployment lock unavailable",
                null,
                request.TargetVersion,
                null,
                "Another deployment or recovery operation holds the deployment lock.",
                DateTimeOffset.UtcNow), cancellationToken);
        }

        try
        {
            var application = LoadRegisteredApplication(request.ApplicationName);
            return await ExecuteUnderLockAsync(application, request, isRecovery: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidDataException
                                          or ApplicationRegistryConfigurationException
                                          or ApplicationRegistrationValidationException
                                          or DeploymentExecutionException
                                          or ArgumentException)
        {
            return await WriteTransactionAsync(new DeploymentTransaction(
                request.OperationId,
                request.ApplicationName,
                DeploymentTransactionStatus.Failed,
                "Deployment rejected",
                null,
                request.TargetVersion,
                null,
                ToSafeError(exception),
                DateTimeOffset.UtcNow), CancellationToken.None);
        }
    }

    /// <summary>
    /// Rolls back durable nonterminal transactions after a crash. Recovery owns
    /// the only non-self-update use of <c>systemctl --ignore-dependencies</c>.
    /// </summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var deploymentLock = await TryAcquireLockAsync(cancellationToken);
        if (deploymentLock is null)
        {
            return;
        }

        foreach (var transaction in await ReadTransactionsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (transaction.IsTerminal)
            {
                continue;
            }

            try
            {
                var application = LoadRegisteredApplication(transaction.ApplicationName);
                if (transaction.Step is "Validating deployment" or "Provisioning first-install dependencies"
                    && string.Equals(
                        _releaseLinks.GetCurrentReleaseVersion(application),
                        transaction.PreviousVersion,
                        StringComparison.Ordinal))
                {
                    // An interrupted pre-activation phase has never switched current or
                    // started a new service. Provider state is retained for safe retry.
                    await WriteTransactionAsync(transaction with
                    {
                        Status = DeploymentTransactionStatus.Failed,
                        Step = "Recovery: pre-activation interrupted",
                        Error = "The installation stopped before activation; retry the same verified release.",
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, CancellationToken.None);
                    continue;
                }

                await RestorePreviousReleaseAsync(application, transaction, isRecovery: true, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidDataException
                                              or ApplicationRegistryConfigurationException
                                              or ApplicationRegistrationValidationException
                                              or DeploymentExecutionException
                                              or ArgumentException)
            {
                await WriteTransactionAsync(transaction with
                {
                    Status = DeploymentTransactionStatus.Failed,
                    Step = "Recovery failed",
                    Error = ToSafeError(exception),
                    UpdatedAt = DateTimeOffset.UtcNow
                }, CancellationToken.None);
            }
        }
    }

    private async Task<DeploymentTransaction> ExecuteUnderLockAsync(
        ApplicationRegistration application,
        DeploymentExecutorRequest request,
        bool isRecovery,
        CancellationToken cancellationToken)
    {
        var isUpdater = IsUpdater(application);
        var isSelfActivation = isUpdater && request.Type is OperationType.SelfUpdate or OperationType.Rollback;
        if (request.Type == OperationType.SelfUpdate && !isUpdater)
        {
            throw new DeploymentExecutionException("Only the configured Updater registration can perform a self-update.");
        }

        if (request.Type == OperationType.Prepare)
        {
            return await SecurePreparedPackageAsync(application, request, cancellationToken);
        }

        if (application.Readiness is null)
        {
            throw new DeploymentExecutionException("A registered readiness check is required for deployment.");
        }

        var previousVersion = _releaseLinks.GetCurrentReleaseVersion(application);
        var targetVersion = ResolveTargetVersion(application, request);
        var transaction = await WriteTransactionAsync(new DeploymentTransaction(
            request.OperationId,
            application.Name,
            DeploymentTransactionStatus.Activating,
            "Validating deployment",
            previousVersion,
            targetVersion,
            previousVersion,
            null,
            DateTimeOffset.UtcNow), cancellationToken);

        var activationStarted = false;
        try
        {
            var targetReleasePath = await EnsureTargetReleaseAsync(application, request, targetVersion, isSelfActivation, cancellationToken);
            if (isSelfActivation)
            {
                await ValidateInstalledUpdaterBundleAsync(targetReleasePath, targetVersion, cancellationToken);
            }
            if (ControlNetworkAccessUnitInstaller.OwnsHelper(application))
            {
                ControlNetworkAccessUnitInstaller.ValidateRelease(targetReleasePath);
            }

            if (request.Type == OperationType.Install && previousVersion is null && application.InstallHook is not null)
            {
                transaction = await WriteTransactionAsync(transaction with
                {
                    Step = "Provisioning first-install dependencies",
                    UpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
                var hook = await RunFirstInstallHookAsync(application, targetVersion, cancellationToken);
                transaction = await WriteTransactionAsync(transaction with
                {
                    ProvisioningOutput = hook.ExitCode != 0 || hook.TimedOut || hook.Cancelled ? hook.Output : null,
                    ProvisioningExitCode = hook.ExitCode,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, CancellationToken.None);
                if (hook.Cancelled) throw new OperationCanceledException(cancellationToken);
                if (hook.TimedOut) throw new DeploymentExecutionException("First-install provisioning timed out.");
                if (hook.ExitCode != 0)
                {
                    throw new DeploymentExecutionException(
                        $"First-install provisioning failed: {hook.Diagnostic} exit code {hook.ExitCode}.");
                }
            }

            transaction = await WriteTransactionAsync(transaction with
            {
                Step = isSelfActivation
                    ? "Activating paired Updater and Executor bundle"
                    : "Activating release",
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            activationStarted = true;
            if (application.Type == ApplicationType.DotNet)
            {
                await _systemd.StopAsync(application.ServiceName, cancellationToken);
            }
            if (ControlNetworkAccessUnitInstaller.OwnsHelper(application) && _controlHelper.IsInstalled(application))
            {
                await _systemd.StopAsync(ControlNetworkAccessUnitInstaller.UnitName, cancellationToken);
            }

            var unitBackup = isSelfActivation
                ? await InstallUpdaterSystemdUnitsAsync(targetReleasePath, cancellationToken)
                : [];
            try
            {
                _releaseLinks.PointCurrentAt(application, targetReleasePath);
                if (ControlNetworkAccessUnitInstaller.OwnsHelper(application))
                {
                    await _controlHelper.InstallAsync(application, targetReleasePath, cancellationToken);
                }
                if (previousVersion is not null)
                {
                    _releaseLinks.PointPreviousAt(application, GetReleasePath(application, previousVersion));
                }
                if (application.Type == ApplicationType.DotNet)
                {
                    await _systemd.StartAsync(
                        application.ServiceName,
                        GetStartMode(isSelfActivation, isRecovery),
                        cancellationToken);
                }

                await _readinessVerifier.VerifyAsync(application, targetVersion, cancellationToken);

                // Bootstrap provisions service units but does not enable uninstalled apps.
                // Only an activated, readiness-verified .NET release may start on future boots.
                // The Updater's own boot policy is owned by Bootstrap.
                if (application.Type == ApplicationType.DotNet && !isUpdater)
                {
                    if (ControlNetworkAccessUnitInstaller.OwnsHelper(application))
                    {
                        await _systemd.EnableAsync(ControlNetworkAccessUnitInstaller.UnitName, cancellationToken);
                    }
                    await _systemd.EnableAsync(application.ServiceName, cancellationToken);
                }

                return await WriteTransactionAsync(transaction with
                {
                    Status = DeploymentTransactionStatus.Succeeded,
                    Step = isSelfActivation
                        ? "Paired Updater and Executor bundle activated"
                        : "Deployment completed",
                    ActiveVersion = targetVersion,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidDataException
                                              or DeploymentExecutionException
                                              or ArgumentException)
            {
                return await RestorePreviousReleaseAsync(
                    application,
                    transaction,
                    isRecovery,
                    cancellationToken,
                    exception,
                    unitBackup);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidDataException
                                          or DeploymentExecutionException
                                          or ArgumentException)
        {
            // No service or current link was changed during validation/provisioning.
            // Never attempt rollback/stop of a unit that may not exist yet.
            if (!activationStarted)
            {
                return await WriteTransactionAsync(transaction with
                {
                    Status = DeploymentTransactionStatus.Failed,
                    Step = "Pre-activation failed",
                    Error = ToSafeError(exception),
                    UpdatedAt = DateTimeOffset.UtcNow
                }, CancellationToken.None);
            }

            return await RestorePreviousReleaseAsync(application, transaction, isRecovery, cancellationToken, exception);
        }
    }

    private async Task<HookResult> RunFirstInstallHookAsync(
        ApplicationRegistration application,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        var protectedArtifact = GetPreparedArtifactPath(application, targetVersion);
        EnsureOrdinaryDirectory(protectedArtifact, "First-install hook requires a protected Prepare artifact.");
        EnsurePreparedArtifactPermissions(protectedArtifact);
        await VerifyStagedPackageAsync(application, protectedArtifact, targetVersion, false, cancellationToken);

        // The protected artifact and mutable staging must represent exactly the same verified package.
        var manifestName = PreparedPackageIntegrity.ManifestFileName;
        var stagingManifest = await File.ReadAllBytesAsync(
            Path.Combine(GetStagedPackagePath(application, targetVersion), manifestName), cancellationToken);
        var protectedManifest = await File.ReadAllBytesAsync(
            Path.Combine(protectedArtifact, manifestName), cancellationToken);
        if (!stagingManifest.AsSpan().SequenceEqual(protectedManifest))
        {
            throw new DeploymentExecutionException("First-install hook artifact differs from the verified package.");
        }

        // Only the audited, relative hook contract may be executed as root.
        if (application.InstallHook is not "provisioning/provision.sh")
        {
            throw new DeploymentExecutionException("The registered first-install hook is not permitted.");
        }

        var script = Path.Combine(protectedArtifact, application.InstallHook.Replace('/', Path.DirectorySeparatorChar));
        if (!IsRegularFile(script))
        {
            throw new DeploymentExecutionException("This release does not contain its required first-install provisioning script; publish a newer release.");
        }

        using var diagnostic = new ProvisioningDiagnosticFile();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { script, "install" }
            }
        };

        process.StartInfo.Environment["RUSTSERVER_PROVISIONING_DIAGNOSTIC"] = diagnostic.Path;

        try
        {
            if (!process.Start())
                throw new DeploymentExecutionException("First-install provisioning process could not start.");

            using var drainCancellation = new CancellationTokenSource();
            var stdout = ReadHookOutputAsync(process.StandardOutput, drainCancellation.Token);
            var stderr = ReadHookOutputAsync(process.StandardError, drainCancellation.Token);
            var cancelled = false;
            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = cancellationToken.IsCancellationRequested;
                timedOut = !cancelled;
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }

            // Descendants can keep inherited pipes open even after the hook exits.
            drainCancellation.CancelAfter(TimeSpan.FromSeconds(5));
            var output = await stdout;
            var error = await stderr;
            var text = RedactHookOutput($"stdout:\n{output}\nstderr:\n{error}");
            return new HookResult(text, process.ExitCode, diagnostic.Read(), timedOut, cancelled);
        }
        catch (Win32Exception exception)
        {
            throw new DeploymentExecutionException("First-install provisioning executable is unavailable.", exception);
        }
    }

    private sealed record HookResult(string Output, int ExitCode, string Diagnostic, bool TimedOut, bool Cancelled);

    private static async Task<string> ReadHookOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        const int limit = 64 * 1024;
        var result = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
            {
                result.Append(buffer, 0, count);
                if (result.Length > limit)
                {
                    result.Remove(0, result.Length - limit);
                    truncated = true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.Append("\n[Output drain timed out.]");
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            result.Append("\n[Output could not be read.]");
        }
        if (truncated) result.Append("\n[Output truncated.]");
        return result.ToString();
    }

    private static string RedactHookOutput(string output)
    {
        // Values explicitly marked as secrets in the executor environment are never retained.
        foreach (System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables())
        {
            var name = (string)item.Key;
            var value = item.Value as string;
            if (value is { Length: >= 4 } && Regex.IsMatch(name, "(?i)(password|passwd|token|secret|api_?key)"))
                output = output.Replace(value, "[REDACTED]", StringComparison.Ordinal);
        }
        return Regex.Replace(output,
            "(?i)\\b(password|passwd|token|secret|api[_-]?key)\\s*[:=]\\s*[^\\s,;]+",
            "$1=[REDACTED]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private async Task<DeploymentTransaction> SecurePreparedPackageAsync(
        ApplicationRegistration application,
        DeploymentExecutorRequest request,
        CancellationToken cancellationToken)
    {
        var targetVersion = ResolveTargetVersion(application, request);
        var stagingPath = GetStagedPackagePath(application, targetVersion);
        await VerifyStagedPackageAsync(application, stagingPath, targetVersion, isSelfActivation: false, cancellationToken);
        var expectedManifest = await File.ReadAllBytesAsync(
            Path.Combine(stagingPath, PreparedPackageIntegrity.ManifestFileName),
            cancellationToken);
        var preparedArtifactPath = GetPreparedArtifactPath(application, targetVersion);
        var preparedArtifactsRoot = Path.GetDirectoryName(preparedArtifactPath)
            ?? throw new DeploymentExecutionException("Prepared artifact path is invalid.");

        Directory.CreateDirectory(preparedArtifactsRoot);
        EnsureOrdinaryDirectory(preparedArtifactsRoot, "Prepared artifact root is unavailable.");
        SecurePreparedArtifactRootPermissions(preparedArtifactsRoot);
        if (Directory.Exists(preparedArtifactPath))
        {
            EnsureOrdinaryDirectory(preparedArtifactPath, "Existing prepared artifact is unavailable.");
            EnsurePreparedArtifactPermissions(preparedArtifactPath);
            await VerifyStagedPackageAsync(application, preparedArtifactPath, targetVersion, isSelfActivation: false, cancellationToken);
            var existingManifest = await File.ReadAllBytesAsync(
                Path.Combine(preparedArtifactPath, PreparedPackageIntegrity.ManifestFileName),
                cancellationToken);
            if (!expectedManifest.AsSpan().SequenceEqual(existingManifest))
            {
                throw new DeploymentExecutionException("The existing protected artifact does not match the verified staging package.");
            }

            return await WriteTransactionAsync(new DeploymentTransaction(
                request.OperationId,
                application.Name,
                DeploymentTransactionStatus.Succeeded,
                "Verified package already secured",
                null,
                targetVersion,
                null,
                null,
                DateTimeOffset.UtcNow), cancellationToken);
        }
        if (File.Exists(preparedArtifactPath))
        {
            throw new DeploymentExecutionException("Existing prepared artifact is not a directory.");
        }

        var temporaryArtifactPath = Path.Combine(
            preparedArtifactsRoot,
            $".{targetVersion}.{Guid.NewGuid():N}.tmp");
        try
        {
            CopyDirectoryWithoutLinks(stagingPath, temporaryArtifactPath);
            var copiedManifest = await File.ReadAllBytesAsync(
                Path.Combine(temporaryArtifactPath, PreparedPackageIntegrity.ManifestFileName),
                cancellationToken);
            if (!expectedManifest.AsSpan().SequenceEqual(copiedManifest))
            {
                throw new DeploymentExecutionException("Prepared artifact changed while it was being secured.");
            }

            await VerifyStagedPackageAsync(application, temporaryArtifactPath, targetVersion, isSelfActivation: false, cancellationToken);
            SecurePreparedArtifactPermissions(temporaryArtifactPath);
            Directory.Move(temporaryArtifactPath, preparedArtifactPath);
        }
        finally
        {
            // Only an unpublished root-owned handoff tree is removed here.
            // A published prepared artifact is immutable and is never replaced.
            if (Directory.Exists(temporaryArtifactPath))
            {
                Directory.Delete(temporaryArtifactPath, recursive: true);
            }
        }

        return await WriteTransactionAsync(new DeploymentTransaction(
            request.OperationId,
            application.Name,
            DeploymentTransactionStatus.Succeeded,
            "Verified package secured",
            null,
            targetVersion,
            null,
            null,
            DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task<DeploymentTransaction> RestorePreviousReleaseAsync(
        ApplicationRegistration application,
        DeploymentTransaction transaction,
        bool isRecovery,
        CancellationToken cancellationToken,
        Exception? activationException = null,
        IReadOnlyList<SystemdUnitBackup>? previousUnitBackup = null)
    {
        try
        {
            // A self-update may have started a process from the new current
            // link. It must be stopped before either the old unit files or
            // current link are restored; otherwise `systemctl start` can leave
            // the new process running and falsely report the old release up.
            if (application.Type == ApplicationType.DotNet)
            {
                await _systemd.StopAsync(application.ServiceName, cancellationToken);
            }
            if (ControlNetworkAccessUnitInstaller.OwnsHelper(application) && _controlHelper.IsInstalled(application))
            {
                await _systemd.StopAsync(ControlNetworkAccessUnitInstaller.UnitName, cancellationToken);
            }

            if (transaction.PreviousVersion is null)
            {
                if (ControlNetworkAccessUnitInstaller.OwnsHelper(application))
                {
                    await _controlHelper.RemoveAfterFailedFirstInstallAsync(application, cancellationToken);
                }
                _releaseLinks.RemoveCurrentLink(application);
            }
            else
            {
                var previousReleasePath = GetReleasePath(application, transaction.PreviousVersion);
                EnsureOrdinaryDirectory(previousReleasePath, "Previous release is unavailable for rollback.");
                if (IsUpdater(application))
                {
                    await ValidateInstalledUpdaterBundleAsync(previousReleasePath, transaction.PreviousVersion, cancellationToken);
                    if (previousUnitBackup is not null)
                    {
                        await RestoreSystemdUnitsAsync(previousUnitBackup, cancellationToken);
                    }
                    else
                    {
                        await InstallUpdaterSystemdUnitsAsync(previousReleasePath, cancellationToken);
                    }
                }

                _releaseLinks.PointCurrentAt(application, previousReleasePath);
                if (ControlNetworkAccessUnitInstaller.OwnsHelper(application))
                {
                    await _controlHelper.InstallAsync(application, previousReleasePath, cancellationToken);
                }
                if (application.Type == ApplicationType.DotNet)
                {
                    await _systemd.StartAsync(
                        application.ServiceName,
                        GetStartMode(IsUpdater(application), isRecovery && !ControlNetworkAccessUnitInstaller.OwnsHelper(application)),
                        cancellationToken);
                }

                await _readinessVerifier.VerifyAsync(application, transaction.PreviousVersion, cancellationToken);
                if (application.Type == ApplicationType.DotNet && !IsUpdater(application))
                {
                    if (ControlNetworkAccessUnitInstaller.OwnsHelper(application))
                    {
                        await _systemd.EnableAsync(ControlNetworkAccessUnitInstaller.UnitName, cancellationToken);
                    }
                    await _systemd.EnableAsync(application.ServiceName, cancellationToken);
                }
            }

            return await WriteTransactionAsync(transaction with
            {
                Status = DeploymentTransactionStatus.RolledBack,
                Step = isRecovery ? "Recovery rollback completed" : "Rollback completed",
                ActiveVersion = transaction.PreviousVersion,
                Error = activationException is null
                    ? "An interrupted deployment was rolled back during recovery."
                    : ToSafeError(activationException),
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception rollbackException) when (rollbackException is IOException
                                                   or UnauthorizedAccessException
                                                   or InvalidDataException
                                                   or DeploymentExecutionException
                                                   or ArgumentException)
        {
            return await WriteTransactionAsync(transaction with
            {
                Status = DeploymentTransactionStatus.Failed,
                Step = isRecovery ? "Recovery rollback failed" : "Rollback failed",
                ActiveVersion = _releaseLinks.GetCurrentReleaseVersion(application),
                Error = $"Activation failed: {ToSafeError(activationException)} Rollback failed: {ToSafeError(rollbackException)}",
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
    }

    private async Task<string> EnsureTargetReleaseAsync(
        ApplicationRegistration application,
        DeploymentExecutorRequest request,
        string targetVersion,
        bool isSelfActivation,
        CancellationToken cancellationToken)
    {
        var targetReleasePath = GetReleasePath(application, targetVersion);
        if (request.Type == OperationType.Rollback)
        {
            EnsureOrdinaryDirectory(targetReleasePath, "Requested rollback release is unavailable.");
            return targetReleasePath;
        }

        var stagedPath = GetStagedPackagePath(application, targetVersion);
        await VerifyStagedPackageAsync(application, stagedPath, targetVersion, isSelfActivation, cancellationToken);
        if (Directory.Exists(targetReleasePath))
        {
            // Retry after a failed first install may reuse exactly the same immutable files.
            // Never rewrite them and never accept a differing release under the same tag.
            EnsureOrdinaryDirectory(targetReleasePath, "Existing immutable release is unsafe.");
            await VerifyStagedPackageAsync(application, targetReleasePath, targetVersion, isSelfActivation, cancellationToken);
            var manifestName = PreparedPackageIntegrity.ManifestFileName;
            var requestedManifest = await File.ReadAllBytesAsync(Path.Combine(stagedPath, manifestName), cancellationToken);
            var installedManifest = await File.ReadAllBytesAsync(Path.Combine(targetReleasePath, manifestName), cancellationToken);
            if (!requestedManifest.AsSpan().SequenceEqual(installedManifest))
            {
                throw new DeploymentExecutionException("Existing immutable release differs from the verified package.");
            }

            return targetReleasePath;
        }

        var releasesPath = Path.GetDirectoryName(targetReleasePath)
            ?? throw new DeploymentExecutionException("Release path is invalid.");
        Directory.CreateDirectory(releasesPath);
        var temporaryReleasePath = Path.Combine(releasesPath, $".{targetVersion}.{Guid.NewGuid():N}.tmp");
        try
        {
            CopyDirectoryWithoutLinks(stagedPath, temporaryReleasePath);
            Directory.Move(temporaryReleasePath, targetReleasePath);
        }
        finally
        {
            // Only a not-yet-published temporary tree is cleaned. Immutable
            // release directories are never removed by the executor.
            if (Directory.Exists(temporaryReleasePath))
            {
                Directory.Delete(temporaryReleasePath, recursive: true);
            }
        }

        return targetReleasePath;
    }

    private async Task VerifyStagedPackageAsync(
        ApplicationRegistration application,
        string stagedPath,
        string targetVersion,
        bool isSelfActivation,
        CancellationToken cancellationToken)
    {
        EnsureOrdinaryDirectory(stagedPath, "Verified staging package is unavailable.");
        var manifestPath = Path.Combine(stagedPath, PreparedPackageIntegrity.ManifestFileName);
        if (!IsRegularFile(manifestPath))
        {
            throw new DeploymentExecutionException("Verified staging package manifest is missing.");
        }

        PreparedPackageManifest? manifest;
        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            manifest = await JsonSerializer.DeserializeAsync<PreparedPackageManifest>(stream, SerializerOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new DeploymentExecutionException("Verified staging package manifest is invalid.", exception);
        }

        if (manifest is null
            || !string.Equals(manifest.ApplicationName, application.Name, StringComparison.Ordinal)
            || !string.Equals(manifest.ReleaseTag, targetVersion, StringComparison.Ordinal)
            || !await PreparedPackageIntegrity.MatchesAsync(stagedPath, manifest.Contents, cancellationToken))
        {
            throw new DeploymentExecutionException("Verified staging package no longer matches its immutable manifest.");
        }

        if (isSelfActivation)
        {
            await UpdaterReleaseBundle.ValidateAsync(stagedPath, targetVersion, cancellationToken);
        }
    }

    private async Task ValidateInstalledUpdaterBundleAsync(
        string releasePath,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await UpdaterReleaseBundle.ValidateAsync(releasePath, targetVersion, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new DeploymentExecutionException("Updater and Executor cannot be activated independently.", exception);
        }
    }

    private async Task<IReadOnlyList<SystemdUnitBackup>> InstallUpdaterSystemdUnitsAsync(
        string releasePath,
        CancellationToken cancellationToken)
    {
        var sourceDirectory = Path.Combine(releasePath, "systemd");
        EnsureOrdinaryDirectory(sourceDirectory, "Updater release is missing its systemd resources.");
        var sources = UpdaterSystemdUnits.Select(unit => (Unit: unit, Source: Path.Combine(sourceDirectory, unit))).ToArray();
        if (sources.Any(item => !IsRegularFile(item.Source)))
        {
            throw new DeploymentExecutionException("Updater release is missing a required systemd resource.");
        }

        Directory.CreateDirectory(_systemdUnitDirectory);
        var backups = new List<SystemdUnitBackup>(sources.Length);
        try
        {
            foreach (var (unit, source) in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(_systemdUnitDirectory, unit);
                backups.Add(new SystemdUnitBackup(destination, File.Exists(destination) ? await File.ReadAllBytesAsync(destination, cancellationToken) : null));
                await ReplaceFileAsync(source, destination, cancellationToken);
            }

            await _systemd.ReloadAsync(cancellationToken);
            return backups;
        }
        catch
        {
            try
            {
                await RestoreSystemdUnitsAsync(backups, CancellationToken.None);
            }
            catch
            {
                // Preserve the original failure; a later transaction recovery
                // will still select and reinstall the prior complete bundle.
            }

            throw;
        }
    }

    private async Task RestoreSystemdUnitsAsync(
        IReadOnlyList<SystemdUnitBackup> backups,
        CancellationToken cancellationToken)
    {
        if (backups.Count == 0)
        {
            return;
        }

        foreach (var backup in backups)
        {
            if (backup.Contents is null)
            {
                if (File.Exists(backup.Path))
                {
                    File.Delete(backup.Path);
                }

                continue;
            }

            var temporary = $"{backup.Path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, backup.Contents, cancellationToken);
                File.Move(temporary, backup.Path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        await _systemd.ReloadAsync(cancellationToken);
    }

    private async Task<DeploymentExecutorRequest> ReadRequestAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_requestStoragePath, $"{operationId:N}.json");
        if (!IsRegularFile(path))
        {
            throw new DeploymentExecutionException("Deployment request is missing.");
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var request = await JsonSerializer.DeserializeAsync<DeploymentExecutorRequest>(stream, SerializerOptions, cancellationToken)
                ?? throw new DeploymentExecutionException("Deployment request is invalid.");
            if (request.OperationId != operationId)
            {
                throw new DeploymentExecutionException("Deployment request identifier does not match its durable file name.");
            }

            return request;
        }
        catch (JsonException exception)
        {
            throw new DeploymentExecutionException("Deployment request is invalid.", exception);
        }
    }

    private ApplicationRegistration LoadRegisteredApplication(string applicationName)
    {
        return _applicationRegistry.GetRequired(applicationName);
    }

    private string ResolveTargetVersion(ApplicationRegistration application, DeploymentExecutorRequest request)
    {
        if (request.Type != OperationType.Rollback)
        {
            return RequireReleaseVersion(request.TargetVersion);
        }

        if (!string.IsNullOrWhiteSpace(request.TargetVersion))
        {
            return RequireReleaseVersion(request.TargetVersion);
        }

        var previous = _releaseLinks.GetPreviousReleaseVersion(application);
        return previous ?? throw new DeploymentExecutionException("No previous successful release is available for rollback.");
    }

    private string GetStagedPackagePath(ApplicationRegistration application, string releaseVersion)
    {
        var applicationRoot = Path.GetFullPath(Path.Combine(_packageStoragePath, application.Name));
        var packagePath = Path.GetFullPath(Path.Combine(applicationRoot, releaseVersion));
        if (!packagePath.StartsWith(applicationRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new DeploymentExecutionException("Staging path is invalid.");
        }

        return packagePath;
    }

    private static string GetPreparedArtifactPath(ApplicationRegistration application, string releaseVersion)
    {
        var preparedArtifactsRoot = Path.GetFullPath(Path.Combine(application.InstallationPath, "prepared"));
        var preparedArtifactPath = Path.GetFullPath(Path.Combine(preparedArtifactsRoot, releaseVersion));
        if (!preparedArtifactPath.StartsWith(preparedArtifactsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new DeploymentExecutionException("Prepared artifact path is invalid.");
        }

        return preparedArtifactPath;
    }

    private static void SecurePreparedArtifactPermissions(string artifactPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(artifactPath, "*", SearchOption.AllDirectories)
                     .Prepend(artifactPath))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                throw new DeploymentExecutionException("Prepared artifact contains a symbolic link or reparse point.");
            }

            var mode = File.GetUnixFileMode(entry)
                & ~(UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
            if (string.Equals(entry, artifactPath, StringComparison.Ordinal))
            {
                mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
            }

            File.SetUnixFileMode(entry, mode);
        }
    }

    private static void SecurePreparedArtifactRootPermissions(string preparedArtifactsRoot)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if ((File.GetAttributes(preparedArtifactsRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DeploymentExecutionException("Prepared artifact root contains a symbolic link or reparse point.");
        }

        File.SetUnixFileMode(
            preparedArtifactsRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
    }

    private static void EnsurePreparedArtifactPermissions(string artifactPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(artifactPath, "*", SearchOption.AllDirectories)
                     .Prepend(artifactPath))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                throw new DeploymentExecutionException("Prepared artifact contains a symbolic link or reparse point.");
            }

            var mode = File.GetUnixFileMode(entry);
            if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            {
                throw new DeploymentExecutionException("Existing prepared artifact is writable by group or others.");
            }
        }
    }

    private static void CopyDirectoryWithoutLinks(string source, string destination)
    {
        EnsureOrdinaryDirectory(source, "Verified staging package is unavailable.");
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new DeploymentExecutionException("Verified staging package contains a symbolic link or reparse point.");
            }

            var relativePath = Path.GetRelativePath(source, entry);
            var destinationPath = Path.GetFullPath(Path.Combine(destination, relativePath));
            if (!destinationPath.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new DeploymentExecutionException("Verified staging package contains an unsafe path.");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(destinationPath);
            }
            else if (IsRegularFile(entry))
            {
                var parent = Path.GetDirectoryName(destinationPath)
                    ?? throw new DeploymentExecutionException("Release destination is invalid.");
                Directory.CreateDirectory(parent);
                File.Copy(entry, destinationPath, overwrite: false);
            }
            else
            {
                throw new DeploymentExecutionException("Verified staging package contains an unsupported filesystem entry.");
            }
        }
    }

    private static async Task ReplaceFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destinationStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                await destinationStream.FlushAsync(cancellationToken);
                destinationStream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string GetReleasePath(ApplicationRegistration application, string releaseVersion)
    {
        releaseVersion = RequireReleaseVersion(releaseVersion);
        var releasesPath = Path.GetFullPath(Path.Combine(application.InstallationPath, "releases"));
        var releasePath = Path.GetFullPath(Path.Combine(releasesPath, releaseVersion));
        if (!releasePath.StartsWith(releasesPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new DeploymentExecutionException("Release path is invalid.");
        }

        return releasePath;
    }

    private async Task<DeploymentTransaction> WriteTransactionAsync(
        DeploymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_transactionStoragePath);
        var destination = Path.Combine(_transactionStoragePath, $"{transaction.OperationId:N}.json");
        var temporary = Path.Combine(_transactionStoragePath, $".{transaction.OperationId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, transaction, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: true);
            return transaction;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private async Task<IReadOnlyList<DeploymentTransaction>> ReadTransactionsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_transactionStoragePath))
        {
            return [];
        }

        var transactions = new List<DeploymentTransaction>();
        foreach (var path in Directory.EnumerateFiles(_transactionStoragePath, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var transaction = await JsonSerializer.DeserializeAsync<DeploymentTransaction>(stream, SerializerOptions, cancellationToken);
                if (transaction is not null)
                {
                    transactions.Add(transaction);
                }
            }
            catch (JsonException)
            {
                // A corrupt record is not evidence that any release can be safely changed.
            }
            catch (IOException)
            {
                // A concurrently written record is revisited on the next recovery run.
            }
        }

        return transactions;
    }

    private async Task<FileStream?> TryAcquireLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_lockPath)
            ?? throw new DeploymentExecutionException("Deployment lock path is invalid.");
        Directory.CreateDirectory(directory);
        try
        {
            return await Task.FromResult(new FileStream(
                _lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous | FileOptions.WriteThrough));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private bool IsUpdater(ApplicationRegistration application) =>
        string.Equals(application.Name, _updaterApplicationName, StringComparison.OrdinalIgnoreCase);

    private static SystemdStartMode GetStartMode(bool isSelfActivation, bool isRecovery) =>
        DeploymentStartPolicy.For(isSelfActivation, isRecovery);

    private static void ValidateRequest(DeploymentExecutorRequest request)
    {
        if (request.OperationId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ApplicationName)
            || request.Type is not (OperationType.Prepare
                or OperationType.Install
                or OperationType.Update
                or OperationType.Rollback
                or OperationType.SelfUpdate))
        {
            throw new ArgumentException("Deployment request is invalid.", nameof(request));
        }

        if (request.Type != OperationType.Rollback)
        {
            _ = RequireReleaseVersion(request.TargetVersion);
        }
        else if (!string.IsNullOrWhiteSpace(request.TargetVersion))
        {
            _ = RequireReleaseVersion(request.TargetVersion);
        }
    }

    private static string RequireReleaseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsRegularReleaseName(value.Trim()))
        {
            throw new DeploymentExecutionException("A safe concrete release version is required.");
        }

        return value.Trim();
    }

    private static bool IsRegularReleaseName(string value) =>
        value.Length <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static string RequireSimpleName(string value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Trim().All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
        {
            throw new ArgumentException("A simple name is required.", settingName);
        }

        return value.Trim();
    }

    private static string RequireAbsolutePath(string path, string settingName)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!path.StartsWith("/", StringComparison.Ordinal) && !Path.IsPathFullyQualified(path)))
        {
            throw new ArgumentException("Path must be absolute.", settingName);
        }

        return Path.GetFullPath(path);
    }

    private static void EnsureOrdinaryDirectory(string path, string message)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DeploymentExecutionException(message);
        }
    }

    private static bool IsRegularFile(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static string ToSafeError(Exception? exception) =>
        exception is null ? "Deployment interrupted without an error message." : exception.Message;

    private sealed record SystemdUnitBackup(string Path, byte[]? Contents);
}
