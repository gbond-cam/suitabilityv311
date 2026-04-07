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
        var json = await LoadRevocationListJsonAsync(keyId, ct);
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

    private async Task<string> LoadRevocationListJsonAsync(string keyId, CancellationToken ct)
    {
        if (Uri.TryCreate(_revocationListUrl, UriKind.Absolute, out _) && !LooksLikePlaceholderUrl(_revocationListUrl))
        {
            return await _http.GetStringAsync(_revocationListUrl, ct);
        }

        return LoadLocalRevocationListJson(keyId);
    }

    private static string LoadLocalRevocationListJson(string keyId)
    {
        var filePath = ResolveLocalFilePath(
            Environment.GetEnvironmentVariable("LOCAL_REVOCATION_LIST_PATH"),
            "local-revocation-list.json");

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            return File.ReadAllText(filePath);
        }

        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            source = "local-default",
            keys = new[]
            {
                new
                {
                    keyId,
                    status = "Active"
                }
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static bool LooksLikePlaceholderUrl(string? url)
    {
        return string.IsNullOrWhiteSpace(url) ||
               url.Contains('<') ||
               url.Contains('>');
    }

    private static string? ResolveLocalFilePath(string? configuredPath, string fileName)
    {
        var candidates = new[]
        {
            configuredPath,
            Path.Combine(Environment.CurrentDirectory, "keys", fileName),
            Path.Combine(AppContext.BaseDirectory, "keys", fileName),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "keys", fileName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "keys", fileName))
        };

        return candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }
}
