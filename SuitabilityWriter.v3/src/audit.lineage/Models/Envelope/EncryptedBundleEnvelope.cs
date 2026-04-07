using System.Text.Json.Serialization;

public sealed record EncryptedBundleEnvelope
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "com.consilium.evidence.envelope";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "1.0.0";

    [JsonPropertyName("encryptionAlgorithm")]
    public string EncryptionAlgorithm { get; init; } = "AES-256-GCM";

    [JsonPropertyName("keyEncryptionAlgorithm")]
    public string KeyEncryptionAlgorithm { get; init; } = "RSA-OAEP-SHA256";

    [JsonPropertyName("recipientId")]
    public string RecipientId { get; init; } = default!;

    [JsonPropertyName("recipientKeyId")]
    public string RecipientKeyId { get; init; } = default!;

    [JsonPropertyName("encryptedKey")]
    public string EncryptedKey { get; init; } = default!;

    [JsonPropertyName("iv")]
    public string Iv { get; init; } = default!;

    [JsonPropertyName("ciphertext")]
    public string Ciphertext { get; init; } = default!;

    [JsonPropertyName("authTag")]
    public string AuthTag { get; init; } = default!;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    [JsonPropertyName("bundleType")]
    public string BundleType { get; init; } = "EvidenceBundle";
}
