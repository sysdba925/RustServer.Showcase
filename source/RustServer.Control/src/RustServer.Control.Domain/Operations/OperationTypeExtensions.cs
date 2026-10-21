namespace RustServer.Control.Domain.Operations;

public static class OperationTypeExtensions
{
    public static bool IsServerMutation(this OperationType operationType) =>
        operationType is OperationType.RustUpdate or OperationType.OxideUpdate or OperationType.Wipe;
}
