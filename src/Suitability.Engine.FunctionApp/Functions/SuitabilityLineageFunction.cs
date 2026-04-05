using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using Suitability.Engine.FunctionApp.Models;
using Suitability.Engine.FunctionApp.Services;

namespace Suitability.Engine.FunctionApp.Functions;

public sealed class SuitabilityLineageFunction
{
    private readonly ISuitabilityEngine _engine;

    public SuitabilityLineageFunction(ISuitabilityEngine engine) => _engine = engine;

    [Function("Suitability_Lineage")]
    [OpenApiOperation(operationId: "getSuitabilityLineage", tags: new[] { "suitability" }, Summary = "Get lineage")]
    [OpenApiParameter(name: "caseId", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Case ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(LineageResponse), Summary = "Lineage")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "suitability/lineage")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var caseId = query["caseId"];

        if (string.IsNullOrWhiteSpace(caseId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("caseId is required.", ct);
            return bad;
        }

        var lineage = await _engine.GetLineageAsync(caseId, ct);
        if (lineage is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteStringAsync("No lineage found for caseId.", ct);
            return notFound;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(lineage, ct);
        return ok;
    }
}
