using System.Net.Http.Json;
using System.Text.Json;

public sealed class AuditLineageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;

    public AuditLineageClient(HttpClient http)
    {
        _http = http;
    }

    public async Task RecordAsync(LineageRecord record, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(record.EventId))
        {
            record = record with { EventId = IdempotencyKey.From(record) };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/RecordLineageEvent")
        {
            Content = JsonContent.Create(record, options: JsonOptions)
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", record.EventId);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}