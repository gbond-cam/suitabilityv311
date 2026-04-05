using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Suitability.Engine.FunctionApp.Services;

namespace Suitability.Engine.FunctionApp.Functions;

public sealed class SuitabilityStatusFunction
{
    private readonly IStatusStore _status;

    public SuitabilityStatusFunction(IStatusStore status) => _status = status;

    [Function("Suitability_Status")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "suitability/status")] HttpRequestData req,
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

        var status = await _status.GetAsync(caseId, ct);
        if (status is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteStringAsync("No status found for caseId.", ct);
            return nf;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(status, ct);
        return ok;
    }
}
