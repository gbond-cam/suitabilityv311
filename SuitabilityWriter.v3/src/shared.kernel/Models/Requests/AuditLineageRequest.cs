namespace Shared.Kernel.Models.Requests;

public class AuditLineageRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
}
