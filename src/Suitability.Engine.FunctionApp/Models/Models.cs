namespace Suitability.Engine.FunctionApp.Models;

public record EngineArtefactReference(string Name, string Version, string Status, string SharePointUrl);

public record ReportInfo(string Format, string Url);

public record SchemaVersions(string CaseEvidence, string SuitabilityInput);

public record StatusResponse(string CaseId, string Status, DateTimeOffset Timestamp);

public record GenerateSuccessResponse(
    string Status,
    string Message,
    string CaseId,
    ReportInfo Report,
    List<EngineArtefactReference> ArtefactsUsed,
    SchemaVersions SchemaVersions);

public record LineageResponse(
    string CaseId,
    List<EngineArtefactReference> ArtefactsUsed,
    Dictionary<string, string> Schemas,
    DateTimeOffset GeneratedAtUtc,
    string EngineVersion);

public record GenerateBlockedResponse(
    string Status,
    string ReasonCode,
    string Message,
    string CaseId);

public record ErrorResponse(
    string Status,
    string Message,
    string CorrelationId);
