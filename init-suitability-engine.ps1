param (
    [string]$RootPath = (Get-Location).Path
)

Write-Host "Initializing Suitability.Engine.FunctionApp in $RootPath" -ForegroundColor Cyan

function New-FileIfMissing {
    param (
        [string]$Path,
        [string]$Content
    )

    $fullPath = Join-Path $RootPath $Path
    $dir = Split-Path $fullPath -Parent

    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    if (-not (Test-Path $fullPath)) {
        $Content | Out-File -FilePath $fullPath -Encoding utf8
        Write-Host "Created: $Path" -ForegroundColor Green
    }
    else {
        Write-Host "Exists : $Path" -ForegroundColor Yellow
    }
}


$projRoot = "src/Suitability.Engine.FunctionApp"

# ------------------------------------------------------------
# Project + host files (Azure Functions isolated)
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Suitability.Engine.FunctionApp.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.24.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.18.0" OutputItemType="Analyzer" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.3.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.OpenApi" Version="1.6.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.ApplicationInsights" Version="1.4.0" />
    <PackageReference Include="Microsoft.ApplicationInsights.WorkerService" Version="2.23.0" />
    <PackageReference Include="Azure.Identity" Version="1.19.0" />
    <PackageReference Include="Azure.Storage.Blobs" Version="12.19.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\SuitabilityWriter.v3\src\shared.kernel\shared.kernel.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="host.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="local.settings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>Never</CopyToPublishDirectory>
    </None>
  </ItemGroup>

</Project>
'@

New-FileIfMissing "$projRoot/host.json" @'
{
  "version": "2.0",
  "logging": {
    "applicationInsights": { "samplingSettings": { "isEnabled": true } }
  }
}
'@

New-FileIfMissing "$projRoot/local.settings.json" @'
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",

    "AUDIT_LINEAGE_BASE_URL": "https://<audit-lineage>.azurewebsites.net/",
    "AUDIT_LINEAGE_FUNCTION_KEY": "<function-key>",

    "APP_VERSION": "SuitabilityEngine/3.0"
  }
}
'@

New-FileIfMissing "$projRoot/README.md" @'
# Suitability.Engine.FunctionApp

Orchestrates the advice case processing pipeline:
- pulls evidence
- validates input
- routes template
- enforces placeholder validation
- generates report
- emits audit lineage

This app is orchestration only: business logic lives in services, and evidential integrity lives in audit.lineage.
'@

# ------------------------------------------------------------
# Program.cs (DI wiring) â€” matches existing patterns
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Program.cs" @'
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Suitability.Engine.FunctionApp.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureOpenApi()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddHttpClient();

        // Core engine
        services.AddSingleton<ISuitabilityEngine, SuitabilityEngine>();

        // Supporting services (stubs by default; replace with real implementations)
        services.AddSingleton<IStatusStore, InMemoryStatusStore>();
        services.AddSingleton<ICaseEvidenceStore, StubCaseEvidenceStore>();
        services.AddSingleton<ISuitabilityArtefactCatalog, StubSuitabilityArtefactCatalog>();
        services.AddSingleton<ITemplateRouter, StubTemplateRouter>();
        services.AddSingleton<IReportGenerator, StubReportGenerator>();
        services.AddSingleton<IPlaceholderValidator, StubPlaceholderValidator>();
        services.AddSingleton<IAuditLineageClient, AuditLineageClient>();
    })
    .Build();

host.Run();
'@

# ------------------------------------------------------------
# Models â€” matches existing Models.cs patterns
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Models/Models.cs" @'
namespace Suitability.Engine.FunctionApp.Models;

public record EngineArtefactReference(string Name, string Version, string Status, string SharePointUrl);

public record ReportInfo(string Format, string Url);

public record SchemaVersions(string CaseEvidence, string SuitabilityInput);

public record StatusResponse(string CaseId, string Status, DateTimeOffset Timestamp);

public record GenerateSuccessResponse(
    string Status,
    string Message,
    string CaseId,
    ReportInfo Report,
    List<EngineArtefactReference> ArtefactsUsed,
    SchemaVersions SchemaVersions);

public record LineageResponse(
    string CaseId,
    List<EngineArtefactReference> ArtefactsUsed,
    Dictionary<string, string> Schemas,
    DateTimeOffset GeneratedAtUtc,
    string EngineVersion);

