using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Case.Bootstrap.Functions;

public class BootstrapCaseFunction
{
    private readonly ILogger<BootstrapCaseFunction> _logger;

    public BootstrapCaseFunction(ILogger<BootstrapCaseFunction> logger)
    {
        _logger = logger;
    }

    [Function("BootstrapCase")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "cases/bootstrap")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<CaseBootstrapRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid case bootstrap payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Bootstrapping case {CaseId} for client {ClientId}.", payload.CaseId, payload.ClientId);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "case.bootstrap",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
