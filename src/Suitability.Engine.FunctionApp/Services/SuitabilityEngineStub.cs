using Suitability.Engine.FunctionApp.Models;

namespace Suitability.Engine.FunctionApp.Services;

/// <summary>
/// Stub used for early wiring tests.
/// </summary>
public sealed class SuitabilityEngineStub : ISuitabilityEngine
{
    private readonly IStatusStore _status;

    public SuitabilityEngineStub(IStatusStore status) => _status = status;

    public async Task<(bool success, object response)> GenerateAsync(string caseId, CancellationToken ct)
    {
        await _status.SetAsync(new StatusResponse(caseId, "Completed", DateTimeOffset.UtcNow), ct);

        var resp = new GenerateSuccessResponse(
            Status: "Success",
            Message: "Stub generation successful.",
            CaseId: caseId,
            Report: new ReportInfo("docx", "https://example/report.docx"),
            ArtefactsUsed: new List<EngineArtefactReference>
            {
                new("Consilium Suitability Report - ISA Template.docx", "v1", "Approved", "https://sharepoint/.../template.docx")
            },
            SchemaVersions: new SchemaVersions("case-evidence.v1", "suitability-input.v1")
        );

        return (true, resp);
    }

    public Task<LineageResponse?> GetLineageAsync(string caseId, CancellationToken ct)
        => Task.FromResult<LineageResponse?>(null);
}
