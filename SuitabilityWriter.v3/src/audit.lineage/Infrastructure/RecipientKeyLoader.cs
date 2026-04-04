using System.Security.Cryptography;

public static class RecipientKeyLoader
{
    public static RSA LoadFromPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}
