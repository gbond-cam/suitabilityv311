using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;

public sealed class AzureTableIdempotencyStore : IIdempotencyStore
{
    private readonly TableClient _table;
    private readonly string _lockOwnerId;

    public AzureTableIdempotencyStore(TableServiceClient svc, string tableName, string lockOwnerId)
    {
        _table = svc.GetTableClient(tableName);
        _table.CreateIfNotExists();
        _lockOwnerId = lockOwnerId;
    }

    public async Task<bool> TryAcquireAsync(string caseId, string eventId, TimeSpan lockTtl, CancellationToken ct)
    {
        var pk = PartitionKeyFor(caseId);
        var rk = eventId; // eventId should be safe (hex); do not use raw strings with / \ # ?

        // First attempt: create new marker
        var now = DateTimeOffset.UtcNow;
        var entity = new LineageIdempotencyEntity
        {
            PartitionKey = pk,
            RowKey = rk,
            CaseId = caseId,
            EventId = eventId,
            Status = "InProgress",
            LockOwner = _lockOwnerId,
            LockAcquiredUtc = now,
            CreatedUtc = now
        };

        try
        {
            await _table.AddEntityAsync(entity, ct);
            return true; // we own it
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Already exists. Decide if we can proceed.
        }

        // Exists: read it
        var existing = await _table.GetEntityAsync<LineageIdempotencyEntity>(pk, rk, cancellationToken: ct);
        var e = existing.Value;

        if (e.Status == "Completed")
            return false; // duplicate

        // If in progress and not stale, another worker owns it
        if (e.Status == "InProgress" && e.LockAcquiredUtc.HasValue &&
            (now - e.LockAcquiredUtc.Value) <= lockTtl)
        {
            return false;
        }

        // Pending or stale InProgress: try to acquire using optimistic concurrency (ETag)
        e.Status = "InProgress";
        e.LockOwner = _lockOwnerId;
        e.LockAcquiredUtc = now;

        try
        {
            await _table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            // Someone else updated it first
            return false;
        }
    }

    public async Task MarkCompletedAsync(string caseId, string eventId, CancellationToken ct)
    {
        var pk = PartitionKeyFor(caseId);
        var rk = eventId;

        var existing = await _table.GetEntityAsync<LineageIdempotencyEntity>(pk, rk, cancellationToken: ct);
        var e = existing.Value;

        // If already completed, idempotent
        if (e.Status == "Completed")
            return;

        e.Status = "Completed";
        e.CompletedUtc = DateTimeOffset.UtcNow;

        // Update with ETag to avoid clobbering a concurrent lock takeover
        try
        {
            await _table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            // Re-read and try once more (rare race)
            var latest = await _table.GetEntityAsync<LineageIdempotencyEntity>(pk, rk, cancellationToken: ct);
            var l = latest.Value;
            if (l.Status != "Completed")
            {
                l.Status = "Completed";
                l.CompletedUtc = DateTimeOffset.UtcNow;
                await _table.UpdateEntityAsync(l, l.ETag, TableUpdateMode.Replace, ct);
            }
        }
    }

    private static string PartitionKeyFor(string caseId)
    {
        // Case IDs may contain characters unsuitable for PK; use stable hash.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(caseId));
        // Keep it short but stable
        var hex = Convert.ToHexString(bytes);
        return $"case-{hex[..32]}";
    }
}