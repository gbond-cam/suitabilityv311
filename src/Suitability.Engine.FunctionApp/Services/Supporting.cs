using Suitability.Engine.FunctionApp.Models;

namespace Suitability.Engine.FunctionApp.Services;

public interface IStatusStore
{
    Task SetAsync(StatusResponse status, CancellationToken ct);
    Task<StatusResponse?> GetAsync(string caseId, CancellationToken ct);
}

public sealed class InMemoryStatusStore : IStatusStore
{
    private readonly Dictionary<string, StatusResponse> _store = new();
    public Task SetAsync(StatusResponse status, CancellationToken ct) { _store[status.CaseId] = status; return Task.CompletedTask; }
    public Task<StatusResponse?> GetAsync(string caseId, CancellationToken ct) => Task.FromResult(_store.TryGetValue(caseId, out var s) ? s : null);
}

public interface ICaseEvidenceStore
{
    Task<string> GetCaseEvidenceJsonAsync(string caseId, CancellationToken ct);
}

public sealed class StubCaseEvidenceStore : ICaseEvidenceStore
{
    public Task<string> GetCaseEvidenceJsonAsync(string caseId, CancellationToken ct)
        => Task.FromResult("{\"caseId\":\"" + caseId + "\"}");
}

public interface ISuitabilityArtefactCatalog
{
    Task<ApprovedArtefact> GetApprovedTemplateAsync(string templateName, string version, CancellationToken ct);
}

public sealed record ApprovedArtefact(string Name, string Version, string Status, string? SourceUrl, string Hash);

public sealed class StubSuitabilityArtefactCatalog : ISuitabilityArtefactCatalog
{
    public Task<ApprovedArtefact> GetApprovedTemplateAsync(string templateName, string version, CancellationToken ct)
        => Task.FromResult(new ApprovedArtefact(templateName, version, "Approved", "https://sharepoint/.../template.docx", "N/A"));
}

public interface ITemplateRouter
{
    Task<(string TemplateName, string Version)> RouteAsync(string caseId, string evidenceJson, CancellationToken ct);
}

public sealed class StubTemplateRouter : ITemplateRouter
{
    public Task<(string TemplateName, string Version)> RouteAsync(string caseId, string evidenceJson, CancellationToken ct)
        => Task.FromResult(("Consilium Suitability Report - ISA Template.docx", "v1"));
}

public interface IPlaceholderValidator
{
    Task<PlaceholderValidationResult> ValidateAsync(ApprovedArtefact template, string evidenceJson, CancellationToken ct);
}

public sealed record PlaceholderIssue(string Placeholder, string Severity, string Message);

public sealed record PlaceholderValidationResult(IReadOnlyList<PlaceholderIssue> Issues)
{
    public bool HasBlockers => Issues.Any(i => i.Severity == "Block");
}

public sealed class StubPlaceholderValidator : IPlaceholderValidator
{
    public Task<PlaceholderValidationResult> ValidateAsync(ApprovedArtefact template, string evidenceJson, CancellationToken ct)
        => Task.FromResult(new PlaceholderValidationResult(Array.Empty<PlaceholderIssue>()));
}

public interface IReportGenerator
{
    Task<(string Format, string Url)> GenerateAsync(string caseId, ApprovedArtefact template, string evidenceJson, CancellationToken ct);
}

public sealed class StubReportGenerator : IReportGenerator
{
    public Task<(string Format, string Url)> GenerateAsync(string caseId, ApprovedArtefact template, string evidenceJson, CancellationToken ct)
        => Task.FromResult(("docx", "https://sharepoint/.../generated-report.docx"));
}

public interface IAuditLineageClient
{
    Task RecordAsync(string caseId, string stage, string action, ApprovedArtefact artefact, object metadata, CancellationToken ct);
}

public sealed class AuditLineageClient : IAuditLineageClient
{
    private readonly HttpClient _http;

    public AuditLineageClient(IHttpClientFactory factory)
    {
        _http = factory.CreateClient();
        var baseUrl = Environment.GetEnvironmentVariable("AUDIT_LINEAGE_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
            _http.BaseAddress = new Uri(baseUrl);

        var key = Environment.GetEnvironmentVariable("AUDIT_LINEAGE_FUNCTION_KEY");
        if (!string.IsNullOrWhiteSpace(key))
            _http.DefaultRequestHeaders.Add("x-functions-key", key);
    }

    public async Task RecordAsync(string caseId, string stage, string action, ApprovedArtefact artefact, object metadata, CancellationToken ct)
    {
        // Minimal payload; align to your lineage record shape in audit.lineage
        var payload = new
        {
            caseId,
            stage,
            action,
            artefactName = artefact.Name,
            artefactVersion = artefact.Version,
            artefactHash = artefact.Hash,
            performedBy = "suitability.engine",
            timestampUtc = DateTimeOffset.UtcNow,
            metadata
        };

        // Endpoint name may differ in your audit.lineage app; keep this consistent with your implementation.
        await _http.PostAsJsonAsync("api/RecordLineageEvent", payload, ct);
    }
}
