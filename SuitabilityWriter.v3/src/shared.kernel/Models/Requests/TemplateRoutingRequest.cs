namespace Shared.Kernel.Models.Requests;

public class TemplateRoutingRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string AdviceType { get; set; } = string.Empty;
}
