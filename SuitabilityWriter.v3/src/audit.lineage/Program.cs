using System.Security.Cryptography;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

static string? ResolveLocalSupportFile(string? configuredPath, string fileName)
{
    var candidates = new[]
    {
        configuredPath,
        Path.Combine(Environment.CurrentDirectory, "keys", fileName),
        Path.Combine(AppContext.BaseDirectory, "keys", fileName),
        Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "keys", fileName)),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "keys", fileName))
    };

    return candidates.FirstOrDefault(path =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path));
}

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
                ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? "UseDevelopmentStorage=true";
            return new BlobServiceClient(conn);
        });

        services.AddSingleton<TableServiceClient>(_ =>
        {
            var tableConn = Environment.GetEnvironmentVariable("AUDIT_LINEAGE_TABLES_CONNECTION")
                ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? "UseDevelopmentStorage=true";
            return new TableServiceClient(tableConn);
        });

        services.AddSingleton<IIdempotencyStore>(sp =>
        {
            var owner = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? Environment.MachineName;
            var tableConn = Environment.GetEnvironmentVariable("AUDIT_LINEAGE_TABLES_CONNECTION")
                ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? "UseDevelopmentStorage=true";

            if (string.Equals(tableConn, "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
            {
                return new InMemoryIdempotencyStore(owner);
            }

            var svc = sp.GetRequiredService<TableServiceClient>();
            return new AzureTableIdempotencyStore(svc, tableName: "LineageIdempotency", lockOwnerId: owner);
        });

        services.AddHttpClient<ArtefactVerifier>();
        services.AddHttpClient<ArtefactBinaryDownloader>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient("tsa", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient("revocation", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<RevocationChecker>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("revocation");
            var url = Environment.GetEnvironmentVariable("REVOCATION_LIST_URL");

            if (string.IsNullOrWhiteSpace(url))
            {
                return null!;
            }

            return new RevocationChecker(http, url);
        });

        services.AddSingleton<BundleEncryptor>();
        services.AddSingleton<ImmutableAuditArchive>();

        services.AddSingleton<ZipSigner>(_ =>
        {
            var vault = Environment.GetEnvironmentVariable("KEYVAULT_URL");
            var keyName = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_NAME");

            if (!string.IsNullOrWhiteSpace(vault) && !string.IsNullOrWhiteSpace(keyName))
            {
                return new ZipSigner(KeyVaultRsaProvider.Load(vault, keyName));
            }

            var pem = Environment.GetEnvironmentVariable("ZIP_SIGNING_PRIVATE_KEY_PEM");
            if (string.IsNullOrWhiteSpace(pem))
            {
                var pemPath = ResolveLocalSupportFile(
                    Environment.GetEnvironmentVariable("ZIP_SIGNING_PRIVATE_KEY_PEM_FILE"),
                    "test-auditor-private.pem");

                if (!string.IsNullOrWhiteSpace(pemPath) && File.Exists(pemPath))
                {
                    pem = File.ReadAllText(pemPath);
                }
            }

            if (string.IsNullOrWhiteSpace(pem))
            {
                return null!;
            }

            var rsa = RSA.Create();
            var normalizedPem = pem.Replace("\\n", "\n");
            var passphrase = Environment.GetEnvironmentVariable("ZIP_SIGNING_PRIVATE_KEY_PASSPHRASE") ?? "LocalTestOnly123!";

            try
            {
                rsa.ImportFromPem(normalizedPem);
            }
            catch (ArgumentException)
            {
                rsa.ImportFromEncryptedPem(normalizedPem, passphrase);
            }

            return new ZipSigner(rsa);
        });

        services.AddSingleton<Rfc3161TimestampClient>(sp =>
        {
            var tsaUrl = Environment.GetEnvironmentVariable("TSA_URL");
            if (string.IsNullOrWhiteSpace(tsaUrl))
            {
                return null!;
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("tsa");
            return new Rfc3161TimestampClient(http, tsaUrl);
        });

        services.AddSingleton<ILineageWriter, BlobAppendLineageStore>();
        services.AddSingleton<ILineageReader, BlobLineageStore>();
    })
    .Build();

host.Run();