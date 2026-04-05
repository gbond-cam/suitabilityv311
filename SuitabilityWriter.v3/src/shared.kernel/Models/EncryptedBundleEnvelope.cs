namespace Shared.Kernel.Models;

/// <summary>
/// Versioned encryption envelope for transmitting an evidence bundle securely.
/// </summary>
public sealed record EncryptedBundleEnvelope
{
    public string Schema { get; init; } = "com.consilium.evidence.envelope";
    public string SchemaVersion { get; init; } = "1.0.0";

    public string EncryptionAlgorithm { get; init; } = "AES-256-GCM";
    public string KeyEncryptionAlgorithm { get; init; } = "RSA-OAEP-SHA256";

    public string RecipientId { get; init; } = default!;
    public string RecipientKeyId { get; init; } = default!;

    public string EncryptedKey { get; init; } = default!;
    public string Iv { get; init; } = default!;
    public string Ciphertext { get; init; } = default!;
    public string AuthTag { get; init; } = default!;

    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string BundleHash { get; init; } = default!;
    public string Signature { get; init; } = default!;
    public string? TimestampToken { get; init; }
}
