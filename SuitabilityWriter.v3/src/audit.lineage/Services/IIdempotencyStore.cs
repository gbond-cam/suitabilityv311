public interface IIdempotencyStore
{
    /// <summary>
    /// Try to acquire the right to process this event exactly once.
    /// Returns true if caller may proceed to append; false if duplicate or already being processed.
    /// </summary>
    Task<bool> TryAcquireAsync(string caseId, string eventId, TimeSpan lockTtl, CancellationToken ct);

    /// <summary>
    /// Mark event as completed (append succeeded).
    /// </summary>
    Task MarkCompletedAsync(string caseId, string eventId, CancellationToken ct);
}