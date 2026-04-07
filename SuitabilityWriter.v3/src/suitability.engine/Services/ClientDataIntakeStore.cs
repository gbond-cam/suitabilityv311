using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;

namespace Suitability.Engine.Services;

public interface IClientDataIntakeStore
{
    Task StoreAsync(string caseId, ClientDataIntakeRequest clientData, CancellationToken cancellationToken);
}

public sealed class JsonFileClientDataIntakeStore : IClientDataIntakeStore
{
    private readonly string _rootPath;
    private readonly ILogger<JsonFileClientDataIntakeStore> _logger;

    public JsonFileClientDataIntakeStore(string rootPath, ILogger<JsonFileClientDataIntakeStore> logger)
    {
        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
        _logger = logger;
    }

    public async Task StoreAsync(string caseId, ClientDataIntakeRequest clientData, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_rootPath, $"{Sanitize(caseId)}.client-intake.json");
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, clientData, cancellationToken: cancellationToken);
        _logger.LogInformation("Stored structured client intake for case {CaseId} at {FilePath}.", caseId, filePath);
    }

    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
}

public sealed class AzureBlobClientDataIntakeStore : IClientDataIntakeStore
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<AzureBlobClientDataIntakeStore> _logger;

    public AzureBlobClientDataIntakeStore(BlobServiceClient blobServiceClient, string containerName, ILogger<AzureBlobClientDataIntakeStore> logger)
    {
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName.ToLowerInvariant());
        _containerClient.CreateIfNotExists(PublicAccessType.None);
        _logger = logger;
    }

    public async Task StoreAsync(string caseId, ClientDataIntakeRequest clientData, CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient($"{Sanitize(caseId)}/client-intake.json");
        await blobClient.UploadAsync(BinaryData.FromObjectAsJson(clientData), overwrite: true, cancellationToken);
        _logger.LogInformation("Stored structured client intake for case {CaseId} in blob {BlobUri}.", caseId, blobClient.Uri);
    }

    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
}

public sealed class DataverseClientDataIntakeStore : IClientDataIntakeStore
{
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _environmentUrl;
    private readonly string _entitySetName;
    private readonly string _caseIdField;
    private readonly string _jsonField;
    private readonly string _nameField;
    private readonly ILogger<DataverseClientDataIntakeStore> _logger;

    public DataverseClientDataIntakeStore(
        HttpClient httpClient,
        TokenCredential credential,
        string environmentUrl,
        string entitySetName,
        string caseIdField,
        string jsonField,
        string nameField,
        ILogger<DataverseClientDataIntakeStore> logger)
    {
        _httpClient = httpClient;
        _credential = credential;
        _environmentUrl = environmentUrl.TrimEnd('/');
        _entitySetName = entitySetName;
        _caseIdField = caseIdField;
        _jsonField = jsonField;
        _nameField = nameField;
        _logger = logger;
    }

    public async Task StoreAsync(string caseId, ClientDataIntakeRequest clientData, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext([$"{_environmentUrl}/.default"]), cancellationToken);
        _httpClient.BaseAddress = new Uri(_environmentUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Remove("OData-Version");
        _httpClient.DefaultRequestHeaders.Remove("OData-MaxVersion");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");

        var payload = new Dictionary<string, object?>
        {
            [_caseIdField] = caseId,
            [_jsonField] = JsonSerializer.Serialize(clientData)
        };

        if (!string.IsNullOrWhiteSpace(_nameField))
        {
            payload[_nameField] = $"Client intake {caseId}";
        }

        var response = await _httpClient.PostAsJsonAsync($"/api/data/v9.2/{_entitySetName}", payload, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Dataverse client intake storage failed with status {(int)response.StatusCode}: {responseBody}");
        }

        _logger.LogInformation("Stored structured client intake for case {CaseId} in Dataverse entity set {EntitySetName}.", caseId, _entitySetName);
    }
}

public static class ClientDataIntakeStoreFactory
{
    public static IClientDataIntakeStore Create(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var provider = Environment.GetEnvironmentVariable("CLIENT_DATA_STORAGE_PROVIDER")?.Trim();

        if (string.Equals(provider, "Dataverse", StringComparison.OrdinalIgnoreCase))
        {
            var environmentUrl = RequireSetting("DATAVERSE_URL");
            var entitySetName = RequireSetting("DATAVERSE_ENTITY_SET_NAME");
            var caseIdField = Environment.GetEnvironmentVariable("DATAVERSE_CASE_ID_FIELD") ?? "cr_caseid";
            var jsonField = Environment.GetEnvironmentVariable("DATAVERSE_JSON_FIELD") ?? "cr_payloadjson";
            var nameField = Environment.GetEnvironmentVariable("DATAVERSE_NAME_FIELD") ?? "cr_name";

            return new DataverseClientDataIntakeStore(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DataverseClientDataIntakeStore)),
                BuildCredential(),
                environmentUrl,
                entitySetName,
                caseIdField,
                jsonField,
                nameField,
                loggerFactory.CreateLogger<DataverseClientDataIntakeStore>());
        }

        var blobConnection = Environment.GetEnvironmentVariable("CLIENT_DATA_STORAGE_CONNECTION")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var containerName = Environment.GetEnvironmentVariable("CLIENT_DATA_BLOB_CONTAINER") ?? "client-intake";

        if (!string.IsNullOrWhiteSpace(blobConnection) &&
            !string.Equals(blobConnection, "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
        {
            return new AzureBlobClientDataIntakeStore(
                new BlobServiceClient(blobConnection),
                containerName,
                loggerFactory.CreateLogger<AzureBlobClientDataIntakeStore>());
        }

        var filePath = Environment.GetEnvironmentVariable("CLIENT_DATA_STORAGE_FILE_PATH");
        var resolvedPath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuitabilityWriter", "client-intake")
            : filePath;

        return new JsonFileClientDataIntakeStore(
            resolvedPath,
            loggerFactory.CreateLogger<JsonFileClientDataIntakeStore>());
    }

    private static TokenCredential BuildCredential()
    {
        var tenantId = Environment.GetEnvironmentVariable("DATAVERSE_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_SECRET");
        var managedIdentityClientId = Environment.GetEnvironmentVariable("DATAVERSE_MANAGED_IDENTITY_CLIENT_ID");

        if (!string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
        {
            return new ClientSecretCredential(tenantId, clientId, clientSecret);
        }

        if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
        {
            return new ManagedIdentityCredential(managedIdentityClientId);
        }

        return new DefaultAzureCredential();
    }

    private static string RequireSetting(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The app setting '{name}' is required when CLIENT_DATA_STORAGE_PROVIDER is set to 'Dataverse'.");
        }

        return value;
    }
}