namespace Shared.Kernel.Models.Requests;

public class ReportGenerationRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
}
