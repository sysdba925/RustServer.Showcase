namespace RustServer.Plugins.Features.PluginManagement.Models;

public static class EnvironmentSourcePolicy
{
    public static bool IsAllowed(PluginEnvironment environment, Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return environment switch
        {
            PluginEnvironment.Staging => true,
            PluginEnvironment.Production => source.Kind is SourceKind.Release,
            _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unsupported environment.")
        };
    }
}
