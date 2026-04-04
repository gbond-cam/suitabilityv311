using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

public sealed class ArtefactBinaryDownloader
{
    private readonly HttpClient _http;

    public ArtefactBinaryDownloader(HttpClient http)
    {
        _http = http;
    }

    public async Task<(byte[] Bytes, string FileName)> DownloadAsync(
        string sourceUrl,
        string fallbackName,
        CancellationToken ct)
    {
        using var resp = await _http.GetAsync(sourceUrl, ct);
        resp.EnsureSuccessStatusCode();

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);

        var headerName =
            resp.Content.Headers.ContentDisposition?.FileNameStar ??
            resp.Content.Headers.ContentDisposition?.FileName;

        var fileName = string.IsNullOrWhiteSpace(headerName)
            ? fallbackName
            : headerName.Trim('"');

        return (bytes, SanitizeFileName(fileName));
    }

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    public static string SanitizeFileName(string name)
    {
        name = name.Replace("\\", "_").Replace("/", "_");
        name = Regex.Replace(name, @"[<>:""|?*\x00-\x1F]", "_");
        return name.Length > 120 ? name[..120] : name;
    }
}