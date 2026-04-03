using Azure.Storage.Blobs;
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
        services.AddSingleton<IFailClosedPolicy, FailClosedPolicy>();
        services.AddSingleton<BlobServiceClient>(_ =>
        {
            var conn = Environment.GetEnvironmentVariable("AuditLineageStorage")
                ?? "UseDevelopmentStorage=true";
            return new BlobServiceClient(conn);
        });
        services.AddSingleton<ILineageWriter, BlobAppendLineageStore>();
        services.AddSingleton<ILineageReader, BlobLineageStore>();
    })
    .Build();

host.Run();