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
}
