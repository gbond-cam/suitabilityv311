using System.Security.Cryptography;

public sealed class BundleEncryptor
{
    public EncryptedBundleEnvelope Encrypt(
        byte[] zipBytes,
        RSA recipientPublicKey,
        string recipientId,
        string recipientKeyId)
    {
        var aesKey = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(12);

        byte[] ciphertext = new byte[zipBytes.Length];
        byte[] tag = new byte[16];

        using (var aes = new AesGcm(aesKey, tag.Length))
        {
            aes.Encrypt(iv, zipBytes, ciphertext, tag);
        }

        var wrappedKey = recipientPublicKey.Encrypt(
            aesKey,
            RSAEncryptionPadding.OaepSHA256);

        return new EncryptedBundleEnvelope
        {
            Schema = "com.consilium.evidence.envelope",
            SchemaVersion = "1.0.0",
            EncryptionAlgorithm = "AES-256-GCM",
            KeyEncryptionAlgorithm = "RSA-OAEP-SHA256",
            RecipientId = recipientId,
            RecipientKeyId = recipientKeyId,
            EncryptedKey = Convert.ToBase64String(wrappedKey),
            Iv = Convert.ToBase64String(iv),
            Ciphertext = Convert.ToBase64String(ciphertext),
            AuthTag = Convert.ToBase64String(tag),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BundleType = "EvidenceBundle"
        };
    }
}
