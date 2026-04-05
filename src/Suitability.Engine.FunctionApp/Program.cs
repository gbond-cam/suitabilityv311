using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Suitability.Engine.FunctionApp.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureOpenApi()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddHttpClient();

        // Core engine
        services.AddSingleton<ISuitabilityEngine, SuitabilityEngine>();

        // Supporting services (stubs by default; replace with real implementations)
        services.AddSingleton<IStatusStore, InMemoryStatusStore>();
        services.AddSingleton<ICaseEvidenceStore, StubCaseEvidenceStore>();
        services.AddSingleton<ISuitabilityArtefactCatalog, StubSuitabilityArtefactCatalog>();
        services.AddSingleton<ITemplateRouter, StubTemplateRouter>();
        services.AddSingleton<IReportGenerator, StubReportGenerator>();
        services.AddSingleton<IPlaceholderValidator, StubPlaceholderValidator>();
        services.AddSingleton<IAuditLineageClient, AuditLineageClient>();
    })
    .Build();

host.Run();
