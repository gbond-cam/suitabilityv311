using System.Text.Json;
using Azure.Storage.Blobs;

public sealed class BlobLineageStore :
    ILineageWriter,
    ILineageReader
{
    private readonly BlobContainerClient _container;

    public BlobLineageStore(BlobServiceClient blobServiceClient)
    {
        _container = blobServiceClient.GetBlobContainerClient("audit-lineage");
        _container.CreateIfNotExists();
    }

    public Task AppendAsync(LineageRecord record)
    {
        // Writes are handled by BlobAppendLineageStore.
        return Task.CompletedTask;
    }

    public async Task<LineageEnvelope> GetByCaseIdAsync(string caseId)
    {
        var blobName = $"{caseId}.jsonl";
        var blobClient = _container.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
        {
            return new LineageEnvelope(caseId, Array.Empty<LineageRecord>());
        }

        var content = await blobClient.DownloadContentAsync();
        var lines = content.Value.Content.ToString()
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var records = new List<LineageRecord>();
        foreach (var line in lines)
        {
            var record = JsonSerializer.Deserialize<LineageRecord>(line);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return new LineageEnvelope(caseId, records);
    }
}