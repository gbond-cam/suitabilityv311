using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
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
