namespace Shared.Kernel.Models.Requests;

public class PlaceholderValidationRequest
{
    public string TemplateId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public ArtefactReference Artefact { get; set; } = new(string.Empty, string.Empty, string.Empty);
}
