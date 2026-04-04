using System.Security.Cryptography;

public sealed class BundleDecryptor
{
    public byte[] Decrypt(EncryptedBundleEnvelope encrypted, RSA rsaPrivateKey)
    {
        var aesKey = rsaPrivateKey.Decrypt(
            Convert.FromBase64String(encrypted.EncryptedKey),
            RSAEncryptionPadding.OaepSHA256);

        var iv = Convert.FromBase64String(encrypted.Iv);
        var ciphertext = Convert.FromBase64String(encrypted.Ciphertext);
        var tag = Convert.FromBase64String(encrypted.AuthTag);

        var zipBytes = new byte[ciphertext.Length];

        using var aes = new AesGcm(aesKey, tag.Length);
        aes.Decrypt(iv, ciphertext, tag, zipBytes);

        return zipBytes;
    }
}
