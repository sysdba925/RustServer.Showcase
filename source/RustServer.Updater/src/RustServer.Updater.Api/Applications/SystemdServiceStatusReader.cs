using System.ComponentModel;
using System.Diagnostics;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Reads service activity by invoking the fixed <c>systemctl is-active --quiet</c> command.
/// </summary>
public sealed class SystemdServiceStatusReader : ISystemdServiceStatusReader
{
    /// <inheritdoc />
    public async Task<SystemdServiceActivity> GetActivityAsync(string serviceName, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = CreateStartInfo(serviceName)
            };

            if (!process.Start())
            {
                return SystemdServiceActivity.Unknown;
            }

            await process.WaitForExitAsync(cancellationToken);

            // systemctl documents exit code 0 for active units and 3 for inactive units.
            return process.ExitCode switch
            {
                0 => SystemdServiceActivity.Active,
                3 => SystemdServiceActivity.Inactive,
                _ => SystemdServiceActivity.Unknown
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return SystemdServiceActivity.Unknown;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string serviceName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "systemctl",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("is-active");
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add(serviceName);
        return startInfo;
    }
}
