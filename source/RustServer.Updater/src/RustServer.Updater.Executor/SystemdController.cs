using System.ComponentModel;
using System.Diagnostics;

namespace RustServer.Updater.Executor;

/// <summary>Permitted ways to start a systemd service from the executor.</summary>
public enum SystemdStartMode
{
    /// <summary>Normal application activation: systemd resolves dependencies.</summary>
    Normal,

    /// <summary>Only self-update and crash recovery may bypass dependencies.</summary>
    SelfUpdateOrRecovery
}

/// <summary>Maps executor operation context to the only permitted systemd start mode.</summary>
public static class DeploymentStartPolicy
{
    /// <summary>
    /// Selects the dependency bypass only for a paired self-update or a durable
    /// recovery. All ordinary install, update, and rollback activation remains
    /// subject to normal systemd dependency resolution.
    /// </summary>
    public static SystemdStartMode For(bool isPairedSelfUpdate, bool isRecovery) =>
        isPairedSelfUpdate || isRecovery
            ? SystemdStartMode.SelfUpdateOrRecovery
            : SystemdStartMode.Normal;
}

/// <summary>Minimal systemd boundary so policy can be tested without executing commands.</summary>
public interface ISystemdController
{
    /// <summary>Stops a registered service.</summary>
    Task StopAsync(string unitName, CancellationToken cancellationToken);

    /// <summary>Starts a registered service using the supplied permitted mode.</summary>
    Task StartAsync(string unitName, SystemdStartMode mode, CancellationToken cancellationToken);

    /// <summary>Enables a verified application's unit for automatic start on future boots.</summary>
    Task EnableAsync(string unitName, CancellationToken cancellationToken);

    /// <summary>Disables an auxiliary unit when its first application activation rolls back.</summary>
    Task DisableAsync(string unitName, CancellationToken cancellationToken);

    /// <summary>Reloads systemd after installed service unit files change.</summary>
    Task ReloadAsync(CancellationToken cancellationToken);
}

/// <summary>Runs a fixed, argument-list based subset of systemctl.</summary>
public sealed class SystemdController : ISystemdController
{
    private readonly string _executablePath;
    private readonly IReadOnlyList<string> _prefixArguments;

    /// <summary>Creates the production controller for the fixed systemctl binary.</summary>
    public SystemdController()
        : this("systemctl", [])
    {
    }

    // The process-backed integration tests inject an isolated command shim.
    // Runtime construction is intentionally limited to the parameterless
    // production controller above.
    internal SystemdController(string executablePath, IReadOnlyList<string> prefixArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(prefixArguments);
        if (prefixArguments.Any(argument => argument is null))
        {
            throw new ArgumentException("Command prefix arguments cannot contain null values.", nameof(prefixArguments));
        }

        _executablePath = executablePath;
        _prefixArguments = prefixArguments.ToArray();
    }

    /// <inheritdoc />
    public Task StopAsync(string unitName, CancellationToken cancellationToken) =>
        RunAsync(["stop", ValidateUnitName(unitName)], cancellationToken);

    /// <inheritdoc />
    public Task StartAsync(string unitName, SystemdStartMode mode, CancellationToken cancellationToken)
    {
        return RunAsync(GetStartArguments(unitName, mode), cancellationToken);
    }

    /// <inheritdoc />
    public Task EnableAsync(string unitName, CancellationToken cancellationToken) =>
        RunAsync(["enable", ValidateUnitName(unitName)], cancellationToken);

    /// <inheritdoc />
    public Task DisableAsync(string unitName, CancellationToken cancellationToken) =>
        RunAsync(["disable", ValidateUnitName(unitName)], cancellationToken);

    /// <inheritdoc />
    public Task ReloadAsync(CancellationToken cancellationToken) =>
        RunAsync(["daemon-reload"], cancellationToken);

    /// <summary>
    /// Builds the fixed start invocation. Keeping this policy visible makes it
    /// impossible for ordinary deployment code to silently acquire the bypass.
    /// </summary>
    public static IReadOnlyList<string> GetStartArguments(string unitName, SystemdStartMode mode) => mode switch
    {
        SystemdStartMode.Normal => ["start", ValidateUnitName(unitName)],
        SystemdStartMode.SelfUpdateOrRecovery => ["start", "--ignore-dependencies", ValidateUnitName(unitName)],
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Systemd start mode is invalid.")
    };

    private async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = CreateStartInfo(arguments)
            };
            if (!process.Start())
            {
                throw new DeploymentExecutionException("systemctl could not be started.");
            }

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                // systemctl exit status 5 means the unit was not found. An initial
                // installation may not have created it before rollback/recovery.
                if (arguments[0] == "stop" && process.ExitCode == 5)
                {
                    return;
                }

                throw new DeploymentExecutionException($"systemctl {arguments[0]} failed with exit code {process.ExitCode}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception)
        {
            throw new DeploymentExecutionException("systemctl is unavailable.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new DeploymentExecutionException("systemctl could not be executed.", exception);
        }
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in _prefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string ValidateUnitName(string unitName)
    {
        if (string.IsNullOrWhiteSpace(unitName)
            || !unitName.EndsWith(".service", StringComparison.Ordinal)
            || unitName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' )))
        {
            throw new ArgumentException("A simple .service unit name is required.", nameof(unitName));
        }

        return unitName;
    }
}

/// <summary>Raised when the fixed-function executor cannot complete a deployment.</summary>
public sealed class DeploymentExecutionException : Exception
{
    /// <summary>Creates an executor failure.</summary>
    public DeploymentExecutionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
