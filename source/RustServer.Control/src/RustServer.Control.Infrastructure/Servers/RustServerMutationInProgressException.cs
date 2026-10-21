namespace RustServer.Control.Infrastructure.Servers;

public sealed class RustServerMutationInProgressException : Exception
{
    public RustServerMutationInProgressException()
        : base("A server-mutating update is pending or running.")
    {
    }
}
