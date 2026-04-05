namespace Template.Routing.FunctionApp.Models;

/// <summary>
/// Deterministic routing output.
/// </summary>
public sealed record TemplateRoutingResult(
    string TemplateFileName,
    string TemplateVersion,
    string Reason
);
