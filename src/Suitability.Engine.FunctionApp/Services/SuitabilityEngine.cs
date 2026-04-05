using Suitability.Engine.FunctionApp.Models;

namespace Suitability.Engine.FunctionApp.Services;

/// <summary>
/// Orchestrator service for suitability generation.
/// This is deliberately thin: it coordinates other services and emits lineage.
/// </summary>
public sealed class SuitabilityEngine : ISuitabilityEngine
{
    private readonly IStatusStore _status;
    private readonly ICaseEvidenceStore _caseEvidence;
    private readonly ISuitabilityArtefactCatalog _artefacts;
    private readonly ITemplateRouter _router;
    private readonly IPlaceholderValidator _placeholders;
    private readonly IReportGenerator _generator;
    private readonly IAuditLineageClient _lineage;

    private readonly string _engineVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "SuitabilityEngine/3.0";

    public SuitabilityEngine(
        IStatusStore status,
        ICaseEvidenceStore caseEvidence,
        ISuitabilityArtefactCatalog artefacts,
        ITemplateRouter router,
        IPlaceholderValidator placeholders,
        IReportGenerator generator,
        IAuditLineageClient lineage)
    {
        _status = status;
        _caseEvidence = caseEvidence;
        _artefacts = artefacts;
        _router = router;
        _placeholders = placeholders;
        _generator = generator;
        _lineage = lineage;
    }

    public async Task<(bool success, object response)> GenerateAsync(string caseId, CancellationToken ct)
    {
        await _status.SetAsync(new StatusResponse(caseId, "Processing", DateTimeOffset.UtcNow), ct);

        // 1) Get case evidence (already validated upstream by evidence.intake)
        var evidenceJson = await _caseEvidence.GetCaseEvidenceJsonAsync(caseId, ct);

        // 2) Route to approved template
        var template = await _router.RouteAsync(caseId, evidenceJson, ct);

        // 3) Resolve approved artefact reference (for lineage)
        var templateRef = await _artefacts.GetApprovedTemplateAsync(template.TemplateName, template.Version, ct);

        // 4) Placeholder validation (fail closed)
        var validation = await _placeholders.ValidateAsync(templateRef, evidenceJson, ct);
        if (validation.HasBlockers)
        {
            await _status.SetAsync(new StatusResponse(caseId, "Blocked", DateTimeOffset.UtcNow), ct);

            await _lineage.RecordAsync(caseId, "Validation", "Blocked", templateRef, new { reasonCode = "PlaceholderBlock", issues = validation.Issues }, ct);

            return (false, new GenerateBlockedResponse(
                Status: "Blocked",
                ReasonCode: "PlaceholderBlock",
                Message: "Case blocked due to unresolved mandatory placeholders.",
                CaseId: caseId));
        }

        // 5) Generate report
        var report = await _generator.GenerateAsync(caseId, templateRef, evidenceJson, ct);

        // 6) Emit lineage + status
        await _lineage.RecordAsync(caseId, "Generation", "ReportGenerated", templateRef, new { reportUrl = report.Url, format = report.Format }, ct);

        await _status.SetAsync(new StatusResponse(caseId, "Completed", DateTimeOffset.UtcNow), ct);

        var resp = new GenerateSuccessResponse(
            Status: "Success",
            Message: "Suitability report generated successfully.",
            CaseId: caseId,
            Report: new ReportInfo(report.Format, report.Url),
            ArtefactsUsed: new List<EngineArtefactReference>
            {
                new(templateRef.Name, templateRef.Version, templateRef.Status, templateRef.SourceUrl ?? "")
            },
            SchemaVersions: new SchemaVersions("case-evidence.v1", "suitability-input.v1")
        );

        return (true, resp);
    }

    public Task<LineageResponse?> GetLineageAsync(string caseId, CancellationToken ct)
    {
        // Read-side is typically served by audit.lineage; this is a lightweight placeholder.
        return Task.FromResult<LineageResponse?>(null);
    }
}
