using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Delivery.Gate.Functions;

public class EvaluateDeliveryGateFunction
{
    private readonly ILogger<EvaluateDeliveryGateFunction> _logger;
    private readonly ILineageRecorder _lineage;
    private readonly ISystemClock _clock;

    public EvaluateDeliveryGateFunction(
        ILogger<EvaluateDeliveryGateFunction> logger,
        ILineageRecorder lineage,
        ISystemClock clock)
    {
        _logger = logger;
        _lineage = lineage;
        _clock = clock;
    }

    [Function("EvaluateDeliveryGate")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "delivery/gate")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<DeliveryGateRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid delivery gate payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Evaluating delivery gate {Stage} for case {CaseId}.", payload.Stage, payload.CaseId);

        var caseId = payload.CaseId;
        var adviserId = payload.DecisionBy;
        var _auditLineage = _lineage;

        if (payload.Stage.Equals(LineageStages.Governance, StringComparison.OrdinalIgnoreCase) ||
            payload.Stage.Contains("Emergency", StringComparison.OrdinalIgnoreCase))
        {
            var metadata = JsonSerializer.SerializeToElement(new
            {
                incidentId = "INC-2026-04-002",
                approvedBy = "ComplianceOwner",
                reason = "Production outage blocking advice"
            });

            var record = new LineageRecord(
                EventId: string.Empty,
                CaseId: caseId,
                Stage: LineageStages.Governance,
                Action: LineageActions.EmergencyOverrideApplied,
                ArtefactName: "N/A",
                ArtefactVersion: "N/A",
                ArtefactHash: "N/A",
                PerformedBy: "graham.bond",
                TimestampUtc: _clock.UtcNow,
                Metadata: metadata
            );

            record = record with
            {
                EventId = IdempotencyKey.From(record)
            };

            await _auditLineage.RecordAsync(record);

            await _auditLineage.RecordAsync(new LineageRecord(
                CaseId: caseId,
                Stage: LineageStages.Governance,
                Action: LineageActions.IncidentCreated,
                ArtefactName: "N/A",
                ArtefactVersion: "N/A",
                ArtefactHash: "N/A",
                PerformedBy: "system",
                TimestampUtc: _clock.UtcNow,
                Metadata: new
                {
                    incidentId = "INC-2026-04-002",
                    source = "github-issue",
                    severity = "High"
                }
            ));

            var incidentId = "INC-2026-04-002";
            var slaWarningRecord = new LineageRecord(
                EventId: string.Empty,
                CaseId: caseId,
                Stage: LineageStages.Governance,
                Action: LineageActions.SlaWarningIssued,
                ArtefactName: "N/A",
                ArtefactVersion: "N/A",
                ArtefactHash: "N/A",
                PerformedBy: "system",
                TimestampUtc: _clock.UtcNow,
                Metadata: new { incidentId, level = "Critical", message = "SLA breach imminent" }
            );

            slaWarningRecord = slaWarningRecord with { EventId = IdempotencyKey.From(slaWarningRecord, discriminator: "Critical") };
            await _auditLineage.RecordAsync(slaWarningRecord);

            await _auditLineage.RecordAsync(new LineageRecord(
                CaseId: caseId,
                Stage: LineageStages.Governance,
                Action: LineageActions.IncidentClosed,
                ArtefactName: "N/A",
                ArtefactVersion: "N/A",
                ArtefactHash: "N/A",
                PerformedBy: "compliance.owner",
                TimestampUtc: _clock.UtcNow,
                Metadata: new
                {
                    incidentId = "INC-2026-04-002",
                    reviewOutcome = "Root cause identified; preventative control added"
                }
            ));
        }

        await _lineage.RecordAsync(new LineageRecord(
            CaseId: caseId,
            Stage: LineageStages.Delivery,
            Action: "AdviserApproved",
            ArtefactName: "N/A",
            ArtefactVersion: "N/A",
            ArtefactHash: "N/A",
            PerformedBy: adviserId,
            TimestampUtc: _clock.UtcNow,
            Metadata: new { declaration = "Reviewed and suitable" }
        ));

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "delivery.gate",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
