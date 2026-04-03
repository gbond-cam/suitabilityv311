using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;

public sealed class BlobAppendLineageStore : ILineageWriter
{
    private readonly BlobContainerClient _container;

    public BlobAppendLineageStore(BlobServiceClient blobServiceClient)
    {
        _container = blobServiceClient.GetBlobContainerClient("audit-lineage");
        _container.CreateIfNotExists();
    }

    public async Task AppendAsync(LineageRecord record)
    {
        // One blob per case = clean reconstruction
        var blobName = $"{record.CaseId}.jsonl";

        AppendBlobClient appendBlob =
            _container.GetAppendBlobClient(blobName);

        if (!await appendBlob.ExistsAsync())
        {
            await appendBlob.CreateAsync();
        }

        var json = JsonSerializer.Serialize(record);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json + Environment.NewLine));
        await appendBlob.AppendBlockAsync(stream);
    }
}