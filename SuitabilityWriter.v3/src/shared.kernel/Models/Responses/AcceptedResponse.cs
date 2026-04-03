namespace Shared.Kernel.Models.Responses;

public class AcceptedResponse
{
    public string Operation { get; set; } = string.Empty;
    public string Status { get; set; } = "accepted";
    public string CorrelationId { get; set; } = string.Empty;
}
