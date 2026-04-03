namespace Shared.Kernel.Models.Requests;

public class SuitabilityEvaluationRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string AdviceScope { get; set; } = string.Empty;
    public string RiskProfile { get; set; } = string.Empty;
}
