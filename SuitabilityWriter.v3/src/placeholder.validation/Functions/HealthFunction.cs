using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;

namespace Placeholder.Validation.Functions;

public class ValidatePlaceholderFunction
{
    private readonly ILogger<ValidatePlaceholderFunction> _logger;

    public ValidatePlaceholderFunction(ILogger<ValidatePlaceholderFunction> logger)
    {
        _logger = logger;
    }

    [Function("ValidatePlaceholder")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "placeholders/validate")] HttpRequestData req)
    {
        var payload = await req.ReadFromJsonAsync<PlaceholderValidationRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.TemplateId))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("A valid placeholder validation payload is required.");
            return badRequest;
        }

        _logger.LogInformation("Validating placeholders for template {TemplateId}.", payload.TemplateId);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new AcceptedResponse
        {
            Operation = "placeholder.validate",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        return response;
    }
}
