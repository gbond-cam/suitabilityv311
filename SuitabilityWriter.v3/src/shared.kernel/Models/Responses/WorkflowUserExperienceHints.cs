namespace Shared.Kernel.Models.Responses;

public static class WorkflowUserExperienceHints
{
    public static void Apply(CaseWorkflowStateResponse state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.CurrentStage = DetermineCurrentStage(state);
        state.ProgressPercentage = DetermineProgressPercentage(state);
        state.NextPrompt = DetermineNextPrompt(state);
        state.SecureDownloadUrl = NormalizeSecureUrl(state.DownloadUrl);
    }

    public static void Apply(AcceptedResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var normalizedSecureUrl = NormalizeSecureUrl(string.IsNullOrWhiteSpace(response.SecureDownloadUrl) ? response.DownloadUrl : response.SecureDownloadUrl);

        response.SecureDownloadUrl = normalizedSecureUrl;
        response.CurrentStage = string.IsNullOrWhiteSpace(response.CurrentStage)
            ? DetermineCurrentStage(response.Operation, response.Status)
            : response.CurrentStage;
        response.ProgressPercentage = response.ProgressPercentage > 0
            ? response.ProgressPercentage
            : DetermineProgressPercentage(response.Operation, response.Status, normalizedSecureUrl);
        response.NextPrompt = string.IsNullOrWhiteSpace(response.NextPrompt)
            ? DetermineNextPrompt(response.Status, normalizedSecureUrl)
            : response.NextPrompt;
    }

    public static void Apply(ValidationProblemResponse response, bool clientDataMissing)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Prompt = clientDataMissing
            ? "Please upload client data first."
            : "Please complete the fact-find before starting evaluation.";
        response.SuggestedAction = clientDataMissing ? "upload-client-data" : "complete-fact-find";
    }

    private static string DetermineCurrentStage(CaseWorkflowStateResponse state)
    {
        var activeStep = state.Steps.FirstOrDefault(step => string.Equals(step.Status, "in-progress", StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(step.Status, "retrying", StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(step.Status, "queued", StringComparison.OrdinalIgnoreCase));
        if (activeStep is not null)
        {
            return ToStageLabel(activeStep.Step);
        }

        if (string.Equals(state.Status, "completed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(state.DownloadUrl))
        {
            return "Report ready";
        }

        var latestCompleted = state.Steps.LastOrDefault(step => string.Equals(step.Status, "completed", StringComparison.OrdinalIgnoreCase));
        return latestCompleted is not null ? ToStageLabel(latestCompleted.Step) : "Awaiting client data";
    }

    private static string DetermineCurrentStage(string operation, string status)
    {
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) && string.Equals(operation, "report.generate", StringComparison.OrdinalIgnoreCase))
        {
            return "Report ready";
        }

        return ToStageLabel(operation);
    }

    private static int DetermineProgressPercentage(CaseWorkflowStateResponse state)
    {
        var upload = state.Steps.FirstOrDefault(step => string.Equals(step.Step, "data.upload.validation", StringComparison.OrdinalIgnoreCase));
        var evaluate = state.Steps.FirstOrDefault(step => string.Equals(step.Step, "suitability.evaluate", StringComparison.OrdinalIgnoreCase));
        var report = state.Steps.FirstOrDefault(step => string.Equals(step.Step, "report.generate", StringComparison.OrdinalIgnoreCase));

        if (IsCompleted(report) || string.Equals(state.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (IsActive(report))
        {
            return 85;
        }

        if (IsCompleted(evaluate))
        {
            return 67;
        }

        if (IsActive(evaluate))
        {
            return 50;
        }

        if (IsCompleted(upload))
        {
            return 34;
        }

        if (IsActive(upload))
        {
            return 15;
        }

        return 0;
    }

    private static int DetermineProgressPercentage(string operation, string status, string secureDownloadUrl)
    {
        if (!string.IsNullOrWhiteSpace(secureDownloadUrl) || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(operation, "report.generate", StringComparison.OrdinalIgnoreCase) ? 100 : 67;
        }

        if (string.Equals(operation, "report.generate", StringComparison.OrdinalIgnoreCase))
        {
            return 85;
        }

        if (string.Equals(operation, "suitability.evaluate", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(status, "awaiting-action", StringComparison.OrdinalIgnoreCase) ? 34 : 50;
        }

        return 15;
    }

    private static string DetermineNextPrompt(CaseWorkflowStateResponse state)
    {
        if (!string.IsNullOrWhiteSpace(state.DownloadUrl) && string.Equals(state.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return "Your secure report download link is ready.";
        }

        if (string.Equals(state.Status, "awaiting-action", StringComparison.OrdinalIgnoreCase))
        {
            return "Shall I start the evaluation now?";
        }

        if (string.Equals(state.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return "Please upload client data first.";
        }

        if (string.Equals(state.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Please review the validation errors and try again.";
        }

        return "I’ll keep you updated as each stage completes.";
    }

    private static string DetermineNextPrompt(string status, string secureDownloadUrl)
    {
        if (!string.IsNullOrWhiteSpace(secureDownloadUrl) && string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return "Your secure report download link is ready.";
        }

        if (string.Equals(status, "awaiting-action", StringComparison.OrdinalIgnoreCase))
        {
            return "Shall I start the evaluation now?";
        }

        return "I’ll keep you updated as each stage completes.";
    }

    private static string NormalizeSecureUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed) && string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? parsed.ToString()
            : string.Empty;
    }

    private static bool IsCompleted(WorkflowStepStateResponse? step) => step is not null && string.Equals(step.Status, "completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsActive(WorkflowStepStateResponse? step) => step is not null &&
        (string.Equals(step.Status, "in-progress", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(step.Status, "retrying", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(step.Status, "queued", StringComparison.OrdinalIgnoreCase));

    private static string ToStageLabel(string? stepName)
    {
        return stepName?.ToLowerInvariant() switch
        {
            "data.upload.validation" => "Client data upload",
            "suitability.evaluate" => "Suitability evaluation",
            "report.generate" => "Report generation",
            _ => string.IsNullOrWhiteSpace(stepName) ? "Workflow update" : stepName
        };
    }
}
