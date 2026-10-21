using System.Runtime.CompilerServices;

namespace RustServer.Plugins.Tests;

internal static class TestTopology
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("RUSTSERVER_CONTROL_URL", "http://127.0.0.1:5112");
        Environment.SetEnvironmentVariable("RUSTSERVER_PLUGINS_URL", "http://127.0.0.1:5114");
        Environment.SetEnvironmentVariable("RUSTSERVER_PLUGINS_BIND_HOST", "127.0.0.1");
    }
}
