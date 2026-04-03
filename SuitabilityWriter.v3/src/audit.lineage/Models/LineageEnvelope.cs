public sealed record LineageEnvelope
(
    string CaseId,
    IReadOnlyList<LineageRecord> Records
);