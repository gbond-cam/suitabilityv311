using Evidence.Intake.Services;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        services.AddHttpClient(nameof(SharePointEvidenceResolver), client =>
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        });

        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();
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
        services.AddSingleton<IFailClosedPolicy, FailClosedPolicy>();
        services.AddSingleton<ISharePointEvidenceResolver, SharePointEvidenceResolver>();
    })
    .Build();

host.Run();