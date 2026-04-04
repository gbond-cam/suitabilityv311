using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

// DTOs (the typed reconstruction models you already added)
 // CaseReconstructionDto
 // ReconstructionEventDto
 // ArtefactDto
 // GovernanceDto
 // MetricsDto

public sealed class ReconstructCaseLineage
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<ReconstructCaseLineage> _logger;

    public ReconstructCaseLineage(BlobServiceClient blobServiceClient, ILogger<ReconstructCaseLineage> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    [Function("ReconstructCaseLineage")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "lineage/reconstruct")]
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

            var records = await LoadRecordsAsync(caseId);

            if (records is null)
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

            // Order chronologically
            var ordered = records
                .OrderBy(r => r.TimestampUtc)
                .Select(r => new ReconstructionEventDto(
                    EventId: r.EventId,
                    TimestampUtc: r.TimestampUtc,
                    Stage: r.Stage,
                    Action: r.Action,
                    PerformedBy: r.PerformedBy,
                    Artefact: (r.ArtefactName == "N/A" || r.ArtefactName == "NA")
                        ? ArtefactDto.NotApplicable
                        : new ArtefactDto(r.ArtefactName, r.ArtefactVersion, r.ArtefactHash),
                    Metadata: ToJsonElement(r.Metadata)
                ))
                .ToList();

            // Governance summary (derived strictly from recorded facts)
            var blockedEvent = ordered.FirstOrDefault(e => e.Action == "Blocked");
            string? blockReason = null;
            if (blockedEvent is not null && blockedEvent.Metadata.ValueKind == JsonValueKind.Object &&
                blockedEvent.Metadata.TryGetProperty("reasonCode", out var reasonProp))
            {
                blockReason = reasonProp.GetString();
            }

            var overrideEvent = ordered.FirstOrDefault(e => e.Action == "EmergencyOverrideApplied");
            string? incidentId = null;
            if (overrideEvent is not null && overrideEvent.Metadata.ValueKind == JsonValueKind.Object &&
                overrideEvent.Metadata.TryGetProperty("incidentId", out var incProp))
            {
                incidentId = incProp.GetString();
            }

            var adviserApprovedEvent = ordered.FirstOrDefault(e => e.Action == "AdviserApproved");
            var deliveredEvent = ordered.FirstOrDefault(e => e.Action == "Delivered");

            var governance = new GovernanceDto(
                IsBlocked: blockedEvent is not null,
                BlockReasonCode: blockReason,
                EmergencyOverrideApplied: overrideEvent is not null,
                IncidentId: incidentId,
                AdviserApproved: adviserApprovedEvent is not null,
                AdviserId: adviserApprovedEvent?.PerformedBy,
                DeliveredAtUtc: deliveredEvent?.TimestampUtc
            );

            // Metrics (derived strictly from timestamps)
            string? timeToDelivery = null;
            string? timeToBlock = null;

            if (ordered.Count > 0)
            {
                var first = ordered[0].TimestampUtc;

                if (governance.DeliveredAtUtc.HasValue)
                    timeToDelivery = ToIso8601Duration(governance.DeliveredAtUtc.Value - first);

                if (governance.IsBlocked && blockedEvent is not null)
                    timeToBlock = ToIso8601Duration(blockedEvent.TimestampUtc - first);
            }

            var metrics = new MetricsDto(
                TotalEvents: ordered.Count,
                TimeToDelivery: timeToDelivery,
                TimeToBlock: timeToBlock
            );

            var dto = new CaseReconstructionDto(
                CaseId: caseId!,
                Timeline: ordered,
                Governance: governance,
                Metrics: metrics
            );

            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(dto);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconstruction failed. CorrelationId={CorrelationId}", correlationId);

            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new
            {
                status = "ERROR",
                message = "Reconstruction failed.",
                correlationId
            });
            return err;
        }
    }

    private async Task<List<LineageRecord>?> LoadRecordsAsync(string caseId)
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

        var records = new List<LineageRecord>();
        string? line;

        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var record = JsonSerializer.Deserialize<LineageRecord>(line, JsonOpts);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    private static JsonElement ToJsonElement(object? metadata)
    {
        if (metadata is null)
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        return metadata is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(metadata);
    }

    private static string ToIso8601Duration(TimeSpan duration)
    {
        return XmlConvert.ToString(duration);
    }
}