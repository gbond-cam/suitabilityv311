using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Audit.Lineage.Functions;

public class RecordAuditLineageFunction
{
    private readonly ILogger<RecordAuditLineageFunction> _logger;

    public RecordAuditLineageFunction(ILogger<RecordAuditLineageFunction> logger)
    {
        _logger = logger;
    }

    [Function("RecordAuditLineage")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "audit/lineage")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<AuditLineageRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid audit lineage payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Recording audit lineage event {EventType} for case {CaseId}.", payload.EventType, payload.CaseId);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "audit.lineage",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
