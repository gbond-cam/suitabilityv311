using System.Collections.Concurrent;

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly string _lockOwnerId;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _completed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Owner, DateTimeOffset AcquiredUtc)> _inProgress = new(StringComparer.Ordinal);

    public InMemoryIdempotencyStore(string lockOwnerId)
    {
        _lockOwnerId = lockOwnerId;
    }

    public Task<bool> TryAcquireAsync(string caseId, string eventId, TimeSpan lockTtl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var key = BuildKey(caseId, eventId);
        if (_completed.ContainsKey(key))
        {
            return Task.FromResult(false);
        }

        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            if (!_inProgress.TryGetValue(key, out var existing))
            {
                if (_inProgress.TryAdd(key, (_lockOwnerId, now)))
                {
                    return Task.FromResult(true);
                }

                continue;
            }

            if ((now - existing.AcquiredUtc) <= lockTtl)
            {
                return Task.FromResult(false);
            }

            if (_inProgress.TryUpdate(key, (_lockOwnerId, now), existing))
            {
                return Task.FromResult(true);
            }
        }
    }

    public Task MarkCompletedAsync(string caseId, string eventId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var key = BuildKey(caseId, eventId);
        _completed[key] = DateTimeOffset.UtcNow;
        _inProgress.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static string BuildKey(string caseId, string eventId) => $"{caseId}::{eventId}";
}
