using Suitability.Engine.FunctionApp.Models;

namespace Suitability.Engine.FunctionApp.Services;

public interface ISuitabilityEngine
{
    Task<(bool success, object response)> GenerateAsync(string caseId, CancellationToken ct);
    Task<LineageResponse?> GetLineageAsync(string caseId, CancellationToken ct);
}
