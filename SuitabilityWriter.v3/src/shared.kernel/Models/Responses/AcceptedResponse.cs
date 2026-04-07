namespace Shared.Kernel.Models.Responses;

public class AcceptedResponse
{
    public string Operation { get; set; } = string.Empty;
    public string Status { get; set; } = "accepted";
    public string CorrelationId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public int ProgressPercentage { get; set; }
    public string NextPrompt { get; set; } = string.Empty;
    public string StatusUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string SecureDownloadUrl { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
}
