using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public sealed class PublishRevocationList
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<PublishRevocationList> _logger;

    public PublishRevocationList(
        BlobServiceClient blobServiceClient,
        ILogger<PublishRevocationList> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    [Function("PublishRevocationList")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "keys/revocation-list/publish")]
        HttpRequestData req)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            var document = BuildRevocationList();
            var json = JsonSerializer.Serialize(document, JsonOpts);
            var payload = Encoding.UTF8.GetBytes(json);

            var containerName = Environment.GetEnvironmentVariable("KEY_REVOCATION_CONTAINER_NAME") ?? "governance-public";
            var blobName = Environment.GetEnvironmentVariable("KEY_REVOCATION_BLOB_NAME") ?? "revocation-list.json";

            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: CancellationToken.None);

            var blob = container.GetBlobClient(blobName);
            await using (var stream = new MemoryStream(payload))
            {
                await blob.UploadAsync(
                    stream,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders
                        {
                            ContentType = "application/json; charset=utf-8",
                            CacheControl = "no-store, no-cache"
                        }
                    },
                    cancellationToken: CancellationToken.None);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("X-Correlation-Id", correlationId);

            await response.WriteAsJsonAsync(new
            {
                status = "PUBLISHED",
                correlationId,
                publishedAtUtc = DateTimeOffset.UtcNow,
                issuer = Environment.GetEnvironmentVariable("SIGNING_ISSUER") ?? "Consilium Asset Management",
                container = containerName,
                blob = blobName,
                blobUri = blob.Uri.ToString(),
                sha256 = Convert.ToHexString(SHA256.HashData(payload)),
                document
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Key revocation list publish failed. CorrelationId={CorrelationId}",
                correlationId);

            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            err.Headers.Add("X-Correlation-Id", correlationId);
            await err.WriteAsJsonAsync(new
            {
                status = "ERROR",
                message = "Key revocation list publish failed.",
                correlationId
            });
            return err;
        }
    }

    private static object BuildRevocationList()
    {
        var issuer = Environment.GetEnvironmentVariable("SIGNING_ISSUER") ?? "Consilium Asset Management";
        var activeKeyId = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_NAME") ?? "zip-signing-key-v2";
        var activeThumbprint = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_THUMBPRINT") ?? "B47A11...";
        var revokedKeyId = Environment.GetEnvironmentVariable("ZIP_SIGNING_PREVIOUS_KEY_ID") ?? "zip-signing-key-v1";
        var revokedThumbprint = Environment.GetEnvironmentVariable("ZIP_SIGNING_PREVIOUS_KEY_THUMBPRINT") ?? "A9F3C2...";
        var revokedAtUtc = Environment.GetEnvironmentVariable("ZIP_SIGNING_PREVIOUS_KEY_REVOKED_AT_UTC") ?? "2026-03-18T09:12:00Z";
        var revokeReason = Environment.GetEnvironmentVariable("ZIP_SIGNING_PREVIOUS_KEY_REVOKE_REASON") ?? "Key rotation";

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            issuer,
            keys = new object[]
            {
                new
                {
                    keyId = revokedKeyId,
                    thumbprint = revokedThumbprint,
                    status = "Revoked",
                    revokedAtUtc,
                    reason = revokeReason
                },
                new
                {
                    keyId = activeKeyId,
                    thumbprint = activeThumbprint,
                    status = "Active"
                }
            }
        };
    }
}
