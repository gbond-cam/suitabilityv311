using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Template.Routing.Functions;

public class RouteTemplateFunction
{
    private readonly ILogger<RouteTemplateFunction> _logger;

    public RouteTemplateFunction(ILogger<RouteTemplateFunction> logger)
    {
        _logger = logger;
    }

    [Function("RouteTemplate")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "templates/route")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<TemplateRoutingRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.CaseId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid template routing payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Routing template for case {CaseId} and product {ProductType}.", payload.CaseId, payload.ProductType);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "template.route",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
