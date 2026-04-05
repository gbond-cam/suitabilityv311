param (
    [string]$RootPath = (Get-Location).Path
)

Write-Host "Initializing template.routing in $RootPath" -ForegroundColor Cyan

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


$projRoot = "src/Template.Routing.FunctionApp"

# ------------------------------------------------------------
# Project + host files (Azure Functions isolated)
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Template.Routing.FunctionApp.csproj" @'
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

    "APP_VERSION": "TemplateRouting/1.0",

    "ROUTING_POLICY_PATH": "Policies/routing-policy.json"
  }
}
'@

New-FileIfMissing "$projRoot/README.md" @'
# template.routing

Routes a case evidence payload to the correct approved suitability template.

## Responsibilities
- Apply auditable routing rules (policy-driven)
- Produce deterministic output: templateFileName, templateVersion, reason
- Fail closed only if required inputs are missing (otherwise safe default route)

## Non-responsibilities
- No template retrieval (that is approved artefact catalog / SharePoint)
- No placeholder validation (placeholder.validation)
- No report generation (report.generator)
- No cryptographic signing (audit.lineage / evidence bundle export)
'@

# ------------------------------------------------------------
# Program.cs (DI wiring)
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Program.cs" @'
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Template.Routing.FunctionApp.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureOpenApi()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddSingleton<IRoutingPolicyStore, JsonFileRoutingPolicyStore>();
        services.AddSingleton<ITemplateRouter, TemplateRouter>();
    })
    .Build();

host.Run();
'@

# ------------------------------------------------------------
# Policies (auditable routing rules)
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Policies/routing-policy.json" @'
{
  "default": {
    "templateFileName": "Consilium Suitability Report - ISA Template.docx",
    "templateVersion": "v1",
    "reason": "Default routing - no specific rule matched"
  },
  "rules": [
    {
      "match": { "reportType": "IsaNewBusiness" },
      "route": { "templateFileName": "Isa_New_Business_V1.docx", "templateVersion": "v1", "reason": "Matched reportType IsaNewBusiness" }
    },
    {
      "match": { "reportType": "IsaTransferCash" },
      "route": { "templateFileName": "ISA_Transfer_(Cash)_V1.docx", "templateVersion": "v1", "reason": "Matched reportType IsaTransferCash" }
    },
    {
      "match": { "reportType": "IsaTransferInSpecie" },
      "route": { "templateFileName": "ISA_Transfer_(In-specie)_V1.docx", "templateVersion": "v1", "reason": "Matched reportType IsaTransferInSpecie" }
    },
    {
      "match": { "reportType": "PensionSwitch" },
      "route": { "templateFileName": "Investment Strategy Switch Fundment.docx", "templateVersion": "v1", "reason": "Matched reportType PensionSwitch" }
    }
  ]
}
'@

# ------------------------------------------------------------
# Models
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Models/TemplateRoutingResult.cs" @'
namespace Template.Routing.FunctionApp.Models;

/// <summary>
/// Deterministic routing output.
/// </summary>
public sealed record TemplateRoutingResult(
    string TemplateFileName,
    string TemplateVersion,
    string Reason
);
'@

# ------------------------------------------------------------
# Services: policy store
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Services/IRoutingPolicyStore.cs" @'
using System.Text.Json;

namespace Template.Routing.FunctionApp.Services;

public interface IRoutingPolicyStore
{
    JsonDocument LoadPolicy();
}
'@

New-FileIfMissing "$projRoot/Services/JsonFileRoutingPolicyStore.cs" @'
using System.Text.Json;

namespace Template.Routing.FunctionApp.Services;

public sealed class JsonFileRoutingPolicyStore : IRoutingPolicyStore
{
    public JsonDocument LoadPolicy()
    {
        var configuredPath = Environment.GetEnvironmentVariable("ROUTING_POLICY_PATH") ?? "Policies/routing-policy.json";
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(Environment.CurrentDirectory, configuredPath);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Routing policy file not found: {path}");

        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json);
    }
}
'@

