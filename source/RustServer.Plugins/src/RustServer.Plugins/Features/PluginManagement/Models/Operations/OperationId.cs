namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public sealed record OperationId
{
    public OperationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Operation ID must not be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static OperationId New() => new(Guid.NewGuid());
}
