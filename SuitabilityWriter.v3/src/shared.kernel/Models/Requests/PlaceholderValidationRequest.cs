namespace Shared.Kernel.Models.Requests;

public class PlaceholderValidationRequest
{
    public string TemplateId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
