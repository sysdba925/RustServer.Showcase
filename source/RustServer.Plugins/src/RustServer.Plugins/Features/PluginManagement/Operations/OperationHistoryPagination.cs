namespace RustServer.Plugins.Features.PluginManagement.Operations;

/** Published pagination bounds for compact persistent operation history. */
public static class OperationHistoryPagination
{
    public const int DefaultLimit = 20;
    public const int MinimumLimit = 1;
    public const int MaximumLimit = 200;

    public static bool IsValidLimit(int limit) =>
        limit is >= MinimumLimit and <= MaximumLimit;
}
