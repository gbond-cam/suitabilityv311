using System.Text;
using System.Text.Json;

public sealed class SharedAuditLineageClient : ILineageRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public SharedAuditLineageClient(HttpClient http)
    {
        _http = http;
    }

    public Task RecordAsync(LineageRecord record)
        => RecordAsync(record, CancellationToken.None);

    public async Task RecordAsync(LineageRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_http.BaseAddress is null)
        {
            return;
        }

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
