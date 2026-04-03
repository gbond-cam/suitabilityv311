namespace Shared.Kernel.Models.Requests;

public class EvidenceIntakeRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}
