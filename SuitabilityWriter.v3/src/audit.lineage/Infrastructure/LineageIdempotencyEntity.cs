using Azure;
using Azure.Data.Tables;

public sealed class LineageIdempotencyEntity : ITableEntity
{
    // Required by Tables
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Useful fields
    public string CaseId { get; set; } = default!;
    public string EventId { get; set; } = default!;
    public string Status { get; set; } = "Pending";   // Pending | InProgress | Completed
    public string? LockOwner { get; set; }
    public DateTimeOffset? LockAcquiredUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}
