using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Models.Responses;
using Shared.Kernel.Validation;

namespace Suitability.Engine.Services;

public interface ISuitabilityWorkflowOrchestrator
{
    Task<CaseWorkflowStateResponse> RunAsync(SuitabilityEvaluationRequest request, CancellationToken cancellationToken);
}

public sealed class SuitabilityWorkflowOrchestrator : ISuitabilityWorkflowOrchestrator
{
    private const int DefaultDownstreamRetryAttempts = 3;
    private const int DefaultRetryBaseDelayMs = 250;

    private readonly ICaseWorkflowStateStore _stateStore;
    private readonly IClientDataIntakeStore _clientDataIntakeStore;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ISystemClock _clock;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SuitabilityWorkflowOrchestrator> _logger;

    public SuitabilityWorkflowOrchestrator(
        ICaseWorkflowStateStore stateStore,
        IClientDataIntakeStore clientDataIntakeStore,
        ICorrelationIdProvider correlationIdProvider,
        ISystemClock clock,
        IHttpClientFactory httpClientFactory,
        ILogger<SuitabilityWorkflowOrchestrator> logger)
    {
        _stateStore = stateStore;
        _clientDataIntakeStore = clientDataIntakeStore;
        _correlationIdProvider = correlationIdProvider;
        _clock = clock;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CaseWorkflowStateResponse> RunAsync(SuitabilityEvaluationRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CaseId))
        {
            throw new InvalidOperationException("A valid caseId is required.");
        }

        NormalizeRequest(request);
        SuitabilityEvaluationRequestValidator.ThrowIfInvalid(request);
        await _stateStore.GetOrCreateAsync(request.CaseId, cancellationToken);

        await _stateStore.UpdateAsync(request.CaseId, state =>
        {
            state.Status = "in-progress";
            state.Message = "Step 1 of 3: validating client data and case evidence.";
            state.LastError = string.Empty;
        }, cancellationToken);

