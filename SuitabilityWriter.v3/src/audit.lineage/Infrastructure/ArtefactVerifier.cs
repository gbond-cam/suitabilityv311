using System.Net.Http;
using System.Text.Json;

public sealed class ArtefactVerifier
{
    private readonly HttpClient _http;

    public ArtefactVerifier(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ArtefactVerificationResult>> VerifyAsync(
        IEnumerable<(string Name, string Version, string Hash, string? Url)> artefacts,
        CancellationToken ct)
    {
        var results = new List<ArtefactVerificationResult>();

        foreach (var a in artefacts)
        {
            if (string.IsNullOrWhiteSpace(a.Url))
            {
                results.Add(ArtefactVerificationResult.NotVerified(
                    a.Name, a.Version, a.Hash, "No source URL available"));
                continue;
            }

            try
            {
                using var resp = await _http.GetAsync(a.Url, ct);
                resp.EnsureSuccessStatusCode();

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var computed = await ArtefactHasher.Sha256Async(stream);

                results.Add(
                    computed.Equals(a.Hash, StringComparison.OrdinalIgnoreCase)
                        ? ArtefactVerificationResult.Verified(a.Name, a.Version, a.Hash)
                        : ArtefactVerificationResult.Mismatch(a.Name, a.Version, a.Hash, computed)
                );
            }
            catch (Exception ex)
            {
                results.Add(ArtefactVerificationResult.NotVerified(
                    a.Name, a.Version, a.Hash, ex.Message));
            }
        }

        return results;
    }
}