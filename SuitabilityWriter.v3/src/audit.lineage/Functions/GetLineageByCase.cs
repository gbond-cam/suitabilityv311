using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

public sealed class GetLineageByCase
{
    private readonly ILineageReader _reader;

    public GetLineageByCase(ILineageReader reader)
    {
        _reader = reader;
    }

    [Function("GetLineageByCase")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get")]
        HttpRequestData req)
    {
        var caseId = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["caseId"];

        if (string.IsNullOrWhiteSpace(caseId))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var result = await _reader.GetByCaseIdAsync(caseId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);

        return response;
    }
}