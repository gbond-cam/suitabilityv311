namespace Shared.Kernel.Models.Requests;

public class CaseBootstrapRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string SubmittedBy { get; set; } = string.Empty;
}