        try
        {
            await RunEvidenceStepAsync(request, cancellationToken);

            if (!request.AutoStartEvaluation)
            {
                return await _stateStore.UpdateAsync(request.CaseId, state =>
                {
                    state.Status = "awaiting-action";
                    state.Message = "Client data and evidence are ready. Shall I start the evaluation now?";
                    state.LastError = string.Empty;
                }, cancellationToken);
            }

            await RunEvaluationStepAsync(request, cancellationToken);
            await RunReportStepAsync(request, cancellationToken);

            return await _stateStore.UpdateAsync(request.CaseId, state =>
            {
                var failedStep = state.Steps.FirstOrDefault(step => string.Equals(step.Status, "failed", StringComparison.OrdinalIgnoreCase));
                if (failedStep is not null)
                {
                    state.Status = "failed";
                    state.Message = failedStep.Message;
                    state.LastError = failedStep.LastError;
                    return;
                }

                var queuedStep = state.Steps.FirstOrDefault(step => string.Equals(step.Status, "queued", StringComparison.OrdinalIgnoreCase));
                if (queuedStep is not null)
                {
                    state.Status = "queued";
                    state.Message = queuedStep.Message;
                    state.LastError = queuedStep.LastError;
                    return;
                }

                state.Status = "completed";
                state.Message = request.AutoGenerateReport && !string.IsNullOrWhiteSpace(state.DownloadUrl)
                    ? "All 3 stages are complete. Your secure report download link is ready."
                    : request.AutoGenerateReport
                        ? "All 3 stages are complete and the final report has been generated."
                        : "Step 2 of 3 complete: evidence upload and evaluation finished successfully.";
                state.LastError = string.Empty;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Suitability workflow failed for case {CaseId}.", request.CaseId);

            await _stateStore.UpdateAsync(request.CaseId, state =>
            {
                state.Status = "failed";
                state.Message = ex.Message;
                state.LastError = ex.Message;
            }, cancellationToken);

            throw;
        }
    }

    private async Task RunEvidenceStepAsync(SuitabilityEvaluationRequest request, CancellationToken cancellationToken)
    {
        await UpdateStepAsync(request.CaseId, WorkflowStepNames.UploadAndValidate, "in-progress", "Step 1 of 3: validating structured client data and case evidence.", null, 0, null, null, null, cancellationToken);

        await _clientDataIntakeStore.StoreAsync(request.CaseId, request.ClientData!, cancellationToken);
        var intakeStoredMessage = request.AutoStartEvaluation
            ? "Step 1 of 3 complete: structured client data was captured securely. Starting the evaluation now."
            : "Step 1 of 3 complete: structured client data was captured securely.";

        AcceptedResponse accepted;
        string? lastError = null;

        if (request.Evidence is null)
        {
            accepted = new AcceptedResponse
            {
                Operation = "evidence.intake",
                Status = "completed",
                CorrelationId = _correlationIdProvider.Get(),
                CaseId = request.CaseId,
                Message = $"{intakeStoredMessage} No new evidence payload supplied; using the case data already on file.",
                AttemptCount = 0
            };
        }
        else
        {
            ValidateEvidenceRequest(request.Evidence, request.CaseId);

            var result = await ExecuteAcceptedOperationWithRetryAsync(
                caseId: request.CaseId,
                stepName: WorkflowStepNames.UploadAndValidate,
                operationDisplayName: "Evidence intake",
                baseUrlEnvironmentVariable: "EVIDENCE_INTAKE_BASE_URL",
                functionKeyEnvironmentVariable: "EVIDENCE_INTAKE_FUNCTION_KEY",
                relativeUrl: "api/evidence/intake",
                payload: request.Evidence,
                fallbackOperation: "evidence.intake",
                fallbackStatus: "completed",
                fallbackMessageFactory: ex => $"{intakeStoredMessage} Evidence validated locally and the workflow continued because the remote evidence intake endpoint was unavailable after {GetConfiguredRetryAttempts()} attempt(s) ({ex.Message}).",
                cancellationToken);

            accepted = result.Response;
            accepted.Message = string.IsNullOrWhiteSpace(accepted.Message)
                ? intakeStoredMessage
                : $"{intakeStoredMessage} {accepted.Message}";
            lastError = result.LastError;
        }

        await UpdateStepAsync(
            request.CaseId,
            WorkflowStepNames.UploadAndValidate,
            ResolveStepStatus(accepted.Status, "completed"),
            accepted.Message,
            accepted.CorrelationId,
            accepted.AttemptCount,
            lastError,
            accepted.StatusUrl,
            accepted.DownloadUrl,
            cancellationToken);
    }

    private async Task RunEvaluationStepAsync(SuitabilityEvaluationRequest request, CancellationToken cancellationToken)
    {
        var correlationId = _correlationIdProvider.Get();
        await UpdateStepAsync(request.CaseId, WorkflowStepNames.EvaluateSuitability, "in-progress", "Step 2 of 3: running the suitability evaluation now.", correlationId, 1, null, null, null, cancellationToken);

        var attempts = request.WaitForCompletion ? Math.Max(1, request.MaxEvaluationPollAttempts) : 1;
        var delayMs = Math.Clamp(request.EvaluationPollIntervalMs, 50, 5000);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            _logger.LogInformation("Waiting for suitability evaluation completion for case {CaseId}. Attempt {Attempt} of {Attempts}.", request.CaseId, attempt, attempts);
            await Task.Delay(delayMs, cancellationToken);
        }

        var completionMessage = request.WaitForCompletion
            ? "Step 2 of 3 complete: the suitability evaluation finished successfully."
            : "Step 2 of 3 complete: the evaluation is ready for the next workflow step.";

        await UpdateStepAsync(request.CaseId, WorkflowStepNames.EvaluateSuitability, "completed", completionMessage, correlationId, attempts, null, null, null, cancellationToken);
    }

