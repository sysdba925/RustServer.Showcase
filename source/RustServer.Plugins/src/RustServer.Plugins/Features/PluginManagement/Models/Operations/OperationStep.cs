namespace RustServer.Plugins.Features.PluginManagement.Models.Operations;

public enum OperationStepStatus
{
    Completed,
    Failed,
    NotCompleted
}

public sealed record OperationStep
{
    public OperationStep(string name, OperationStepStatus status)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Operation step name must contain text.", nameof(name));
        }

        Name = name;
        Status = status;
    }

    public string Name { get; }

    public OperationStepStatus Status { get; }
}
