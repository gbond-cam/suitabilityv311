using System.Net;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public sealed class GetRevocationList
{
    private readonly BlobContainerClient _container;

    public GetRevocationList()
    {
        var storageConn = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                          ?? throw new InvalidOperationException("AzureWebJobsStorage missing");

        var containerName = Environment.GetEnvironmentVariable("REVOCATION_CONTAINER") ?? "key-revocation";
        _container = new BlobContainerClient(storageConn, containerName);
        _container.CreateIfNotExists();
    }

    [Function("GetRevocationList")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "revocation-list")]
        HttpRequestData req)
    {
        var blobName = Environment.GetEnvironmentVariable("REVOCATION_BLOB_NAME") ?? "revocation-list.json";
        var blob = _container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync())
            return req.CreateResponse(HttpStatusCode.NotFound);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/json");

        var dl = await blob.DownloadStreamingAsync();
        await dl.Value.Content.CopyToAsync(resp.Body);

        return resp;
    }
}