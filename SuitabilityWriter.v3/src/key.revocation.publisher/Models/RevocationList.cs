using System.Text.Json.Serialization;

public sealed record RevocationList(
    DateTimeOffset GeneratedAtUtc,
    string Issuer,
    IReadOnlyList<RevokedKeyEntry> Keys
);

public sealed record RevokedKeyEntry(
    string KeyName,
    string KeyVersion,
    string KeyId,                 // Full Key Vault Key Id URI
    string ThumbprintSha256,      // Stable fingerprint of public key
    string Status,                // "Active" | "Revoked"
    DateTimeOffset? RevokedAtUtc,
    string? Reason
);
