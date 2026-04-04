using System.Security.Cryptography;

public static class ArtefactHasher
{
    public static async Task<string> Sha256Async(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }
}