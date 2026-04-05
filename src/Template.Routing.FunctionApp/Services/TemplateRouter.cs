using System.Text.Json;

namespace Template.Routing.FunctionApp.Services;

/// <summary>
/// Policy-driven deterministic template router with safe default.
/// </summary>
public sealed class TemplateRouter : ITemplateRouter
{
    private readonly IRoutingPolicyStore _policyStore;

    public TemplateRouter(IRoutingPolicyStore policyStore)
    {
        _policyStore = policyStore;
    }

    public Task<(string templateFileName, string templateVersion, string reason)> RouteAsync(string caseEvidenceJson, CancellationToken ct)
    {
        using var policy = _policyStore.LoadPolicy();

        var def = policy.RootElement.GetProperty("default");
        var defFile = def.GetProperty("templateFileName").GetString() ?? "Consilium Suitability Report - ISA Template.docx";
        var defVer = def.GetProperty("templateVersion").GetString() ?? "v1";
        var defWhy = def.GetProperty("reason").GetString() ?? "Default routing";

        string? reportType = null;
        using (var evidence = JsonDocument.Parse(caseEvidenceJson))
        {
            if (evidence.RootElement.ValueKind == JsonValueKind.Object &&
                evidence.RootElement.TryGetProperty("reportType", out var rt) &&
                rt.ValueKind == JsonValueKind.String)
            {
                reportType = rt.GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(reportType))
            return Task.FromResult((defFile, defVer, $"{defWhy} (reportType not provided)"));

        foreach (var rule in policy.RootElement.GetProperty("rules").EnumerateArray())
        {
            var match = rule.GetProperty("match");
            if (match.TryGetProperty("reportType", out var expected) &&
                expected.ValueKind == JsonValueKind.String &&
                string.Equals(expected.GetString(), reportType, StringComparison.OrdinalIgnoreCase))
            {
                var route = rule.GetProperty("route");
                var file = route.GetProperty("templateFileName").GetString() ?? defFile;
                var ver = route.GetProperty("templateVersion").GetString() ?? defVer;
                var why = route.GetProperty("reason").GetString() ?? "Matched routing rule";
                return Task.FromResult((file, ver, why));
            }
        }

        return Task.FromResult((defFile, defVer, $"{defWhy} (no rule for reportType={reportType})"));
    }
}
