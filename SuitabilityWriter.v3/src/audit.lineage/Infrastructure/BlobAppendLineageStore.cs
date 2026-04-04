using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;

public sealed class BlobAppendLineageStore : ILineageWriter
{
    private readonly BlobContainerClient _container;
    private readonly IIdempotencyStore _idem;

    public BlobAppendLineageStore(BlobServiceClient blobServiceClient, IIdempotencyStore idem)
    {
        _container = blobServiceClient.GetBlobContainerClient("audit-lineage");
        _container.CreateIfNotExists();
        _idem = idem;
    }

    public async Task AppendAsync(LineageRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.EventId))
            throw new InvalidOperationException("LineageRecord.EventId must be set for idempotency.");

        var acquired = await _idem.TryAcquireAsync(
            record.CaseId,
            record.EventId,
            lockTtl: TimeSpan.FromMinutes(10),
            ct: CancellationToken.None);

        if (!acquired)
            return;

        var blobName = $"{record.CaseId}.jsonl";
        var appendBlob = _container.GetAppendBlobClient(blobName);

        try
        {
            if (!await appendBlob.ExistsAsync())
                await appendBlob.CreateAsync();

            var json = JsonSerializer.Serialize(record);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json + Environment.NewLine));
            await appendBlob.AppendBlockAsync(stream);

            await _idem.MarkCompletedAsync(record.CaseId, record.EventId, CancellationToken.None);
        }
        catch
        {
            throw;
        }
    }
}