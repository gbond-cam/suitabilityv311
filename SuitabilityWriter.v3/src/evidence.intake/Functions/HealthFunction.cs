using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Evidence.Intake.Functions;

public class IntakeEvidenceFunction
{
    private readonly ILogger<IntakeEvidenceFunction> _logger;

    public IntakeEvidenceFunction(ILogger<IntakeEvidenceFunction> logger)
    {
        _logger = logger;
    }

    [Function("IntakeEvidence")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "evidence/intake")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<EvidenceIntakeRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid evidence intake payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Intaking evidence for case {CaseId} of type {EvidenceType}.", payload.CaseId, payload.EvidenceType);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "evidence.intake",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
