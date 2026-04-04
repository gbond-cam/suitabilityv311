using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys;

public static class RsaThumbprint
{
    public static string ComputeSha256Hex(KeyVaultKey key)
    {
        // Key must have RSA parameters (n,e)
        var jwk = key.Key;
        if (jwk.Kty is null || !jwk.Kty.ToString().Contains("Rsa", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Thumbprint helper currently supports RSA keys only.");

        if (jwk.N is null || jwk.E is null)
            throw new InvalidOperationException("RSA key missing N/E parameters.");

        // Stable input: kty|base64url(n)|base64url(e)
        var input = $"RSA|{Base64Url(jwk.N)}|{Base64Url(jwk.E)}";
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}