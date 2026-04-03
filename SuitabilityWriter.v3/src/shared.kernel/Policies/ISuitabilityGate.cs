public interface ISuitabilityGate
{
    Task<GateResult> EvaluateAsync(string caseId, CancellationToken ct);
}