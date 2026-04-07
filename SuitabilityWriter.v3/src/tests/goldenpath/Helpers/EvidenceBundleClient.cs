using System.Security.Cryptography;
using System.Text.Json;

public static class EvidenceBundleClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<HttpResponseMessage> GetEvidenceBundleResponse(string caseId)
    {
        var http = TestHttpClientFactory.Create();
        var resp = await http.GetAsync($"/api/lineage/evidence-bundle?caseId={caseId}");

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            http.Dispose();
            resp.Dispose();
            throw new InvalidOperationException(
                $"Evidence bundle export failed with {(int)resp.StatusCode}: {body}");
        }

        return resp;
    }

    public static async Task<string> GetEncryptedEnvelopeJson(string caseId)
    {
        using var resp = await GetEvidenceBundleResponse(caseId);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<EncryptedEnvelope> GetEncryptedEnvelope(string caseId)
    {
        var json = await GetEncryptedEnvelopeJson(caseId);
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(json, JsonOptions);

        return envelope ?? throw new InvalidOperationException("Encrypted evidence bundle response could not be deserialized.");
    }

    public static async Task<byte[]> GetEvidenceBundleZip(string caseId)
    {
        using var resp = await GetEvidenceBundleResponse(caseId);

        if (string.Equals(resp.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            var json = await resp.Content.ReadAsStringAsync();
            var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(json, JsonOptions)
                ?? throw new InvalidOperationException("Encrypted evidence bundle response could not be deserialized.");

            return DecryptZipBytes(envelope);
        }

        return await resp.Content.ReadAsByteArrayAsync();
    }

    private static byte[] DecryptZipBytes(EncryptedEnvelope envelope)
    {
        var privateKeyPem = Environment.GetEnvironmentVariable("AUDITOR_PRIVATE_KEY_PEM");

        if (!string.IsNullOrWhiteSpace(privateKeyPem) && File.Exists(privateKeyPem))
        {
            privateKeyPem = File.ReadAllText(privateKeyPem);
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            var keyPath = Environment.GetEnvironmentVariable("AUDITOR_PRIVATE_KEY_PEM_FILE");
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                var defaultKeyPath = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "audit.lineage", "keys", "test-auditor-private.pem"));

                if (File.Exists(defaultKeyPath))
                {
                    keyPath = defaultKeyPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath))
            {
                privateKeyPem = File.ReadAllText(keyPath);
            }
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new InvalidOperationException(
                "AUDITOR_PRIVATE_KEY_PEM or AUDITOR_PRIVATE_KEY_PEM_FILE must be configured for golden-path ZIP inspection of encrypted evidence bundles.");
        }

        var normalizedPrivateKeyPem = privateKeyPem.Replace("\\n", "\n");
        var privateKeyPassphrase = Environment.GetEnvironmentVariable("AUDITOR_PRIVATE_KEY_PASSPHRASE") ?? "LocalTestOnly123!";

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(normalizedPrivateKeyPem);
        }
        catch (ArgumentException)
        {
            rsa.ImportFromEncryptedPem(normalizedPrivateKeyPem, privateKeyPassphrase);
        }

        var aesKey = rsa.Decrypt(Convert.FromBase64String(envelope.EncryptedKey), RSAEncryptionPadding.OaepSHA256);
        var iv = Convert.FromBase64String(envelope.Iv);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.AuthTag);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(aesKey, tag.Length);
        aes.Decrypt(iv, ciphertext, tag, plaintext);

        return plaintext;
    }
}

public sealed record EncryptedEnvelope
{
    public string Schema { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = string.Empty;
    public string EncryptionAlgorithm { get; init; } = string.Empty;
    public string KeyEncryptionAlgorithm { get; init; } = string.Empty;
    public string RecipientId { get; init; } = string.Empty;
    public string RecipientKeyId { get; init; } = string.Empty;
    public string EncryptedKey { get; init; } = string.Empty;
    public string Iv { get; init; } = string.Empty;
    public string Ciphertext { get; init; } = string.Empty;
    public string AuthTag { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string BundleType { get; init; } = string.Empty;
}