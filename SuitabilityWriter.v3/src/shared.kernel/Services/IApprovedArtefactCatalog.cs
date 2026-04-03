public interface IApprovedArtefactCatalog
{
    Task<ArtefactReference> GetAsync(
        string artefactName,
        string version,
        string artefactUrl,
        CancellationToken cancellationToken = default
    );
}
