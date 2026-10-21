using System.Runtime.CompilerServices;

namespace RustServer.Snapshots.Tests;

internal static class TestTopology
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("RUSTSERVER_CONTROL_URL", "http://127.0.0.1:5112");
        Environment.SetEnvironmentVariable("RUSTSERVER_SNAPSHOTS_URL", "http://127.0.0.1:5115");
        Environment.SetEnvironmentVariable("RUSTSERVER_SNAPSHOTS_BIND_HOST", "127.0.0.1");
    }
}
