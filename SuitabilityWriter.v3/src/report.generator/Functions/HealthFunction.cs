using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Report.Generator.Functions;

public class GenerateReportFunction
{
    private readonly ILogger<GenerateReportFunction> _logger;

    public GenerateReportFunction(ILogger<GenerateReportFunction> logger)
    {
        _logger = logger;
    }

    [Function("GenerateReport")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "reports/generate")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<ReportGenerationRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid report generation payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Generating report for case {CaseId} using template {TemplateId}.", payload.CaseId, payload.TemplateId);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "report.generate",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
