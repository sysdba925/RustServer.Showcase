namespace RustServer.Auth.Tests.Infrastructure.Testing;

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues = new();

    public EnvironmentVariableScope(params (string Name, string Value)[] values)
    {
        foreach ((string name, string value) in values)
        {
            _originalValues[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach ((string name, string? value) in _originalValues)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
