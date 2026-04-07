using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;
using Shared.Kernel.Validation;
using Suitability.Engine.Services;

namespace Suitability.Engine.Functions;

public class EvaluateSuitabilityFunction
{
    private readonly ILogger<EvaluateSuitabilityFunction> _logger;
    private readonly ISuitabilityWorkflowOrchestrator _workflowOrchestrator;
    private readonly IApiAuthorizationService _apiAuthorizationService;
    private readonly ILineageRecorder _lineageRecorder;

    public EvaluateSuitabilityFunction(ILogger<EvaluateSuitabilityFunction> logger, ISuitabilityWorkflowOrchestrator workflowOrchestrator, IApiAuthorizationService apiAuthorizationService, ILineageRecorder lineageRecorder)
    {
        _logger = logger;
        _workflowOrchestrator = workflowOrchestrator;
        _apiAuthorizationService = apiAuthorizationService;
        _lineageRecorder = lineageRecorder;
    }

    [Function("EvaluateSuitability")]
    [OpenApiOperation(operationId: "EvaluateSuitability", tags: new[] { "Suitability" }, Summary = "Evaluate suitability workflow", Description = "Runs the end-to-end case workflow: validates evidence, evaluates suitability, waits for completion, and can trigger report generation while maintaining per-case state.", Visibility = OpenApiVisibilityType.Important)]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT", Name = "Authorization", In = OpenApiSecurityLocationType.Header, Description = "Azure AD OAuth bearer token.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(SuitabilityEvaluationRequest), Required = true, Description = "The suitability workflow request payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Accepted, contentType: "application/json", bodyType: typeof(AcceptedResponse), Summary = "Accepted", Description = "The end-to-end suitability workflow was accepted and case state tracking has started.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ValidationProblemResponse), Summary = "Bad request", Description = "A valid suitability evaluation payload, including structured client data intake, is required.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Summary = "Unauthorized", Description = "An Azure AD bearer token is required.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Summary = "Forbidden", Description = "The authenticated caller does not have a required role.")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "suitability/evaluate")] HttpRequestData req, CancellationToken cancellationToken)
    {
        var authorizationFailure = await _apiAuthorizationService.AuthorizeAsync(req, cancellationToken, "SUITABILITY_EVALUATE_ALLOWED_ROLES");
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        SuitabilityEvaluationRequest? payload;
        try
        {
            payload = await req.ReadFromJsonAsync<SuitabilityEvaluationRequest>();
        }
        catch (Exception ex) when (ex is JsonException || ex is AggregateException { InnerException: JsonException })
        {
            var badJson = req.CreateResponse(HttpStatusCode.BadRequest);
            await badJson.WriteAsJsonAsync(new ValidationProblemResponse
            {
                Message = "A valid JSON suitability evaluation payload is required.",
                Prompt = "Please check the request details and try again.",
                SuggestedAction = "fix-request-payload",
                Errors = ["The request body could not be parsed as JSON."]
            }, cancellationToken);
            badJson.StatusCode = HttpStatusCode.BadRequest;
            return badJson;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ValidationProblemResponse
            {
                Message = "A valid suitability evaluation payload is required.",
                Prompt = "Please provide a case ID and client details to continue.",
                SuggestedAction = "complete-request",
                Errors = ["CaseId is required."]
            }, cancellationToken);
            badRequest.StatusCode = HttpStatusCode.BadRequest;
            return badRequest;
        }

        var validationErrors = SuitabilityEvaluationRequestValidator.Validate(payload);
        if (validationErrors.Count > 0)
        {
            var clientDataMissing = payload.ClientData is null;
            var problem = new ValidationProblemResponse
            {
                Message = "Structured client data intake is incomplete. Please complete the fact-find before starting evaluation.",
                Errors = validationErrors.ToList()
            };
            WorkflowUserExperienceHints.Apply(problem, clientDataMissing);

            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(problem, cancellationToken);
            badRequest.StatusCode = HttpStatusCode.BadRequest;
            return badRequest;
        }

        await RecordAuditAsync(payload, LineageActions.SuitabilityEvaluationRequested, new
        {
            payload.AdviceScope,
            payload.RiskProfile,
            payload.AutoStartEvaluation,
            payload.AutoGenerateReport,
            payload.WaitForCompletion
        });

        _logger.LogInformation("Starting suitability workflow for case {CaseId}. AutoGenerateReport={AutoGenerateReport}", payload.CaseId, payload.AutoGenerateReport);

        CaseWorkflowStateResponse workflowState;
        try
        {
            workflowState = await _workflowOrchestrator.RunAsync(payload, cancellationToken);
        }
        catch (Exception ex)
        {
            await RecordAuditAsync(payload, LineageActions.SuitabilityEvaluationFailed, new
            {
                payload.AdviceScope,
                payload.RiskProfile,
                payload.AutoGenerateReport,
                error = ex.Message
            });
            throw;
        }

        await RecordAuditAsync(payload, LineageActions.SuitabilityEvaluationCompleted, new
        {
            workflowStatus = workflowState.Status,
            workflowState.CurrentStage,
            workflowState.ProgressPercentage,
            workflowState.DownloadUrl,
            workflowState.SecureDownloadUrl
        });

        var evaluationStep = workflowState.Steps.FirstOrDefault(step => string.Equals(step.Step, WorkflowStepNames.EvaluateSuitability, StringComparison.OrdinalIgnoreCase));
        var statusUrl = $"{req.Url.GetLeftPart(UriPartial.Authority)}/api/suitability/cases/{Uri.EscapeDataString(payload.CaseId)}/status";

        var accepted = new AcceptedResponse
        {
            Operation = "suitability.evaluate",
            Status = workflowState.Status,
            CorrelationId = evaluationStep?.CorrelationId ?? Guid.NewGuid().ToString("N"),
            CaseId = payload.CaseId,
            Message = workflowState.Message,
            CurrentStage = workflowState.CurrentStage,
            ProgressPercentage = workflowState.ProgressPercentage,
            NextPrompt = workflowState.NextPrompt,
            StatusUrl = statusUrl,
            DownloadUrl = workflowState.DownloadUrl,
            SecureDownloadUrl = workflowState.SecureDownloadUrl
        };
        WorkflowUserExperienceHints.Apply(accepted);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(accepted, cancellationToken);
        response.StatusCode = HttpStatusCode.Accepted;
        return response;
    }

    private Task RecordAuditAsync(SuitabilityEvaluationRequest payload, object metadata)
        => RecordAuditAsync(payload, LineageActions.SuitabilityEvaluationRequested, metadata);

    private Task RecordAuditAsync(SuitabilityEvaluationRequest payload, string action, object metadata)
    {
        return _lineageRecorder.RecordAsync(new LineageRecord(
            EventId: string.Empty,
            CaseId: payload.CaseId,
            Stage: LineageStages.Generation,
            Action: action,
            ArtefactName: "SuitabilityWorkflow",
            ArtefactVersion: string.IsNullOrWhiteSpace(payload.AdviceScope) ? "v1" : payload.AdviceScope,
            ArtefactHash: "N/A",
            PerformedBy: "suitability.engine",
            TimestampUtc: DateTimeOffset.UtcNow,
            Metadata: metadata));
    }
}
