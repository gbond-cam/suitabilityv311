using System.Security.Cryptography;
using System.Text;

public sealed class ZipSigner
{
    private readonly RSA _rsa;

    public ZipSigner(RSA rsa)
    {
        _rsa = rsa;
    }

    public byte[] ComputeSha256(byte[] zipBytes)
        => SHA256.HashData(zipBytes);

    public byte[] SignHash(byte[] hash)
        => _rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    public static string ToHex(byte[] bytes)
        => Convert.ToHexString(bytes);
}
