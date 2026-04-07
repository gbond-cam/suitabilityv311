using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Responses;

namespace Suitability.Engine.Services;

public interface ICaseWorkflowStateStore
{
    Task<CaseWorkflowStateResponse> GetOrCreateAsync(string caseId, CancellationToken cancellationToken);
    Task<CaseWorkflowStateResponse?> GetAsync(string caseId, CancellationToken cancellationToken);
    Task<CaseWorkflowStateResponse> UpdateAsync(string caseId, Action<CaseWorkflowStateResponse> update, CancellationToken cancellationToken);
}

public static class WorkflowStepNames
{
    public const string UploadAndValidate = "data.upload.validation";
    public const string EvaluateSuitability = "suitability.evaluate";
    public const string GenerateReport = "report.generate";
}

public abstract class CaseWorkflowStateStoreBase : ICaseWorkflowStateStore
{
    protected readonly ISystemClock Clock;

    protected CaseWorkflowStateStoreBase(ISystemClock clock)
    {
        Clock = clock;
    }

    public abstract Task<CaseWorkflowStateResponse?> GetAsync(string caseId, CancellationToken cancellationToken);

    public async Task<CaseWorkflowStateResponse> GetOrCreateAsync(string caseId, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(caseId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var initial = CreateInitialState(caseId);
        await SaveAsync(initial, cancellationToken);
        return Clone(initial);
    }

    public async Task<CaseWorkflowStateResponse> UpdateAsync(string caseId, Action<CaseWorkflowStateResponse> update, CancellationToken cancellationToken)
    {
        var state = await GetOrCreateAsync(caseId, cancellationToken);
        update(state);
        WorkflowUserExperienceHints.Apply(state);
        state.UpdatedAtUtc = Clock.UtcNow;
        await SaveAsync(state, cancellationToken);
        return Clone(state);
    }

    protected abstract Task SaveAsync(CaseWorkflowStateResponse state, CancellationToken cancellationToken);

    protected CaseWorkflowStateResponse CreateInitialState(string caseId)
    {
        var now = Clock.UtcNow;
        var initial = new CaseWorkflowStateResponse
        {
            CaseId = caseId,
            Status = "pending",
            Message = "Workflow has not started.",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Steps =
            [
                new WorkflowStepStateResponse { Step = WorkflowStepNames.UploadAndValidate, Status = "pending", UpdatedAtUtc = now },
                new WorkflowStepStateResponse { Step = WorkflowStepNames.EvaluateSuitability, Status = "pending", UpdatedAtUtc = now },
                new WorkflowStepStateResponse { Step = WorkflowStepNames.GenerateReport, Status = "pending", UpdatedAtUtc = now }
            ]
        };

        WorkflowUserExperienceHints.Apply(initial);
        return initial;
    }

    protected static CaseWorkflowStateResponse Clone(CaseWorkflowStateResponse state)
    {
        return new CaseWorkflowStateResponse
        {
            CaseId = state.CaseId,
            Status = state.Status,
            Message = state.Message,
            CurrentStage = state.CurrentStage,
            ProgressPercentage = state.ProgressPercentage,
            NextPrompt = state.NextPrompt,
            DownloadUrl = state.DownloadUrl,
            SecureDownloadUrl = state.SecureDownloadUrl,
            LastError = state.LastError,
            CreatedAtUtc = state.CreatedAtUtc,
            UpdatedAtUtc = state.UpdatedAtUtc,
            Steps = state.Steps
                .Select(step => new WorkflowStepStateResponse
                {
                    Step = step.Step,
                    Status = step.Status,
                    Message = step.Message,
                    CorrelationId = step.CorrelationId,
                    Attempts = step.Attempts,
                    LastError = step.LastError,
                    StatusUrl = step.StatusUrl,
                    ArtifactUrl = step.ArtifactUrl,
                    UpdatedAtUtc = step.UpdatedAtUtc
                })
                .ToList()
        };
    }
}

public sealed class InMemoryCaseWorkflowStateStore : CaseWorkflowStateStoreBase
{
    private readonly Dictionary<string, CaseWorkflowStateResponse> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();

