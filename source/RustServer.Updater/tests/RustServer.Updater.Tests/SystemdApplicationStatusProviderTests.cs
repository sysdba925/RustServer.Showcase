using RustServer.Updater.Api.Applications;

namespace RustServer.Updater.Tests;

public sealed class SystemdApplicationStatusProviderTests
{
    [Theory]
    [InlineData(SystemdServiceActivity.Active, ApplicationStatus.Running)]
    [InlineData(SystemdServiceActivity.Inactive, ApplicationStatus.Stopped)]
    [InlineData(SystemdServiceActivity.Unknown, ApplicationStatus.Unknown)]
    public async Task GetStatusAsync_MapsSystemdActivity(SystemdServiceActivity activity, ApplicationStatus expectedStatus)
    {
        var reader = new StubSystemdServiceStatusReader(activity);
        var provider = new SystemdApplicationStatusProvider(reader);

        var status = await provider.GetStatusAsync(Application, isInstalled: true, CancellationToken.None);

        Assert.Equal(expectedStatus, status);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsNotInstalledWithoutCallingSystemd()
    {
        var reader = new StubSystemdServiceStatusReader(SystemdServiceActivity.Active);
        var provider = new SystemdApplicationStatusProvider(reader);

        var status = await provider.GetStatusAsync(Application, isInstalled: false, CancellationToken.None);

        Assert.Equal(ApplicationStatus.NotInstalled, status);
        Assert.Equal(0, reader.CallCount);
    }

    private static readonly ApplicationRegistration Application = new(
        "RustServer.Updater",
        "sysdba925/RustServer.Updater",
        "/opt/rustserver/updater",
        "rustserver-updater.service",
        "RustServer.Updater.Api.dll");

    private sealed class StubSystemdServiceStatusReader(SystemdServiceActivity activity) : ISystemdServiceStatusReader
    {
        public int CallCount { get; private set; }

        public Task<SystemdServiceActivity> GetActivityAsync(string serviceName, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(activity);
        }
    }
}
