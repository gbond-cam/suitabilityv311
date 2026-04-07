using System.Text.Json.Serialization;

public sealed record ComplianceAuditReportDto(
    [property: JsonPropertyName("caseId")] string CaseId,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("complianceStatus")] string ComplianceStatus,
    [property: JsonPropertyName("summary")] ComplianceAuditSummaryDto Summary,
    [property: JsonPropertyName("loggedActions")] IReadOnlyList<string> LoggedActions,
    [property: JsonPropertyName("regulatoryChecks")] IReadOnlyList<ComplianceCheckResultDto> RegulatoryChecks,
    [property: JsonPropertyName("immutableStorage")] ImmutableStorageDescriptorDto ImmutableStorage,
    [property: JsonPropertyName("reconstruction")] CaseReconstructionDto Reconstruction);

public sealed record ComplianceAuditSummaryDto(
    [property: JsonPropertyName("totalEvents")] int TotalEvents,
    [property: JsonPropertyName("firstEventAtUtc")] DateTimeOffset? FirstEventAtUtc,
    [property: JsonPropertyName("lastEventAtUtc")] DateTimeOffset? LastEventAtUtc,
    [property: JsonPropertyName("actors")] IReadOnlyList<string> Actors,
    [property: JsonPropertyName("artefacts")] IReadOnlyList<string> Artefacts);

public sealed record ComplianceCheckResultDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("details")] string Details);

public sealed record ImmutableStorageDescriptorDto(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("containerName")] string ContainerName,
    [property: JsonPropertyName("blobName")] string BlobName,
    [property: JsonPropertyName("contentHashSha256")] string ContentHashSha256,
    [property: JsonPropertyName("retentionUntilUtc")] DateTimeOffset RetentionUntilUtc);
