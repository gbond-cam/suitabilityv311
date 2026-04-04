using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;

public sealed class Rfc3161TimestampClient
{
    private readonly HttpClient _http;
    private readonly Uri _tsaUrl;

    public Rfc3161TimestampClient(HttpClient http, string tsaUrl)
    {
        _http = http;
        _tsaUrl = new Uri(tsaUrl);
    }

    public async Task<byte[]> TimestampAsync(byte[] hash, CancellationToken ct)
    {
        var req = Rfc3161TimestampRequest.CreateFromHash(
            hash,
            HashAlgorithmName.SHA256,
            requestSignerCertificates: false
        );

        var content = new ByteArrayContent(req.Encode());
        content.Headers.ContentType = new("application/timestamp-query");

        using var resp = await _http.PostAsync(_tsaUrl, content, ct);
        resp.EnsureSuccessStatusCode();

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);

        _ = req.ProcessResponse(bytes, out _);
        return bytes;
    }
}