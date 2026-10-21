using Microsoft.Extensions.Options;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Api.Applications;

/// <summary>Accepts activation requests and delegates side effects to the independent privileged executor.</summary>
public sealed class DeploymentApplicationHandler(
    IApplicationRegistry applicationRegistry,
    IInstalledVersionProvider installedVersionProvider,
    IPreparedPackageLocator preparedPackageLocator,
    IOperationManager operationManager,
    IBackgroundOperationQueue backgroundOperationQueue,
    IDeploymentExecutorClient deploymentExecutorClient,
    IOptions<ApplicationRegistryOptions> registryOptions)
{
    /// <summary>Accepts an initial installation of an already verified package.</summary>
    public Task<Operation> InstallAsync(
        string applicationName,
        DeploymentApplicationRequest request,
        CancellationToken cancellationToken) =>
        AcceptActivationAsync(applicationName, request, OperationType.Install, requireNotInstalled: true, cancellationToken);

    /// <summary>Accepts an update of an installed application to an already verified package.</summary>
    public Task<Operation> UpdateAsync(
        string applicationName,
        DeploymentApplicationRequest request,
        CancellationToken cancellationToken) =>
        AcceptActivationAsync(applicationName, request, OperationType.Update, requireNotInstalled: false, cancellationToken);

    /// <summary>
    /// Creates an update operation whose package preparation will run as part of the same operation.
    /// The caller must verify the staged package before invoking <see cref="ExecuteDeploymentAsync"/>.
    /// </summary>
    internal Task<DeploymentOperationContext> CreateEmergencyUpdateAsync(
        string applicationName,
        string version,
        CancellationToken cancellationToken) =>
        CreateActivationAsync(
            applicationName,
            version,
            OperationType.Update,
            requireNotInstalled: false,
            requirePreparedPackage: false,
            cancellationToken);

    /// <summary>Confirms that a package remains verified immediately before it is activated.</summary>
    internal Task EnsurePreparedPackageAsync(
        ApplicationRegistration application,
        string version,
        CancellationToken cancellationToken) =>
        preparedPackageLocator.GetAsync(application, version, cancellationToken);

    /// <summary>Runs the executor portion of an accepted deployment without changing the operation lifecycle.</summary>
    internal async Task ExecuteDeploymentAsync(
        DeploymentOperationContext context,
        CancellationToken cancellationToken)
    {
        await operationManager.UpdateStepAsync(context.Operation.Id, "Starting executor", cancellationToken);
        // The executor runs in its own transient systemd unit. It must not inherit the
        // Updater host stopping token, especially when updating this application.
        var transaction = await deploymentExecutorClient.ExecuteAsync(
            new DeploymentExecutorRequest(
                context.Operation.Id,
                context.Application.Name,
                context.Operation.Type,
                context.TargetVersion),
            CancellationToken.None);
        await PersistExecutorOutcomeAsync(context.Operation.Id, transaction, cancellationToken);
    }

    /// <summary>Accepts a rollback to the previous successful or explicitly selected installed release.</summary>
    public async Task<Operation> RollbackAsync(
        string applicationName,
        RollbackApplicationRequest? request,
        CancellationToken cancellationToken)
    {
        applicationRegistry.EnsureConfigurationIsCurrent();
        var application = GetDeployableApplication(applicationName);
        var installedVersion = installedVersionProvider.GetInstalledVersion(application);
        if (installedVersion is null)
        {
            throw new DeploymentActivationException("An installed release is required before rollback can be requested.");
        }

        var targetVersion = string.IsNullOrWhiteSpace(request?.Version) ? null : request.Version.Trim();
        var operation = await operationManager.CreateAsync(
            new CreateOperationRequest(application.Name, OperationType.Rollback, targetVersion, installedVersion, "Accepted"),
            cancellationToken);
        Enqueue(new DeploymentOperationContext(operation, application, targetVersion));
        return operation;
    }

    private async Task<Operation> AcceptActivationAsync(
        string applicationName,
        DeploymentApplicationRequest request,
        OperationType requestedType,
        bool requireNotInstalled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = await CreateActivationAsync(
            applicationName,
            request.Version,
            requestedType,
            requireNotInstalled,
            requirePreparedPackage: true,
            cancellationToken);
        Enqueue(context);
        return context.Operation;
    }

    private async Task<DeploymentOperationContext> CreateActivationAsync(
        string applicationName,
        string? version,
        OperationType requestedType,
        bool requireNotInstalled,
        bool requirePreparedPackage,
        CancellationToken cancellationToken)
    {
        applicationRegistry.EnsureConfigurationIsCurrent();
        var application = GetDeployableApplication(applicationName);
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new PreparedPackageNotFoundException("A concrete release version is required.");
        }

        var targetVersion = version.Trim();
        if (requirePreparedPackage)
        {
            // Direct Install and Update endpoints never hide a download behind activation.
            await EnsurePreparedPackageAsync(application, targetVersion, cancellationToken);
        }

        var installedVersion = installedVersionProvider.GetInstalledVersion(application);
        if (requireNotInstalled && installedVersion is not null)
        {
            throw new DeploymentActivationException("The application is already installed; use update instead.");
        }

        if (!requireNotInstalled && installedVersion is null)
        {
            throw new DeploymentActivationException("The application is not installed; use install instead.");
        }

        var operationType = string.Equals(application.Name, registryOptions.Value.UpdaterApplicationName, StringComparison.OrdinalIgnoreCase)
            ? OperationType.SelfUpdate
            : requestedType;
        var operation = await operationManager.CreateAsync(
            new CreateOperationRequest(application.Name, operationType, targetVersion, installedVersion, "Accepted"),
            cancellationToken);
        return new DeploymentOperationContext(operation, application, targetVersion);
    }

    private ApplicationRegistration GetDeployableApplication(string applicationName)
    {
        var application = applicationRegistry.GetByName(applicationName)
            ?? throw new ApplicationRegistrationNotFoundException(applicationName);
        if (application.Readiness is null)
        {
            throw new DeploymentActivationException("The application registration must define a readiness check before it can be deployed.");
        }

        return application;
    }

    private void Enqueue(DeploymentOperationContext context)
    {
        try
        {
            backgroundOperationQueue.Enqueue(async cancellationToken =>
            {
                try
                {
                    await operationManager.StartAsync(context.Operation.Id, cancellationToken);
                    await ExecuteDeploymentAsync(context, cancellationToken);
                    await operationManager.CompleteAsync(context.Operation.Id, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The root executor has an independent transaction record. Startup recovery
                    // will reconcile it rather than assuming a cancellation means failure.
                }
                catch (Exception exception)
                {
                    await TryFailAsync(context.Operation.Id, exception.Message);
                }
            });
        }
        catch
        {
            _ = TryFailAsync(context.Operation.Id, "Deployment could not be queued.");
            throw;
        }
    }

    private async Task PersistExecutorOutcomeAsync(
        Guid operationId,
        DeploymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        await operationManager.UpdateStepAsync(operationId, transaction.Step, cancellationToken);
        await operationManager.SetDeploymentResultAsync(operationId, ToResult(transaction), cancellationToken);
        if (transaction.Status == DeploymentTransactionStatus.Succeeded)
        {
            return;
        }

        throw new DeploymentActivationException(transaction.Error ?? "Deployment activation was rolled back by the executor.");
    }

    private async Task TryFailAsync(Guid operationId, string error)
    {
        try
        {
            await operationManager.FailAsync(operationId,
                string.IsNullOrWhiteSpace(error) ? "Deployment failed without an error message." : error,
                CancellationToken.None);
        }
        catch (OperationStateTransitionException)
        {
            // An independent reconciliation run already recorded the terminal executor outcome.
        }
    }

    internal static DeploymentResult ToResult(DeploymentTransaction transaction) => new(
        transaction.PreviousVersion,
        transaction.TargetVersion,
        transaction.ActiveVersion,
        transaction.Status == DeploymentTransactionStatus.RolledBack,
        transaction.UpdatedAt,
        transaction.ProvisioningOutput,
        transaction.ProvisioningExitCode);
}

/// <summary>Accepted deployment data shared by normal activation and emergency orchestration.</summary>
internal sealed record DeploymentOperationContext(
    Operation Operation,
    ApplicationRegistration Application,
    string? TargetVersion);