    public InMemoryCaseWorkflowStateStore(ISystemClock clock) : base(clock)
    {
    }

    public override Task<CaseWorkflowStateResponse?> GetAsync(string caseId, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_states.TryGetValue(caseId, out var state) ? Clone(state) : null);
        }
    }

    protected override Task SaveAsync(CaseWorkflowStateResponse state, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _states[state.CaseId] = Clone(state);
            return Task.CompletedTask;
        }
    }
}

public sealed class JsonFileCaseWorkflowStateStore : CaseWorkflowStateStoreBase
{
    private readonly string _filePath;
    private readonly ILogger<JsonFileCaseWorkflowStateStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonFileCaseWorkflowStateStore(string filePath, ISystemClock clock, ILogger<JsonFileCaseWorkflowStateStore> logger) : base(clock)
    {
        var resolvedPath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuitabilityWriter", filePath);

        _filePath = Path.GetFullPath(resolvedPath);
        _logger = logger;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public override async Task<CaseWorkflowStateResponse?> GetAsync(string caseId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var states = await LoadAllAsync(cancellationToken);
            return states.TryGetValue(caseId, out var state) ? Clone(state) : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    protected override async Task SaveAsync(CaseWorkflowStateResponse state, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var states = await LoadAllAsync(cancellationToken);
            states[state.CaseId] = Clone(state);
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, states, cancellationToken: cancellationToken);
            _logger.LogInformation("Persisted workflow state for case {CaseId} to {FilePath}.", state.CaseId, _filePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, CaseWorkflowStateResponse>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, CaseWorkflowStateResponse>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(_filePath);
        var states = await JsonSerializer.DeserializeAsync<Dictionary<string, CaseWorkflowStateResponse>>(stream, cancellationToken: cancellationToken);
        return states is null
            ? new Dictionary<string, CaseWorkflowStateResponse>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CaseWorkflowStateResponse>(states, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class AzureTableCaseWorkflowStateStore : CaseWorkflowStateStoreBase
{
    private readonly TableClient _table;
    private readonly ILogger<AzureTableCaseWorkflowStateStore> _logger;

    public AzureTableCaseWorkflowStateStore(TableServiceClient tableServiceClient, string tableName, ISystemClock clock, ILogger<AzureTableCaseWorkflowStateStore> logger) : base(clock)
    {
        _table = tableServiceClient.GetTableClient(tableName);
        _table.CreateIfNotExists();
        _logger = logger;
    }

    public override async Task<CaseWorkflowStateResponse?> GetAsync(string caseId, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await _table.GetEntityAsync<WorkflowStateEntity>(PartitionKeyFor(caseId), WorkflowRowKey, cancellationToken: cancellationToken);
            return entity.Value.ToModel();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    protected override async Task SaveAsync(CaseWorkflowStateResponse state, CancellationToken cancellationToken)
    {
        var entity = WorkflowStateEntity.FromModel(state);
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        _logger.LogInformation("Persisted workflow state for case {CaseId} to Azure Table {TableName}.", state.CaseId, _table.Name);
    }

    private static string PartitionKeyFor(string caseId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(caseId));
        return $"case-{Convert.ToHexString(hash[..16])}";
    }

    private const string WorkflowRowKey = "workflow";

    private sealed class WorkflowStateEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = WorkflowRowKey;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
        public string CaseId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string StateJson { get; set; } = string.Empty;

        public static WorkflowStateEntity FromModel(CaseWorkflowStateResponse state)
        {
            return new WorkflowStateEntity
            {
                PartitionKey = PartitionKeyFor(state.CaseId),
                CaseId = state.CaseId,
                Status = state.Status,
                Message = state.Message,
                CreatedAtUtc = state.CreatedAtUtc,
                UpdatedAtUtc = state.UpdatedAtUtc,
                StateJson = JsonSerializer.Serialize(state)
            };
        }

        public CaseWorkflowStateResponse ToModel()
        {
            return JsonSerializer.Deserialize<CaseWorkflowStateResponse>(StateJson) ?? new CaseWorkflowStateResponse
            {
                CaseId = CaseId,
                Status = Status,
                Message = Message,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }
    }
}