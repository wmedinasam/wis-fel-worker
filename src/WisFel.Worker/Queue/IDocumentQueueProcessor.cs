namespace WisFel.Worker.Queue;

public interface IDocumentQueueProcessor
{
    /// <summary>One pass over the queue. Returns how many documents got their request JSON stored.</summary>
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default);
}