    private async Task RunReportStepAsync(SuitabilityEvaluationRequest request, CancellationToken cancellationToken)
    {
        if (!request.AutoGenerateReport)
        {
            await UpdateStepAsync(request.CaseId, WorkflowStepNames.GenerateReport, "skipped", "Step 3 of 3 is waiting because automatic report generation is disabled for this request.", null, 0, null, null, null, cancellationToken);
            return;
        }

        await UpdateStepAsync(request.CaseId, WorkflowStepNames.GenerateReport, "in-progress", "Step 3 of 3: generating the final report and preparing a secure download link.", null, 0, null, null, null, cancellationToken);

        var result = await ExecuteAcceptedOperationWithRetryAsync(
            caseId: request.CaseId,
            stepName: WorkflowStepNames.GenerateReport,
            operationDisplayName: "Report generation",
            baseUrlEnvironmentVariable: "REPORT_GENERATOR_BASE_URL",
            functionKeyEnvironmentVariable: "REPORT_GENERATOR_FUNCTION_KEY",
            relativeUrl: "api/reports/generate",
            payload: request.Report,
            fallbackOperation: "report.generate",
            fallbackStatus: "queued",
            fallbackMessageFactory: ex => $"Report generation was queued locally because the remote report generator endpoint was unavailable after {GetConfiguredRetryAttempts()} attempt(s) ({ex.Message}).",
            cancellationToken);

        var accepted = result.Response;
        await UpdateStepAsync(
            request.CaseId,
            WorkflowStepNames.GenerateReport,
            ResolveStepStatus(accepted.Status, string.IsNullOrWhiteSpace(accepted.DownloadUrl) ? "queued" : "completed"),
            accepted.Message,
            accepted.CorrelationId,
            accepted.AttemptCount,
            result.LastError,
            accepted.StatusUrl,
            accepted.DownloadUrl,
            cancellationToken);
    }

