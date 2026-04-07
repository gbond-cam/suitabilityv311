using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Shared.Kernel.Models.Responses;
using Suitability.Engine.Services;

namespace Suitability.Engine.Functions;

public class GetSuitabilityWorkflowStatusFunction
{
    private readonly ICaseWorkflowStateStore _stateStore;
    private readonly IApiAuthorizationService _apiAuthorizationService;

    public GetSuitabilityWorkflowStatusFunction(ICaseWorkflowStateStore stateStore, IApiAuthorizationService apiAuthorizationService)
    {
        _stateStore = stateStore;
        _apiAuthorizationService = apiAuthorizationService;
    }

    [Function("GetSuitabilityWorkflowStatus")]
    [OpenApiOperation(operationId: "GetSuitabilityWorkflowStatus", tags: new[] { "Suitability" }, Summary = "Get workflow status", Description = "Returns the per-case workflow state for evidence upload, suitability evaluation, and report generation.", Visibility = OpenApiVisibilityType.Important)]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT", Name = "Authorization", In = OpenApiSecurityLocationType.Header, Description = "Azure AD OAuth bearer token.")]
    [OpenApiParameter(name: "caseId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Summary = "Case identifier", Description = "The unique case identifier to inspect.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(CaseWorkflowStateResponse), Summary = "Workflow state", Description = "The current state of the case workflow.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Summary = "Not found", Description = "No workflow state exists for the supplied case identifier.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Summary = "Unauthorized", Description = "An Azure AD bearer token is required.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Summary = "Forbidden", Description = "The authenticated caller does not have a required role.")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "suitability/cases/{caseId}/status")] HttpRequestData req, string caseId, CancellationToken cancellationToken)
    {
        var authorizationFailure = await _apiAuthorizationService.AuthorizeAsync(req, cancellationToken, "SUITABILITY_STATUS_ALLOWED_ROLES");
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var state = await _stateStore.GetAsync(caseId, cancellationToken);
        if (state is null)
        {
            var notFoundState = new CaseWorkflowStateResponse
            {
                CaseId = caseId,
                Status = "pending",
                Message = $"No workflow state exists yet for case {caseId}.",
                NextPrompt = "Please upload client data first."
            };
            WorkflowUserExperienceHints.Apply(notFoundState);

            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(notFoundState, cancellationToken);
            return notFound;
        }

        WorkflowUserExperienceHints.Apply(state);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(state, cancellationToken);
        return response;
    }
}