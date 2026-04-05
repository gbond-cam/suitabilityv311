using System.Text;
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

        var json = JsonSerializer.Serialize(record, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/RecordLineageEvent")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", record.EventId);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}