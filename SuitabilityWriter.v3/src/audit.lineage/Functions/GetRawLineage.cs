using System.Net;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public sealed class GetRawLineage
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<GetRawLineage> _logger;

    public GetRawLineage(
        BlobServiceClient blobServiceClient,
        ILogger<GetRawLineage> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    [Function("GetRawLineage")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "lineage/raw")]
        HttpRequestData req)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var caseId = query["caseId"];

            if (string.IsNullOrWhiteSpace(caseId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new
                {
                    status = "BAD_REQUEST",
                    message = "Query parameter 'caseId' is required.",
                    correlationId
                });
                return bad;
            }

            var rawContent = await LoadRawJsonlAsync(caseId);
            if (rawContent is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new
                {
                    status = "NOT_FOUND",
                    message = $"No lineage found for caseId '{caseId}'.",
                    correlationId
                });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/jsonl");
            response.Headers.Add("X-Correlation-Id", correlationId);
            await response.WriteStringAsync(rawContent, Encoding.UTF8);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Raw lineage retrieval failed. CorrelationId={CorrelationId}",
                correlationId);

            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new
            {
                status = "ERROR",
                message = "Raw lineage retrieval failed.",
                correlationId
            });
            return err;
        }
    }

    private async Task<string?> LoadRawJsonlAsync(string caseId)
    {
        var containerName = Environment.GetEnvironmentVariable("LINEAGE_CONTAINER_NAME") ?? "audit-lineage";
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobName = $"{caseId}.jsonl";
        var blob = container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync())
        {
            return null;
        }

        var download = await blob.DownloadStreamingAsync();
        using var stream = download.Value.Content;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}