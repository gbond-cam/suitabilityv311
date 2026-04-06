using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Suitability.Engine.Functions;

public class EvaluateSuitabilityFunction
{
    private readonly ILogger<EvaluateSuitabilityFunction> _logger;

    public EvaluateSuitabilityFunction(ILogger<EvaluateSuitabilityFunction> logger)
    {
        _logger = logger;
    }

    [Function("EvaluateSuitability")]
    [OpenApiOperation(operationId: "EvaluateSuitability", tags: new[] { "Suitability" }, Summary = "Evaluate suitability", Description = "Submits a suitability case for generation and returns a correlation identifier.", Visibility = OpenApiVisibilityType.Important)]
    [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
    [OpenApiSecurity("function_key_header", SecuritySchemeType.ApiKey, Name = "x-functions-key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(SuitabilityEvaluationRequest), Required = true, Description = "The suitability evaluation request payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Accepted, contentType: "application/json", bodyType: typeof(AcceptedResponse), Summary = "Accepted", Description = "The suitability evaluation was accepted for processing.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Summary = "Bad request", Description = "A valid suitability evaluation payload is required.")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "suitability/evaluate")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<SuitabilityEvaluationRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid suitability evaluation payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Evaluating suitability for case {CaseId}.", payload.CaseId);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "suitability.evaluate",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
