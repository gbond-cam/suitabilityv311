using System.Security.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;

public static class PublicKeyExporter
{
    public static string ExportRsaPublicKeyPem(
        string keyVaultUrl,
        string keyName,
        string keyVersion)
    {
        var client = new KeyClient(new Uri(keyVaultUrl), new DefaultAzureCredential());

        var key = client.GetKey(keyName, keyVersion).Value;

        if (key.Key.N is null || key.Key.E is null)
            throw new InvalidOperationException("Key does not contain RSA public parameters.");

        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = key.Key.N,
            Exponent = key.Key.E
        });

        var pub = rsa.ExportSubjectPublicKeyInfo();
        return ToPem("PUBLIC KEY", pub);
    }

    private static string ToPem(string label, byte[] der)
    {
        var base64 = Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks);
        return $"-----BEGIN {label}-----\n{base64}\n-----END {label}-----\n";
    }
}
