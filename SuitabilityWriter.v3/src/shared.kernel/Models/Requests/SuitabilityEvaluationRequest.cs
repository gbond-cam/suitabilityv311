namespace Shared.Kernel.Models.Requests;

public class SuitabilityEvaluationRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string AdviceScope { get; set; } = string.Empty;
    public string RiskProfile { get; set; } = string.Empty;
    public ClientDataIntakeRequest? ClientData { get; set; }
    public EvidenceIntakeRequest? Evidence { get; set; }
    public ReportGenerationRequest Report { get; set; } = new();
    public bool AutoStartEvaluation { get; set; } = true;
    public bool AutoGenerateReport { get; set; } = true;
    public bool WaitForCompletion { get; set; } = true;
    public int EvaluationPollIntervalMs { get; set; } = 250;
    public int MaxEvaluationPollAttempts { get; set; } = 5;
}
