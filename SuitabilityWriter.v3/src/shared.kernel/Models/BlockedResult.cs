public sealed record BlockedResult(
    string ReasonCode,
    string Message,
    string CaseId,
    string CorrelationId
);