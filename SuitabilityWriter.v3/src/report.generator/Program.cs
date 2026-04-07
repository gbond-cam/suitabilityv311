using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
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
    })
    .Build();

host.Run();