using System.IO;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using Template.Routing.FunctionApp.Models;
using Template.Routing.FunctionApp.Services;

namespace Template.Routing.FunctionApp.Functions;

public sealed class TemplateRouteFunction
{
    private readonly ITemplateRouter _router;

    public TemplateRouteFunction(ITemplateRouter router)
    {
        _router = router;
    }

    [Function("Template_Route")]
    [OpenApiOperation(operationId: "routeTemplate", tags: new[] { "template" }, Summary = "Route a case evidence payload to a template")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(object), Required = true, Description = "Case evidence JSON (must include reportType to match rules)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(TemplateRoutingResult), Summary = "Routing decision")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "template/route")]
        HttpRequestData req,
        CancellationToken ct)
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Request body must be case evidence JSON.", ct);
            return bad;
        }

        var (file, ver, why) = await _router.RouteAsync(body, ct);
        var result = new TemplateRoutingResult(file, ver, why);

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(result, ct);
        return ok;
    }
}
