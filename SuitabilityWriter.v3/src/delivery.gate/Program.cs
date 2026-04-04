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

        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();
        services.AddHttpClient<AuditLineageClient>(client =>
        {
            var baseUrl = Environment.GetEnvironmentVariable("AUDIT_LINEAGE_BASE_URL")
                ?? "http://localhost:7071/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(5);

            var functionKey = Environment.GetEnvironmentVariable("AUDIT_LINEAGE_FUNCTION_KEY");
            if (!string.IsNullOrWhiteSpace(functionKey))
            {
                client.DefaultRequestHeaders.Add("x-functions-key", functionKey);
            }
        });
        services.AddSingleton<ILineageRecorder>(sp =>
            sp.GetRequiredService<AuditLineageClient>());
        services.AddSingleton<IFailClosedPolicy, FailClosedPolicy>();
    })
    .Build();

host.Run();