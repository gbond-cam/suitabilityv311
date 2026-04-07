public static class LineageStages
{
    public const string Validation = "Validation";
    public const string Routing = "Routing";
    public const string Generation = "Generation";
    public const string Delivery = "Delivery";
    public const string Governance = "Governance";
    public const string Audit = "Audit";
}

public static class LineageActions
{
    public const string Blocked = "Blocked";
    public const string EmergencyOverrideApplied = "EmergencyOverrideApplied";
    public const string IncidentCreated = "IncidentCreated";
    public const string IncidentClosed = "IncidentClosed";
    public const string SlaWarningIssued = "SlaWarningIssued";
    public const string EvidenceUploaded = "EvidenceUploaded";
    public const string EvidenceUploadFailed = "EvidenceUploadFailed";
    public const string SuitabilityEvaluationRequested = "SuitabilityEvaluationRequested";
    public const string SuitabilityEvaluationCompleted = "SuitabilityEvaluationCompleted";
    public const string SuitabilityEvaluationFailed = "SuitabilityEvaluationFailed";
    public const string ReportGenerationRequested = "ReportGenerationRequested";
    public const string ReportGenerated = "ReportGenerated";
    public const string ReportGenerationBlocked = "ReportGenerationBlocked";
    public const string AuditReportGenerated = "AuditReportGenerated";
}
