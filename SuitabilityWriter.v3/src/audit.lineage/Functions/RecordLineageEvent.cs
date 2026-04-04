using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Net;
using System.Text.Json;

public sealed class RecordLineageEvent
{
    private readonly ILineageWriter _writer;
    private readonly ILogger<RecordLineageEvent> _logger;

    public RecordLineageEvent(
        ILineageWriter writer,
        ILogger<RecordLineageEvent> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    [Function("RecordLineageEvent")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")]
        HttpRequestData req)
    {
        var record = await req.ReadFromJsonAsync<LineageRecord>();

        if (record is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            return bad;
        }

        if (string.IsNullOrWhiteSpace(record.EventId))
        {
            var headerEventId = req.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.FirstOrDefault()
                : null;

            record = record with { EventId = headerEventId ?? IdempotencyKey.From(record) };
        }

        if (string.IsNullOrWhiteSpace(record.ArtefactHash))
        {
            string? artefactPath = null;

            if (record.Metadata is JsonElement metadataElement &&
                metadataElement.ValueKind == JsonValueKind.Object &&
                metadataElement.TryGetProperty("artefactPath", out var artefactPathProperty) &&
                artefactPathProperty.ValueKind == JsonValueKind.String)
            {
                artefactPath = artefactPathProperty.GetString();
            }
            else if (record.Metadata is IDictionary<string, object> metadata &&
                     metadata.TryGetValue("artefactPath", out var artefactPathValue))
            {
                artefactPath = artefactPathValue?.ToString();
            }

            if (!string.IsNullOrWhiteSpace(artefactPath) && File.Exists(artefactPath))
            {
                var bytes = File.ReadAllBytes(artefactPath);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                record = record with { ArtefactHash = hash };
            }
        }

        await _writer.AppendAsync(record);

        var ok = req.CreateResponse(HttpStatusCode.Accepted);
        return ok;
    }
}
