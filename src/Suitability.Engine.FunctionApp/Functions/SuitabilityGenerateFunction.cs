using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using Suitability.Engine.FunctionApp.Services;

namespace Suitability.Engine.FunctionApp.Functions;

public sealed class SuitabilityGenerateFunction
{
    private readonly ISuitabilityEngine _engine;

    public SuitabilityGenerateFunction(ISuitabilityEngine engine) => _engine = engine;

    [Function("Suitability_Generate")]
    [OpenApiOperation(operationId: "generateSuitability", tags: new[] { "suitability" }, Summary = "Generate suitability report")]
    [OpenApiParameter(name: "caseId", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Case ID")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "suitability/generate")] HttpRequestData req,
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

        var (success, response) = await _engine.GenerateAsync(caseId, ct);
        var resp = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.Conflict);
        await resp.WriteAsJsonAsync(response, ct);
        return resp;
    }
}
