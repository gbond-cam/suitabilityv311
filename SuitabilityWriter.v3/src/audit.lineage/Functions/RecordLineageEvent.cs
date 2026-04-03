using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Net;

public sealed class RecordLineageEvent
{
    private readonly ILineageWriter _writer;
    private readonly ILogger<RecordLineageEvent> _logger;

    public RecordLineageEvent(
        ILineageWriter writer,
        ILogger<RecordLineageEvent> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    [Function("RecordLineageEvent")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")]
        HttpRequestData req)
    {
        var record = await req.ReadFromJsonAsync<LineageRecord>();

        if (record is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            return bad;
        }

        if (string.IsNullOrWhiteSpace(record.ArtefactHash) &&
            record.Metadata is IDictionary<string, object> metadata &&
            metadata.TryGetValue("artefactPath", out var artefactPathValue) &&
            artefactPathValue is string artefactPath &&
            !string.IsNullOrWhiteSpace(artefactPath) &&
            File.Exists(artefactPath))
        {
            var bytes = File.ReadAllBytes(artefactPath);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            record = record with { ArtefactHash = hash };
        }

        await _writer.AppendAsync(record);

        var ok = req.CreateResponse(HttpStatusCode.Accepted);
        return ok;
    }
}