public record GenerateBlockedResponse(
    string Status,
    string ReasonCode,
    string Message,
    string CaseId);

public record ErrorResponse(
    string Status,
    string Message,
    string CorrelationId);
'@

# ------------------------------------------------------------
# Services â€” interface + engine (aligned to existing files)
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Services/ISuitabilityEngine.cs" @'
using Suitability.Engine.FunctionApp.Models;

namespace Suitability.Engine.FunctionApp.Services;

public interface ISuitabilityEngine
{
    Task<(bool success, object response)> GenerateAsync(string caseId, CancellationToken ct);
    Task<LineageResponse?> GetLineageAsync(string caseId, CancellationToken ct);
}
'@

New-FileIfMissing "$projRoot/Services/SuitabilityEngine.cs" @'
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
'@

New-FileIfMissing "$projRoot/Services/SuitabilityEngineStub.cs" @'
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
'@

# ------------------------------------------------------------
# Supporting abstractions + stubs (kept minimal but compile-ready)
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Services/Supporting.cs" @'
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
'@

# ------------------------------------------------------------
# Functions â€” Generate, Status, Lineage (lineage function aligns to existing file)
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Functions/SuitabilityGenerateFunction.cs" @'
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using Suitability.Engine.FunctionApp.Services;

namespace Suitability.Engine.FunctionApp.Functions;

public sealed class SuitabilityGenerateFunction
{
    private readonly ISuitabilityEngine _engine;

    public SuitabilityGenerateFunction(ISuitabilityEngine engine) => _engine = engine;

    [Function("Suitability_Generate")]
    [OpenApiOperation(operationId: "generateSuitability", tags: new[] { "suitability" }, Summary = "Generate suitability report")]
    [OpenApiParameter(name: "caseId", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Case ID")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "suitability/generate")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var caseId = query["caseId"];

        if (string.IsNullOrWhiteSpace(caseId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("caseId is required.", ct);
            return bad;
        }

        var (success, response) = await _engine.GenerateAsync(caseId, ct);
        var resp = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.Conflict);
        await resp.WriteAsJsonAsync(response, ct);
        return resp;
    }
}
'@

New-FileIfMissing "$projRoot/Functions/SuitabilityStatusFunction.cs" @'
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Suitability.Engine.FunctionApp.Services;

namespace Suitability.Engine.FunctionApp.Functions;

public sealed class SuitabilityStatusFunction
{
    private readonly IStatusStore _status;

    public SuitabilityStatusFunction(IStatusStore status) => _status = status;

    [Function("Suitability_Status")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "suitability/status")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var caseId = query["caseId"];

        if (string.IsNullOrWhiteSpace(caseId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("caseId is required.", ct);
            return bad;
        }

        var status = await _status.GetAsync(caseId, ct);
        if (status is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteStringAsync("No status found for caseId.", ct);
            return nf;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(status, ct);
        return ok;
    }
}
'@

New-FileIfMissing "$projRoot/Functions/SuitabilityLineageFunction.cs" @'
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using Suitability.Engine.FunctionApp.Models;
using Suitability.Engine.FunctionApp.Services;

namespace Suitability.Engine.FunctionApp.Functions;

public sealed class SuitabilityLineageFunction
{
    private readonly ISuitabilityEngine _engine;

    public SuitabilityLineageFunction(ISuitabilityEngine engine) => _engine = engine;

    [Function("Suitability_Lineage")]
    [OpenApiOperation(operationId: "getSuitabilityLineage", tags: new[] { "suitability" }, Summary = "Get lineage")]
    [OpenApiParameter(name: "caseId", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Case ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(LineageResponse), Summary = "Lineage")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "suitability/lineage")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var caseId = query["caseId"];

        if (string.IsNullOrWhiteSpace(caseId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("caseId is required.", ct);
            return bad;
        }

        var lineage = await _engine.GetLineageAsync(caseId, ct);
        if (lineage is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteStringAsync("No lineage found for caseId.", ct);
            return notFound;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(lineage, ct);
        return ok;
    }
}
'@

Write-Host "`n[OK] Suitability.Engine.FunctionApp initialized successfully." -ForegroundColor Cyan

