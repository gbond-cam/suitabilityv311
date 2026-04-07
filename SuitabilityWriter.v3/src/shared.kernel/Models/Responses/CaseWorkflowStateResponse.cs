namespace Shared.Kernel.Models.Responses;

public class CaseWorkflowStateResponse
{
    public string CaseId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string Message { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public int ProgressPercentage { get; set; }
    public string NextPrompt { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string SecureDownloadUrl { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<WorkflowStepStateResponse> Steps { get; set; } = [];
}

public class WorkflowStepStateResponse
{
    public string Step { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string Message { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string StatusUrl { get; set; } = string.Empty;
    public string ArtifactUrl { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}