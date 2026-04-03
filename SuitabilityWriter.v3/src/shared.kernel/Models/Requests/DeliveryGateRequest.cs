namespace Shared.Kernel.Models.Requests;

public class DeliveryGateRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string DecisionBy { get; set; } = string.Empty;
}
