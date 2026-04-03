using System.Security.Cryptography;

public sealed class ApprovedArtefactCatalog : IApprovedArtefactCatalog
{
    private readonly HttpClient _http;

    public ApprovedArtefactCatalog(HttpClient http)
    {
        _http = http;
    }

    public async Task<ArtefactReference> GetAsync(
        string artefactName,
        string version,
        string artefactUrl,
        CancellationToken cancellationToken = default
    )
    {
        var bytes = await _http.GetByteArrayAsync(artefactUrl, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        return new ArtefactReference(
            Name: artefactName,
            Version: version,
            Hash: hash
        );
    }
}
