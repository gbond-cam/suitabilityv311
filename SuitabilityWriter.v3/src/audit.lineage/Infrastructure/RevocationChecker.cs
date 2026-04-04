using System.Text.Json;

public sealed class RevocationChecker
{
    private readonly HttpClient _http;
    private readonly string _revocationListUrl;

    public RevocationChecker(HttpClient http, string revocationListUrl)
    {
        _http = http;
        _revocationListUrl = revocationListUrl;
    }

    public async Task<string> GetValidatedRevocationListAsync(string keyId, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(_revocationListUrl, ct);
        using var doc = JsonDocument.Parse(json);

        var key = doc.RootElement
            .GetProperty("keys")
            .EnumerateArray()
            .FirstOrDefault(k => k.GetProperty("keyId").GetString() == keyId);

        if (key.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Signing key '{keyId}' not present in revocation list");
        }

        if (!string.Equals(key.GetProperty("status").GetString(), "Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Signing key '{keyId}' is revoked");
        }

        return json;
    }

    public async Task EnsureKeyIsActiveAsync(string keyId, CancellationToken ct)
    {
        _ = await GetValidatedRevocationListAsync(keyId, ct);
    }
}
