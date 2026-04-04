using System.Collections.Generic;
using System.Text.Json.Serialization;

public sealed record CaseReconstructionDto
(
    [property: JsonPropertyName("caseId")] string CaseId,

    // Ordered, immutable timeline
    [property: JsonPropertyName("timeline")] IReadOnlyList<ReconstructionEventDto> Timeline,

    // Convenience summaries (derived from timeline, not stored)
    [property: JsonPropertyName("governance")] GovernanceDto Governance,
    [property: JsonPropertyName("metrics")] MetricsDto Metrics
);