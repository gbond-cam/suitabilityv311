using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Suitability.Engine.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureOpenApi()
    .ConfigureServices(services =>
    {
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        services.AddHttpClient();
        services.AddHttpClient<SharedAuditLineageClient>(client =>
        {
            var baseUrl = Environment.GetEnvironmentVariable("AUDIT_LINEAGE_BASE_URL");
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/");
            }

            client.Timeout = TimeSpan.FromSeconds(5);

            var functionKey = Environment.GetEnvironmentVariable("AUDIT_LINEAGE_FUNCTION_KEY");
            if (!string.IsNullOrWhiteSpace(functionKey))
            {
                client.DefaultRequestHeaders.Add("x-functions-key", functionKey);
            }
        });
        services.AddSingleton<ILineageRecorder>(sp =>
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIT_LINEAGE_BASE_URL"))
                ? new NoOpLineageRecorder()
                : sp.GetRequiredService<SharedAuditLineageClient>());
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();
        services.AddSingleton<IFailClosedPolicy, FailClosedPolicy>();
        services.AddSingleton<IApiAuthorizationService, ApiAuthorizationService>();
        services.AddSingleton<ICaseWorkflowStateStore>(sp =>
        {
            var clock = sp.GetRequiredService<ISystemClock>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var storageMode = Environment.GetEnvironmentVariable("WORKFLOW_STATE_STORAGE_MODE");

            if (string.Equals(storageMode, "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                return new InMemoryCaseWorkflowStateStore(clock);
            }

            var filePath = Environment.GetEnvironmentVariable("WORKFLOW_STATE_FILE_PATH");
            var tableConnection = Environment.GetEnvironmentVariable("WORKFLOW_STATE_TABLES_CONNECTION")
                ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            var tableName = Environment.GetEnvironmentVariable("WORKFLOW_STATE_TABLE_NAME") ?? "SuitabilityWorkflowState";

            if (!string.IsNullOrWhiteSpace(tableConnection) &&
                !string.Equals(tableConnection, "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
            {
                return new AzureTableCaseWorkflowStateStore(
                    new TableServiceClient(tableConnection),
                    tableName,
                    clock,
                    loggerFactory.CreateLogger<AzureTableCaseWorkflowStateStore>());
            }

            var resolvedFilePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuitabilityWriter", "suitability-workflow-state.json")
                : filePath;

            return new JsonFileCaseWorkflowStateStore(
                resolvedFilePath,
                clock,
                loggerFactory.CreateLogger<JsonFileCaseWorkflowStateStore>());
        });
        services.AddSingleton<IClientDataIntakeStore>(sp => ClientDataIntakeStoreFactory.Create(sp));
        services.AddSingleton<ISuitabilityWorkflowOrchestrator, SuitabilityWorkflowOrchestrator>();
    })
    .Build();

host.Run();