# ------------------------------------------------------------
# Services: ITemplateRouter + implementation
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Services/ITemplateRouter.cs" @'
namespace Template.Routing.FunctionApp.Services;

/// <summary>
/// Routes a case evidence JSON payload to an approved template.
/// </summary>
public interface ITemplateRouter
{
    Task<(string templateFileName, string templateVersion, string reason)> RouteAsync(string caseEvidenceJson, CancellationToken ct);
}
'@

New-FileIfMissing "$projRoot/Services/TemplateRouter.cs" @'
using System.Text.Json;

namespace Template.Routing.FunctionApp.Services;

/// <summary>
/// Policy-driven deterministic template router with safe default.
/// </summary>
public sealed class TemplateRouter : ITemplateRouter
{
    private readonly IRoutingPolicyStore _policyStore;

    public TemplateRouter(IRoutingPolicyStore policyStore)
    {
        _policyStore = policyStore;
    }

    public Task<(string templateFileName, string templateVersion, string reason)> RouteAsync(string caseEvidenceJson, CancellationToken ct)
    {
        using var policy = _policyStore.LoadPolicy();

        var def = policy.RootElement.GetProperty("default");
        var defFile = def.GetProperty("templateFileName").GetString() ?? "Consilium Suitability Report - ISA Template.docx";
        var defVer = def.GetProperty("templateVersion").GetString() ?? "v1";
        var defWhy = def.GetProperty("reason").GetString() ?? "Default routing";

        string? reportType = null;
        using (var evidence = JsonDocument.Parse(caseEvidenceJson))
        {
            if (evidence.RootElement.ValueKind == JsonValueKind.Object &&
                evidence.RootElement.TryGetProperty("reportType", out var rt) &&
                rt.ValueKind == JsonValueKind.String)
            {
                reportType = rt.GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(reportType))
            return Task.FromResult((defFile, defVer, $"{defWhy} (reportType not provided)"));

        foreach (var rule in policy.RootElement.GetProperty("rules").EnumerateArray())
        {
            var match = rule.GetProperty("match");
            if (match.TryGetProperty("reportType", out var expected) &&
                expected.ValueKind == JsonValueKind.String &&
                string.Equals(expected.GetString(), reportType, StringComparison.OrdinalIgnoreCase))
            {
                var route = rule.GetProperty("route");
                var file = route.GetProperty("templateFileName").GetString() ?? defFile;
                var ver = route.GetProperty("templateVersion").GetString() ?? defVer;
                var why = route.GetProperty("reason").GetString() ?? "Matched routing rule";
                return Task.FromResult((file, ver, why));
            }
        }

        return Task.FromResult((defFile, defVer, $"{defWhy} (no rule for reportType={reportType})"));
    }
}
'@

# ------------------------------------------------------------
# Functions: HTTP route endpoint
# ------------------------------------------------------------

New-FileIfMissing "$projRoot/Functions/TemplateRouteFunction.cs" @'
using System.IO;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using Template.Routing.FunctionApp.Models;
using Template.Routing.FunctionApp.Services;

namespace Template.Routing.FunctionApp.Functions;

public sealed class TemplateRouteFunction
{
    private readonly ITemplateRouter _router;

    public TemplateRouteFunction(ITemplateRouter router)
    {
        _router = router;
    }

    [Function("Template_Route")]
    [OpenApiOperation(operationId: "routeTemplate", tags: new[] { "template" }, Summary = "Route a case evidence payload to a template")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(object), Required = true, Description = "Case evidence JSON (must include reportType to match rules)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(TemplateRoutingResult), Summary = "Routing decision")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "template/route")]
        HttpRequestData req,
        CancellationToken ct)
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Request body must be case evidence JSON.", ct);
            return bad;
        }

        var (file, ver, why) = await _router.RouteAsync(body, ct);
        var result = new TemplateRoutingResult(file, ver, why);

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(result, ct);
        return ok;
    }
}
'@

Write-Host "`n[OK] template.routing initialized successfully under $projRoot" -ForegroundColor Cyan

