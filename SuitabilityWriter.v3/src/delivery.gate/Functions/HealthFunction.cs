using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Delivery.Gate.Functions;

public class EvaluateDeliveryGateFunction
{
    private readonly ILogger<EvaluateDeliveryGateFunction> _logger;

    public EvaluateDeliveryGateFunction(ILogger<EvaluateDeliveryGateFunction> logger)
    {
        _logger = logger;
    }

    [Function("EvaluateDeliveryGate")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "delivery/gate")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<DeliveryGateRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid delivery gate payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Evaluating delivery gate {Stage} for case {CaseId}.", payload.Stage, payload.CaseId);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "delivery.gate",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