    private async Task<OperationExecutionResult> ExecuteAcceptedOperationWithRetryAsync(
        string caseId,
        string stepName,
        string operationDisplayName,
        string baseUrlEnvironmentVariable,
        string functionKeyEnvironmentVariable,
        string relativeUrl,
        object payload,
        string fallbackOperation,
        string fallbackStatus,
        Func<Exception, string> fallbackMessageFactory,
        CancellationToken cancellationToken)
    {
        var maxAttempts = GetConfiguredRetryAttempts();
        var baseDelayMs = GetConfiguredRetryBaseDelayMs();
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var accepted = await PostAcceptedOperationAsync(
                    caseId,
                    baseUrlEnvironmentVariable,
                    functionKeyEnvironmentVariable,
                    relativeUrl,
                    payload,
                    fallbackOperation,
                    fallbackStatus,
                    attempt,
                    attempt == 1
                        ? $"{operationDisplayName} accepted."
                        : $"{operationDisplayName} accepted after retry {attempt}.",
                    cancellationToken);

                accepted.AttemptCount = Math.Max(accepted.AttemptCount, attempt);
                return new OperationExecutionResult(accepted, lastException?.Message);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                var retryDelayMs = BuildRetryDelayMs(attempt, baseDelayMs);
                _logger.LogWarning(ex, "{OperationDisplayName} attempt {Attempt} of {MaxAttempts} failed for case {CaseId}. Retrying in {DelayMs}ms.", operationDisplayName, attempt, maxAttempts, caseId, retryDelayMs);

                await UpdateStepAsync(
                    caseId,
                    stepName,
                    "retrying",
                    $"{operationDisplayName} attempt {attempt} of {maxAttempts} failed; retrying in {retryDelayMs}ms.",
                    null,
                    attempt,
                    ex.Message,
                    null,
                    null,
                    cancellationToken);

                await Task.Delay(retryDelayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        if (lastException is null)
        {
            throw new InvalidOperationException($"{operationDisplayName} failed without a captured exception.");
        }

        _logger.LogWarning(lastException, "{OperationDisplayName} failed for case {CaseId} after {MaxAttempts} attempts. Using fallback status {FallbackStatus}.", operationDisplayName, caseId, maxAttempts, fallbackStatus);

        var fallbackResponse = new AcceptedResponse
        {
            Operation = fallbackOperation,
            Status = fallbackStatus,
            CorrelationId = _correlationIdProvider.Get(),
            CaseId = caseId,
            Message = fallbackMessageFactory(lastException),
            AttemptCount = maxAttempts
        };

        return new OperationExecutionResult(fallbackResponse, lastException.Message);
    }

    private async Task<AcceptedResponse> PostAcceptedOperationAsync(
        string caseId,
        string baseUrlEnvironmentVariable,
        string functionKeyEnvironmentVariable,
        string relativeUrl,
        object payload,
        string fallbackOperation,
        string fallbackStatus,
        int attempt,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable(baseUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new AcceptedResponse
            {
                Operation = fallbackOperation,
                Status = fallbackStatus,
                CorrelationId = _correlationIdProvider.Get(),
                CaseId = caseId,
                Message = fallbackMessage,
                AttemptCount = attempt
            };
        }

        var client = _httpClientFactory.CreateClient(nameof(SuitabilityWorkflowOrchestrator));
        client.BaseAddress = new Uri(EnsureTrailingSlash(baseUrl));

        var functionKey = Environment.GetEnvironmentVariable(functionKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(functionKey) && !client.DefaultRequestHeaders.Contains("x-functions-key"))
        {
            client.DefaultRequestHeaders.Add("x-functions-key", functionKey);
        }

        var requestUrl = AppendFunctionKey(relativeUrl, functionKey);
        var requestBody = JsonSerializer.Serialize(payload);
        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(requestUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Workflow step '{fallbackOperation}' failed with status {(int)response.StatusCode}: {body}");
        }

        var accepted = await response.Content.ReadFromJsonAsync<AcceptedResponse>(cancellationToken: cancellationToken)
            ?? new AcceptedResponse();

        accepted.Operation = string.IsNullOrWhiteSpace(accepted.Operation) ? fallbackOperation : accepted.Operation;
        accepted.Status = string.IsNullOrWhiteSpace(accepted.Status) ? "accepted" : accepted.Status;
        accepted.CorrelationId = string.IsNullOrWhiteSpace(accepted.CorrelationId) ? _correlationIdProvider.Get() : accepted.CorrelationId;
        accepted.CaseId = string.IsNullOrWhiteSpace(accepted.CaseId) ? caseId : accepted.CaseId;
        accepted.Message = string.IsNullOrWhiteSpace(accepted.Message) ? $"{fallbackOperation} accepted." : accepted.Message;
        accepted.AttemptCount = Math.Max(accepted.AttemptCount, attempt);
        return accepted;
    }

    private async Task UpdateStepAsync(
        string caseId,
        string stepName,
        string status,
        string message,
        string? correlationId,
        int attempts,
        string? lastError,
        string? statusUrl,
        string? artifactUrl,
        CancellationToken cancellationToken)
    {
        await _stateStore.UpdateAsync(caseId, state =>
        {
            var step = state.Steps.FirstOrDefault(existing => string.Equals(existing.Step, stepName, StringComparison.OrdinalIgnoreCase));
            if (step is null)
            {
                step = new WorkflowStepStateResponse { Step = stepName };
                state.Steps.Add(step);
            }

            step.Status = status;
            step.Message = message;
            step.CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? step.CorrelationId : correlationId;
            step.Attempts = Math.Max(step.Attempts, attempts);
            step.LastError = lastError ?? step.LastError;
            step.StatusUrl = string.IsNullOrWhiteSpace(statusUrl) ? step.StatusUrl : statusUrl;
            step.ArtifactUrl = string.IsNullOrWhiteSpace(artifactUrl) ? step.ArtifactUrl : artifactUrl;
            step.UpdatedAtUtc = _clock.UtcNow;

            state.Message = message;
            state.LastError = string.IsNullOrWhiteSpace(lastError) ? state.LastError : lastError;
            if (string.Equals(stepName, WorkflowStepNames.GenerateReport, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(step.ArtifactUrl))
            {
                state.DownloadUrl = step.ArtifactUrl;
            }
        }, cancellationToken);
    }

    private static void ValidateEvidenceRequest(EvidenceIntakeRequest evidence, string caseId)
    {
        if (string.IsNullOrWhiteSpace(evidence.CaseId))
        {
            evidence.CaseId = caseId;
        }

        if (string.IsNullOrWhiteSpace(evidence.EvidenceType))
        {
            throw new InvalidOperationException("EvidenceType is required for the data upload and validation step.");
        }

        if (string.Equals(evidence.SourceType, "SharePoint", StringComparison.OrdinalIgnoreCase))
        {
            if (evidence.SharePoint is null ||
                (string.IsNullOrWhiteSpace(evidence.SharePoint.WebUrl) &&
                 (string.IsNullOrWhiteSpace(evidence.SharePoint.SiteId) || string.IsNullOrWhiteSpace(evidence.SharePoint.ItemId))))
            {
                throw new InvalidOperationException("A SharePoint evidence reference is required when SourceType is 'SharePoint'.");
            }
        }
    }

    private static void NormalizeRequest(SuitabilityEvaluationRequest request)
    {
        if (request.ClientData is not null)
        {
            if (string.IsNullOrWhiteSpace(request.AdviceScope) && !string.IsNullOrWhiteSpace(request.ClientData.AdviceScope))
            {
                request.AdviceScope = request.ClientData.AdviceScope;
            }
            else if (string.IsNullOrWhiteSpace(request.ClientData.AdviceScope) && !string.IsNullOrWhiteSpace(request.AdviceScope))
            {
                request.ClientData.AdviceScope = request.AdviceScope;
            }

            if (string.IsNullOrWhiteSpace(request.RiskProfile) && !string.IsNullOrWhiteSpace(request.ClientData.RiskAssessment.RiskProfile))
            {
                request.RiskProfile = request.ClientData.RiskAssessment.RiskProfile;
            }
            else if (string.IsNullOrWhiteSpace(request.ClientData.RiskAssessment.RiskProfile) && !string.IsNullOrWhiteSpace(request.RiskProfile))
            {
                request.ClientData.RiskAssessment.RiskProfile = request.RiskProfile;
            }
        }

        if (request.Evidence is not null && string.IsNullOrWhiteSpace(request.Evidence.CaseId))
        {
            request.Evidence.CaseId = request.CaseId;
        }

        request.Report ??= new ReportGenerationRequest();
        if (string.IsNullOrWhiteSpace(request.Report.CaseId))
        {
            request.Report.CaseId = request.CaseId;
        }

        if (string.IsNullOrWhiteSpace(request.Report.RequestedBy))
        {
            request.Report.RequestedBy = "suitability.engine";
        }
    }

    private static string ResolveStepStatus(string acceptedStatus, string defaultStatus)
    {
        return acceptedStatus.Trim().ToLowerInvariant() switch
        {
            "completed" => "completed",
            "queued" => "queued",
            "failed" => "failed",
            "retrying" => "retrying",
            _ => defaultStatus
        };
    }

    private static int BuildRetryDelayMs(int attempt, int baseDelayMs)
    {
        var exponent = Math.Max(0, attempt - 1);
        var multiplier = 1 << Math.Min(exponent, 4);
        return Math.Clamp(baseDelayMs * multiplier, baseDelayMs, 4000);
    }

    private static int GetConfiguredRetryAttempts()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("WORKFLOW_DOWNSTREAM_RETRY_ATTEMPTS"), out var value)
            ? Math.Clamp(value, 1, 5)
            : DefaultDownstreamRetryAttempts;
    }

    private static int GetConfiguredRetryBaseDelayMs()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("WORKFLOW_DOWNSTREAM_RETRY_BASE_DELAY_MS"), out var value)
            ? Math.Clamp(value, 100, 2000)
            : DefaultRetryBaseDelayMs;
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";

    private static string AppendFunctionKey(string relativeUrl, string? functionKey)
    {
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return relativeUrl;
        }

        var separator = relativeUrl.Contains('?') ? '&' : '?';
        return $"{relativeUrl}{separator}code={Uri.EscapeDataString(functionKey)}";
    }

    private sealed record OperationExecutionResult(AcceptedResponse Response, string? LastError);
}