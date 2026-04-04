using System.Text.Json.Serialization;

public sealed record LineageRecord(
    string EventId,
    string CaseId,
    string Stage,
    string Action,
    string ArtefactName,
    string ArtefactVersion,
    string ArtefactHash,
    string PerformedBy,
    DateTimeOffset TimestampUtc,
    object? Metadata)
{
    [JsonConstructor]
    public LineageRecord(
        string CaseId,
        string Stage,
        string Action,
        string ArtefactName,
        string ArtefactVersion,
        string ArtefactHash,
        string PerformedBy,
        DateTimeOffset TimestampUtc,
        object? Metadata)
        : this(
            EventId: string.Empty,
            CaseId: CaseId,
            Stage: Stage,
            Action: Action,
            ArtefactName: ArtefactName,
            ArtefactVersion: ArtefactVersion,
            ArtefactHash: ArtefactHash,
            PerformedBy: PerformedBy,
            TimestampUtc: TimestampUtc,
            Metadata: Metadata)
    {
    }
}
