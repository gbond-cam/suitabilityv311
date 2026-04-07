using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Report.Generator.Functions;

public class GenerateReportFunction
{
    private readonly ILogger<GenerateReportFunction> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public GenerateReportFunction(ILogger<GenerateReportFunction> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [Function("GenerateReport")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "reports/generate")] HttpRequestData req, CancellationToken cancellationToken)
    {
        ReportGenerationRequest? payload;
        try
        {
            payload = await req.ReadFromJsonAsync<ReportGenerationRequest>();
        }
        catch (Exception ex) when (ex is JsonException || ex is AggregateException { InnerException: JsonException })
        {
            var badJson = req.CreateResponse(HttpStatusCode.BadRequest);
            await badJson.WriteStringAsync("A valid JSON report generation payload is required.", cancellationToken);
            return badJson;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid report generation payload is required.", cancellationToken);
            return badRequest;
        }

        var (allowed, message, statusUrl) = await VerifyWorkflowPrerequisitesAsync(payload.CaseId, cancellationToken);
        if (!allowed)
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteStringAsync(message, cancellationToken);
            return conflict;
        }

        _logger.LogInformation("Generating report for case {CaseId} using template {TemplateId}.", payload.CaseId, payload.TemplateId);

        var downloadUrl = BuildDownloadUrl(payload);
        var responseStatus = string.IsNullOrWhiteSpace(downloadUrl) ? "accepted" : "completed";
        var responseMessage = responseStatus == "completed"
            ? "Step 3 of 3 complete: your secure report download link is ready."
            : (string.IsNullOrWhiteSpace(message) ? "Step 3 of 3: report generation has started." : message);

        var accepted = new AcceptedResponse
        {
            Operation = "report.generate",
            Status = responseStatus,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CaseId = payload.CaseId,
            Message = responseMessage,
            StatusUrl = statusUrl,
            DownloadUrl = downloadUrl,
            SecureDownloadUrl = downloadUrl,
            AttemptCount = 1
        };
        WorkflowUserExperienceHints.Apply(accepted);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(accepted, cancellationToken);
        response.StatusCode = HttpStatusCode.Accepted;
        return response;
    }

    private async Task<(bool Allowed, string Message, string StatusUrl)> VerifyWorkflowPrerequisitesAsync(string caseId, CancellationToken cancellationToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("SUITABILITY_ENGINE_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return (true, "Workflow state check skipped because SUITABILITY_ENGINE_BASE_URL is not configured.", string.Empty);
        }

        var client = _httpClientFactory.CreateClient(nameof(GenerateReportFunction));
        client.BaseAddress = new Uri(EnsureTrailingSlash(baseUrl));

        var functionKey = Environment.GetEnvironmentVariable("SUITABILITY_ENGINE_FUNCTION_KEY");
        if (!string.IsNullOrWhiteSpace(functionKey) && !client.DefaultRequestHeaders.Contains("x-functions-key"))
        {
            client.DefaultRequestHeaders.Add("x-functions-key", functionKey);
        }

        var internalApiSharedSecret = Environment.GetEnvironmentVariable("SUITABILITY_ENGINE_INTERNAL_API_SHARED_SECRET");
        if (!string.IsNullOrWhiteSpace(internalApiSharedSecret) && !client.DefaultRequestHeaders.Contains("X-Internal-Api-Key"))
        {
            client.DefaultRequestHeaders.Add("X-Internal-Api-Key", internalApiSharedSecret);
        }

        var relativeUrl = $"api/suitability/cases/{Uri.EscapeDataString(caseId)}/status";
        var requestUrl = AppendFunctionKey(relativeUrl, functionKey);
        var statusUrl = new Uri(client.BaseAddress, requestUrl).ToString();
        var response = await client.GetAsync(requestUrl, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (false, $"No workflow state was found for case {caseId}. Upload data and complete Evaluate Suitability before generating the report.", statusUrl);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, $"The workflow state for case {caseId} could not be verified. Status {(int)response.StatusCode}: {body}", statusUrl);
        }

        var state = await response.Content.ReadFromJsonAsync<CaseWorkflowStateResponse>(cancellationToken: cancellationToken);
        var evaluationStep = state?.Steps.FirstOrDefault(step => string.Equals(step.Step, "suitability.evaluate", StringComparison.OrdinalIgnoreCase));

        if (evaluationStep is null || !string.Equals(evaluationStep.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Evaluate Suitability must complete before Generate Report for case {caseId}.", statusUrl);
        }

        return (true, "Workflow prerequisites satisfied. Report generation accepted.", statusUrl);
    }

    private static string BuildDownloadUrl(ReportGenerationRequest payload)
    {
        var rootUrl = Environment.GetEnvironmentVariable("REPORT_OUTPUT_ROOT_URL");
        if (string.IsNullOrWhiteSpace(rootUrl) || !Uri.TryCreate(rootUrl, UriKind.Absolute, out var baseUri) || !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var safeCaseId = Uri.EscapeDataString(SanitizePathSegment(payload.CaseId));
        var safeTemplateId = Uri.EscapeDataString(SanitizePathSegment(string.IsNullOrWhiteSpace(payload.TemplateId) ? "default-template" : payload.TemplateId));
        return $"{EnsureTrailingSlash(baseUri.ToString())}{safeCaseId}/{safeTemplateId}-suitability-report.pdf";
    }

    private static string SanitizePathSegment(string value)
    {
        return string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";

    private static string AppendFunctionKey(string relativeUrl, string? functionKey)
    {
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return relativeUrl;
        }

        var separator = relativeUrl.Contains('?') ? '&' : '?';
        return $"{relativeUrl}{separator}code={Uri.EscapeDataString(functionKey)}";
    }
}
