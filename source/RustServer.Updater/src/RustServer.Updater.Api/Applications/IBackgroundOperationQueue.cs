namespace RustServer.Updater.Api.Applications;

/// <summary>Queues accepted work so it is independent from the originating HTTP request.</summary>
public interface IBackgroundOperationQueue
{
    /// <summary>Adds durable operation work to the in-process worker queue.</summary>
    void Enqueue(Func<CancellationToken, Task> work);
}
