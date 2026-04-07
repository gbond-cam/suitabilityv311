using System.Net;
using System.Text.Json;
using System.Xml;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public sealed class GetComplianceAuditReport
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILineageWriter _lineageWriter;
    private readonly ImmutableAuditArchive _immutableAuditArchive;
    private readonly ILogger<GetComplianceAuditReport> _logger;

    public GetComplianceAuditReport(
        BlobServiceClient blobServiceClient,
        ILineageWriter lineageWriter,
        ImmutableAuditArchive immutableAuditArchive,
        ILogger<GetComplianceAuditReport> logger)
    {
        _blobServiceClient = blobServiceClient;
        _lineageWriter = lineageWriter;
        _immutableAuditArchive = immutableAuditArchive;
        _logger = logger;
    }

    [Function("GetComplianceAuditReport")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "lineage/compliance-report")]
        HttpRequestData req,
        CancellationToken cancellationToken)
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
                }, cancellationToken);
                bad.StatusCode = HttpStatusCode.BadRequest;
                return bad;
            }

            var rawText = await LoadRawJsonlAsync(caseId);
            if (rawText is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new
                {
                    status = "NOT_FOUND",
                    message = $"No lineage found for caseId '{caseId}'.",
                    correlationId
                }, cancellationToken);
                notFound.StatusCode = HttpStatusCode.NotFound;
                return notFound;
            }

            var records = DeserializeRecords(rawText);
            var reconstruction = BuildReconstruction(caseId!, records);
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var loggedActions = records
                .Select(record => record.Action)
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(action => action, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var provisionalReport = new ComplianceAuditReportDto(
                CaseId: caseId!,
                GeneratedAtUtc: generatedAtUtc,
                ComplianceStatus: "pending-archive",
                Summary: BuildSummary(records),
                LoggedActions: loggedActions,
                RegulatoryChecks: [],
                ImmutableStorage: new ImmutableStorageDescriptorDto("write-once-blob", string.Empty, string.Empty, string.Empty, generatedAtUtc),
                Reconstruction: reconstruction);

            var archiveResult = await _immutableAuditArchive.StoreComplianceReportAsync(caseId!, provisionalReport, cancellationToken);
            var regulatoryChecks = BuildChecks(records, archiveResult.Created || !string.IsNullOrWhiteSpace(archiveResult.BlobName));
            var immutableStorage = new ImmutableStorageDescriptorDto(
                Mode: "write-once-blob",
                ContainerName: archiveResult.ContainerName,
                BlobName: archiveResult.BlobName,
                ContentHashSha256: archiveResult.Sha256,
                RetentionUntilUtc: archiveResult.RetentionUntilUtc);

            var report = provisionalReport with
            {
                ComplianceStatus = regulatoryChecks.All(check => check.Passed)
                    ? "ready-for-regulatory-review"
                    : "attention-required",
                RegulatoryChecks = regulatoryChecks,
                ImmutableStorage = immutableStorage
            };

            var auditRecord = new LineageRecord(
                EventId: $"{caseId}-audit-report-{generatedAtUtc:yyyyMMddHHmmssfff}-{correlationId[..8]}",
                CaseId: caseId!,
                Stage: LineageStages.Audit,
                Action: LineageActions.AuditReportGenerated,
                ArtefactName: "ComplianceAuditReport",
                ArtefactVersion: "v1",
                ArtefactHash: archiveResult.Sha256,
                PerformedBy: "audit.lineage",
                TimestampUtc: generatedAtUtc,
                Metadata: new
                {
                    correlationId,
                    archiveResult.ContainerName,
                    archiveResult.BlobName,
                    archiveResult.RetentionUntilUtc,
                    report.ComplianceStatus
                });

            await _lineageWriter.AppendAsync(auditRecord);
            await _immutableAuditArchive.StoreLineageRecordAsync(auditRecord, cancellationToken);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("X-Correlation-Id", correlationId);
            await response.WriteAsJsonAsync(report, cancellationToken);
            response.StatusCode = HttpStatusCode.OK;
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compliance audit report generation failed. CorrelationId={CorrelationId}", correlationId);

            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new
            {
                status = "ERROR",
                message = "Compliance audit report generation failed.",
                correlationId
            }, cancellationToken);
            err.StatusCode = HttpStatusCode.InternalServerError;
            return err;
        }
    }

    private async Task<string?> LoadRawJsonlAsync(string caseId)
    {
        var containerName = Environment.GetEnvironmentVariable("LINEAGE_CONTAINER_NAME") ?? "audit-lineage";
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blob = container.GetBlobClient($"{caseId}.jsonl");

        if (!await blob.ExistsAsync())
        {
            return null;
        }

        var download = await blob.DownloadStreamingAsync();
        using var stream = download.Value.Content;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static List<LineageRecord> DeserializeRecords(string rawText)
    {
        return rawText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<LineageRecord>(line, JsonOpts))
            .Where(record => record is not null)
            .Cast<LineageRecord>()
            .ToList();
    }

    private static ComplianceAuditSummaryDto BuildSummary(IReadOnlyList<LineageRecord> records)
    {
        var actors = records
            .Select(record => string.IsNullOrWhiteSpace(record.PerformedBy) ? "system" : record.PerformedBy)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var artefacts = records
            .Where(record => !string.Equals(record.ArtefactName, "N/A", StringComparison.OrdinalIgnoreCase))
            .Select(record => $"{record.ArtefactName}@{record.ArtefactVersion}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(artefact => artefact, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ComplianceAuditSummaryDto(
            TotalEvents: records.Count,
            FirstEventAtUtc: records.Count == 0 ? null : records.Min(record => record.TimestampUtc),
            LastEventAtUtc: records.Count == 0 ? null : records.Max(record => record.TimestampUtc),
            Actors: actors,
            Artefacts: artefacts);
    }

    private static IReadOnlyList<ComplianceCheckResultDto> BuildChecks(IReadOnlyList<LineageRecord> records, bool immutableArchiveCreated)
    {
        var hasEvidenceLog = records.Any(record =>
            string.Equals(record.Action, LineageActions.EvidenceUploaded, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Action, "SchemaValidated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Stage, LineageStages.Validation, StringComparison.OrdinalIgnoreCase));

        var hasEvaluationLog = records.Any(record =>
            string.Equals(record.Action, LineageActions.SuitabilityEvaluationRequested, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Action, LineageActions.SuitabilityEvaluationCompleted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Stage, LineageStages.Routing, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Stage, LineageStages.Generation, StringComparison.OrdinalIgnoreCase));

        var hasReportLog = records.Any(record =>
            string.Equals(record.Action, LineageActions.ReportGenerationRequested, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Action, LineageActions.ReportGenerated, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.ArtefactName, "SuitabilityReport", StringComparison.OrdinalIgnoreCase));

        var chronological = IsChronologicallyOrdered(records);

        return new List<ComplianceCheckResultDto>
        {
            new("Evidence intake activity logged", hasEvidenceLog, hasEvidenceLog
                ? "Evidence upload and validation actions are present in the audit trail."
                : "Evidence upload activity is missing from the audit trail."),
            new("Suitability evaluation activity logged", hasEvaluationLog, hasEvaluationLog
                ? "Suitability workflow actions are present for regulatory review."
                : "Suitability evaluation activity is missing from the audit trail."),
            new("Report generation activity logged", hasReportLog, hasReportLog
                ? "Report generation and delivery events are present in the audit trail."
                : "Report generation activity is missing from the audit trail."),
            new("Timeline is chronologically ordered", chronological, chronological
                ? "The audit trail is ordered and reconstructable."
                : "Audit events are out of chronological order."),
            new("Immutable archive snapshot created", immutableArchiveCreated, immutableArchiveCreated
                ? "A write-once compliance snapshot was archived for FCA/SEC review."
                : "The compliance snapshot could not be archived immutably.")
        };
    }

    private static bool IsChronologicallyOrdered(IReadOnlyList<LineageRecord> records)
    {
        for (var index = 1; index < records.Count; index++)
        {
            if (records[index].TimestampUtc < records[index - 1].TimestampUtc)
            {
                return false;
            }
        }

        return true;
    }

    private static CaseReconstructionDto BuildReconstruction(string caseId, IReadOnlyList<LineageRecord> records)
    {
        var ordered = records
            .OrderBy(r => r.TimestampUtc)
            .Select(r => new ReconstructionEventDto(
                EventId: r.EventId,
                TimestampUtc: r.TimestampUtc,
                Stage: r.Stage,
                Action: r.Action,
                PerformedBy: r.PerformedBy,
                Artefact: string.Equals(r.ArtefactName, "N/A", StringComparison.OrdinalIgnoreCase)
                    ? ArtefactDto.NotApplicable
                    : new ArtefactDto(r.ArtefactName, r.ArtefactVersion, r.ArtefactHash),
                Metadata: ToJsonElement(r.Metadata)))
            .ToList();

        var blockedEvent = ordered.FirstOrDefault(e => string.Equals(e.Action, LineageActions.Blocked, StringComparison.OrdinalIgnoreCase));
        var overrideEvent = ordered.FirstOrDefault(e => string.Equals(e.Action, LineageActions.EmergencyOverrideApplied, StringComparison.OrdinalIgnoreCase));
        var adviserApprovedEvent = ordered.FirstOrDefault(e => string.Equals(e.Action, "AdviserApproved", StringComparison.OrdinalIgnoreCase));
        var deliveredEvent = ordered.FirstOrDefault(e => string.Equals(e.Action, "Delivered", StringComparison.OrdinalIgnoreCase));

        var governance = new GovernanceDto(
            IsBlocked: blockedEvent is not null,
            BlockReasonCode: blockedEvent is null ? null : GetMetadataString(blockedEvent.Metadata, "reasonCode", "blockReasonCode", "reason"),
            EmergencyOverrideApplied: overrideEvent is not null,
            IncidentId: overrideEvent is null ? null : GetMetadataString(overrideEvent.Metadata, "incidentId"),
            AdviserApproved: adviserApprovedEvent is not null,
            AdviserId: adviserApprovedEvent?.PerformedBy,
            DeliveredAtUtc: deliveredEvent?.TimestampUtc ?? adviserApprovedEvent?.TimestampUtc);

        var metrics = new MetricsDto(
            TotalEvents: ordered.Count,
            TimeToDelivery: governance.DeliveredAtUtc.HasValue && ordered.Count > 0
                ? ToIso8601Duration(governance.DeliveredAtUtc.Value - ordered.First().TimestampUtc)
                : null,
            TimeToBlock: blockedEvent is not null && ordered.Count > 0
                ? ToIso8601Duration(blockedEvent.TimestampUtc - ordered.First().TimestampUtc)
                : null);

        return new CaseReconstructionDto(caseId, ordered, governance, metrics);
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

    private static string? GetMetadataString(JsonElement metadata, params string[] propertyNames)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (metadata.TryGetProperty(propertyName, out var property) &&
                property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
            }
        }

        return null;
    }

    private static string ToIso8601Duration(TimeSpan duration) => XmlConvert.ToString(duration);
}
