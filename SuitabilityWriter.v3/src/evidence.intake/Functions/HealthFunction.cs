using System.Net;
using System.Text.Json;
using Evidence.Intake.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Evidence.Intake.Functions;

public class IntakeEvidenceFunction
{
    private readonly ILogger<IntakeEvidenceFunction> _logger;
    private readonly ISharePointEvidenceResolver _sharePointEvidenceResolver;
    private readonly ILineageRecorder _lineageRecorder;

    public IntakeEvidenceFunction(ILogger<IntakeEvidenceFunction> logger, ISharePointEvidenceResolver sharePointEvidenceResolver, ILineageRecorder lineageRecorder)
    {
        _logger = logger;
        _sharePointEvidenceResolver = sharePointEvidenceResolver;
        _lineageRecorder = lineageRecorder;
    }

    [Function("IntakeEvidence")]
    [OpenApiOperation(operationId: "IntakeEvidence", tags: new[] { "Evidence" }, Summary = "Intake case evidence", Description = "Accepts evidence intake requests, including SharePoint client file and folder references, for suitability processing.", Visibility = OpenApiVisibilityType.Important)]
    [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
    [OpenApiSecurity("function_key_header", SecuritySchemeType.ApiKey, Name = "x-functions-key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(EvidenceIntakeRequest), Required = true, Description = "The evidence intake request payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Accepted, contentType: "application/json", bodyType: typeof(AcceptedResponse), Summary = "Accepted", Description = "The evidence intake request was accepted for processing.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Summary = "Bad request", Description = "A valid evidence intake payload is required.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadGateway, Summary = "SharePoint resolution failed", Description = "The SharePoint case file could not be resolved or accessed.")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "evidence/intake")] HttpRequestData req)
    {
        EvidenceIntakeRequest? payload;
        try
        {
            payload = await req.ReadFromJsonAsync<EvidenceIntakeRequest>();
        }
        catch (Exception ex) when (ex is JsonException || ex is AggregateException { InnerException: JsonException })
        {
            var badJson = req.CreateResponse(HttpStatusCode.BadRequest);
            await badJson.WriteStringAsync("A valid JSON evidence intake payload is required.");
            return badJson;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid evidence intake payload is required.");
            return badRequest;
        }

        var isSharePointSource =
            string.Equals(payload.Source, "sharepoint", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.SourceType, "sharepoint-file", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.SourceType, "sharepoint-folder", StringComparison.OrdinalIgnoreCase);

        if (isSharePointSource)
        {
            var sharePoint = payload.SharePoint;
            var hasValidReference = sharePoint is not null &&
                                    (
                                        !string.IsNullOrWhiteSpace(sharePoint.WebUrl) ||
                                        (!string.IsNullOrWhiteSpace(sharePoint.SiteId) &&
                                         !string.IsNullOrWhiteSpace(sharePoint.DriveId) &&
                                         !string.IsNullOrWhiteSpace(sharePoint.ItemId))
                                    );

            if (!hasValidReference)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("A valid SharePoint reference is required when source is sharepoint.");
                return badRequest;
            }

            try
            {
                var resolvedEvidence = await _sharePointEvidenceResolver.ResolveAsync(payload, CancellationToken.None);

                _logger.LogInformation(
                    "Intaking SharePoint evidence for case {CaseId}. SourceType={SourceType}, Root={RootWebUrl}, IncludeChildren={IncludeChildren}, FilesResolved={FilesResolved}.",
                    payload.CaseId,
                    payload.SourceType,
                    resolvedEvidence.RootWebUrl,
                    sharePoint!.IncludeChildren,
                    resolvedEvidence.FileCount);

                await RecordAuditAsync(payload, LineageActions.EvidenceUploaded, new
                {
                    payload.EvidenceType,
                    payload.Source,
                    payload.SourceType,
                    resolvedEvidence.RootWebUrl,
                    resolvedEvidence.FileCount,
                    sharePoint.IncludeChildren,
                    status = "completed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve SharePoint evidence for case {CaseId}.", payload.CaseId);
                await RecordAuditAsync(payload, LineageActions.EvidenceUploadFailed, new
                {
                    payload.EvidenceType,
                    payload.Source,
                    payload.SourceType,
                    error = ex.Message
                });
                var sharePointError = req.CreateResponse(HttpStatusCode.BadGateway);
                await sharePointError.WriteStringAsync("Unable to resolve SharePoint evidence for the supplied reference.");
                return sharePointError;
            }
        }
        else
        {
            _logger.LogInformation(
                "Intaking evidence for case {CaseId} of type {EvidenceType} from source {Source}.",
                payload.CaseId,
                payload.EvidenceType,
                string.IsNullOrWhiteSpace(payload.Source) ? "unspecified" : payload.Source);

            await RecordAuditAsync(payload, LineageActions.EvidenceUploaded, new
            {
                payload.EvidenceType,
                payload.Source,
                payload.SourceType,
                status = "completed"
            });
        }

        var accepted = new AcceptedResponse
        {
            Operation = "evidence.intake",
            Status = "completed",
            CorrelationId = Guid.NewGuid().ToString("N"),
            CaseId = payload.CaseId,
            Message = "Step 1 of 3 complete: evidence was received and validated.",
            CurrentStage = "Client data upload",
            ProgressPercentage = 34,
            NextPrompt = "Shall I start the evaluation now?"
        };
        WorkflowUserExperienceHints.Apply(accepted);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(accepted);
        response.StatusCode = HttpStatusCode.Accepted;
        return response;
    }

    private Task RecordAuditAsync(EvidenceIntakeRequest payload, string action, object metadata)
    {
        return _lineageRecorder.RecordAsync(new LineageRecord(
            EventId: string.Empty,
            CaseId: payload.CaseId,
            Stage: LineageStages.Validation,
            Action: action,
            ArtefactName: string.IsNullOrWhiteSpace(payload.EvidenceType) ? "ClientEvidence" : payload.EvidenceType,
            ArtefactVersion: string.IsNullOrWhiteSpace(payload.SourceType) ? "v1" : payload.SourceType,
            ArtefactHash: "N/A",
            PerformedBy: "evidence.intake",
            TimestampUtc: DateTimeOffset.UtcNow,
            Metadata: metadata));
    }
}
