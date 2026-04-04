using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Placeholder.Validation.Functions;

public class ValidatePlaceholderFunction
{
    private readonly ILogger<ValidatePlaceholderFunction> _logger;
    private readonly ILineageRecorder _lineage;
    private readonly ISystemClock _clock;

    public ValidatePlaceholderFunction(
        ILogger<ValidatePlaceholderFunction> logger,
        ILineageRecorder lineage,
        ISystemClock clock)
    {
        _logger = logger;
        _lineage = lineage;
        _clock = clock;
    }

    [Function("ValidatePlaceholder")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "placeholders/validate")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<PlaceholderValidationRequest>();
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.TemplateId) ||
            string.IsNullOrWhiteSpace(payload.CaseId) ||
            payload.Artefact is null ||
            string.IsNullOrWhiteSpace(payload.Artefact.Name) ||
            string.IsNullOrWhiteSpace(payload.Artefact.Version) ||
            string.IsNullOrWhiteSpace(payload.Artefact.Hash))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid placeholder validation payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Validating placeholders for template {TemplateId}.", payload.TemplateId);

        var caseId = payload.CaseId;
        var artefact = payload.Artefact;
        var artefactUrl = $"https://example.invalid/artefacts/{Uri.EscapeDataString(artefact.Name)}";

        var meta = JsonSerializer.SerializeToElement(new
        {
            sourceUrl = artefactUrl,
            incidentId = (string?)null
        });

        var artefactUsed = new LineageRecord(
            EventId: string.Empty,
            CaseId: caseId,
            Stage: LineageStages.Validation,
            Action: "ArtefactUsed",
            ArtefactName: artefact.Name,
            ArtefactVersion: artefact.Version,
            ArtefactHash: artefact.Hash,
            PerformedBy: "placeholder.validation",
            TimestampUtc: _clock.UtcNow,
            Metadata: meta
        );

        artefactUsed = artefactUsed with
        {
            EventId = IdempotencyKey.From(artefactUsed)
        };

        await _lineage.RecordAsync(artefactUsed);

        await _lineage.RecordAsync(new LineageRecord(
            CaseId: caseId,
            Stage: "Validation",
            Action: "SchemaValidated",
            ArtefactName: artefact.Name,
            ArtefactVersion: artefact.Version,
            ArtefactHash: artefact.Hash,
            PerformedBy: "placeholder.validation",
            TimestampUtc: _clock.UtcNow,
            Metadata: new { blocks = 0, warnings = 2 }
        ));

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "placeholder.validate",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
