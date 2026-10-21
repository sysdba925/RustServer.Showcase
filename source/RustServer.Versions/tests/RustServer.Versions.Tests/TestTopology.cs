using System.Runtime.CompilerServices;

namespace RustServer.Versions.Tests;

internal static class TestTopology
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("RUSTSERVER_VERSIONS_URL", "http://127.0.0.1:5113");
        Environment.SetEnvironmentVariable("RUSTSERVER_VERSIONS_BIND_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("RUSTSERVER_RCON_GRPC_URL", "http://127.0.0.1:50051");
    }
}
