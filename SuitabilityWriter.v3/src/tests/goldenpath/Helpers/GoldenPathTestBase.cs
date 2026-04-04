using System.Net.Http;

public abstract class GoldenPathTestBase
{
    protected static Task RunFullPipeline(string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        return Task.CompletedTask;
    }

    protected static Task RunPipelineWithEmergencyOverride(string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        return Task.CompletedTask;
    }

    protected static Task RunBlockedPipeline(string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        return Task.CompletedTask;
    }

    protected static async Task<string> GetLineageJson(string caseId)
    {
        using var http = TestHttpClientFactory.Create();
        var resp = await http.GetAsync($"/api/lineage/reconstruct?caseId={Uri.EscapeDataString(caseId)}");

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Lineage retrieval failed with {(int)resp.StatusCode}: {body}");
        }

        return await resp.Content.ReadAsStringAsync();
    }

    protected static Task<HttpResponseMessage> GetEvidenceBundleResponse(string caseId)
        => EvidenceBundleClient.GetEvidenceBundleResponse(caseId);

    protected static Task<string> GetEncryptedEnvelopeJson(string caseId)
        => EvidenceBundleClient.GetEncryptedEnvelopeJson(caseId);

    protected static Task<EncryptedEnvelope> GetEncryptedEnvelope(string caseId)
        => EvidenceBundleClient.GetEncryptedEnvelope(caseId);

    protected static Task<byte[]> GetEvidenceBundleZip(string caseId)
        => EvidenceBundleClient.GetEvidenceBundleZip(caseId);

    protected static async Task RevokeSigningKeyInKeyVault(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        Environment.SetEnvironmentVariable("ZIP_SIGNING_KEY_NAME", keyId);

        var publishUrl = Environment.GetEnvironmentVariable("REVOCATION_PUBLISH_URL");
        if (!string.IsNullOrWhiteSpace(publishUrl))
        {
            using var http = TestHttpClientFactory.Create();
            using var response = await http.PostAsync(publishUrl, content: null);
            response.EnsureSuccessStatusCode();
        }
    }

    protected static Task RevokeSigningKey(string keyId) => RevokeSigningKeyInKeyVault(keyId);
}

public static class TestHttpClientFactory
{
    public static HttpClient Create()
    {
        var baseUrl = Environment.GetEnvironmentVariable("GOLDEN_PATH_BASE_URL") ?? "http://localhost:7071";
        return new HttpClient
        {
            BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : $"{baseUrl}/")
        };
    }
}
