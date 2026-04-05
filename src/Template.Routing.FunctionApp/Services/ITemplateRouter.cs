namespace Template.Routing.FunctionApp.Services;

/// <summary>
/// Routes a case evidence JSON payload to an approved template.
/// </summary>
public interface ITemplateRouter
{
    Task<(string templateFileName, string templateVersion, string reason)> RouteAsync(string caseEvidenceJson, CancellationToken ct);
}
