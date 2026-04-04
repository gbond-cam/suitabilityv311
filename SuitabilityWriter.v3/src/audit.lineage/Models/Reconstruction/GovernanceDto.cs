using System;
using System.Text.Json.Serialization;

public sealed record GovernanceDto
(
    [property: JsonPropertyName("isBlocked")] bool IsBlocked,
    [property: JsonPropertyName("blockReasonCode")] string? BlockReasonCode,

    [property: JsonPropertyName("emergencyOverrideApplied")] bool EmergencyOverrideApplied,
    [property: JsonPropertyName("incidentId")] string? IncidentId,

    [property: JsonPropertyName("adviserApproved")] bool AdviserApproved,
    [property: JsonPropertyName("adviserId")] string? AdviserId,

    [property: JsonPropertyName("deliveredAtUtc")] DateTimeOffset? DeliveredAtUtc
);