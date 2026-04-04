using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record ReconstructionEventDto
(
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc,

    [property: JsonPropertyName("stage")] string Stage,          // Validation | Routing | Generation | Delivery | Governance
    [property: JsonPropertyName("action")] string Action,        // SchemaValidated | Blocked | EmergencyOverrideApplied | ...

    [property: JsonPropertyName("performedBy")] string PerformedBy,

    [property: JsonPropertyName("artefact")] ArtefactDto Artefact,  // "N/A" for governance events

    [property: JsonPropertyName("metadata")] JsonElement Metadata    // Raw metadata for full audit fidelity
);