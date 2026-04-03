public sealed record LineageRecord(
    string CaseId,
    string Stage,
    string Action,
    string ArtefactName,
    string ArtefactVersion,
    string ArtefactHash,
    string PerformedBy,
    DateTimeOffset TimestampUtc,
    object? Metadata
);
