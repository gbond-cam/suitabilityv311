using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Responses;

namespace Startup.Healthcheck.Functions;

public class StartupHealthcheckFunction
{
    private readonly ILogger<StartupHealthcheckFunction> _logger;

    public StartupHealthcheckFunction(ILogger<StartupHealthcheckFunction> logger)
    {
        _logger = logger;
    }

    [Function("StartupHealthcheck")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = "startup/healthcheck")] HttpRequestData req)
    {
        _logger.LogInformation("Startup healthcheck passed.");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new HealthcheckResponse
        {
            Service = "startup.healthcheck"
        });
        return response;
    }
}
