namespace RustServer.Plugins.Features.PluginManagement.Guards;

public sealed class ExclusiveMutationGuard : IExclusiveMutationGuard
{
    private int isAcquired;

    public IExclusiveMutationLease? TryAcquire()
    {
        return Interlocked.CompareExchange(ref isAcquired, 1, 0) == 0
            ? new Lease(this)
            : null;
    }

    private void Release()
    {
        Volatile.Write(ref isAcquired, 0);
    }

    private sealed class Lease(ExclusiveMutationGuard owner) : IExclusiveMutationLease
    {
        private ExclusiveMutationGuard? guard = owner;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref guard, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
