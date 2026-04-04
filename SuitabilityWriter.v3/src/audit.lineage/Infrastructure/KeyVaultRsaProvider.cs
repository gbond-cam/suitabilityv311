using System.Security.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

public static class KeyVaultRsaProvider
{
    public static RSA Load(string vaultUrl, string keyName)
    {
        var client = new KeyClient(new Uri(vaultUrl), new DefaultAzureCredential());
        var key = client.GetKey(keyName);

        var crypto = new CryptographyClient(key.Value.Id, new DefaultAzureCredential());
        return new RSAKeyVaultAdapter(crypto);
    }

    private sealed class RSAKeyVaultAdapter : RSA
    {
        private readonly CryptographyClient _crypto;

        public RSAKeyVaultAdapter(CryptographyClient crypto) => _crypto = crypto;

        public override byte[] SignHash(byte[] hash, HashAlgorithmName alg, RSASignaturePadding padding)
            => _crypto.Sign(Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm.RS256, hash).Signature;

        public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName alg, RSASignaturePadding padding)
            => _crypto.Verify(Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm.RS256, hash, signature).IsValid;

        // Not used
        public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding) => throw new NotSupportedException();
        public override byte[] Encrypt(byte[] data, RSAEncryptionPadding padding) => throw new NotSupportedException();
        public override RSAParameters ExportParameters(bool includePrivateParameters) => throw new NotSupportedException();
        public override void ImportParameters(RSAParameters parameters) => throw new NotSupportedException();
        public override int KeySize => 2048;
    }
}