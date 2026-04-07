using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Models.Requests;

namespace Evidence.Intake.Services;

public interface ISharePointEvidenceResolver
{
    Task<SharePointEvidenceResolutionResult> ResolveAsync(EvidenceIntakeRequest request, CancellationToken cancellationToken);
}

public sealed record SharePointEvidenceResolutionResult(
    string RootItemId,
    string RootItemName,
    string RootWebUrl,
    bool IncludeChildren,
    int FileCount,
    IReadOnlyList<SharePointResolvedFile> Files,
    DateTimeOffset ResolvedAtUtc);

public sealed record SharePointResolvedFile(
    string ItemId,
    string Name,
    string WebUrl,
    string Extension,
    long? SizeBytes,
    DateTimeOffset? LastModifiedUtc);

public sealed class SharePointEvidenceResolver : ISharePointEvidenceResolver
{
    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SharePointEvidenceResolver> _logger;
    private readonly TokenCredential _credential;
    private readonly string[] _allowedHosts;

    public SharePointEvidenceResolver(IHttpClientFactory httpClientFactory, ILogger<SharePointEvidenceResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var tenantId = Environment.GetEnvironmentVariable("SHAREPOINT_TENANT_ID");
        var hasExplicitTenant =
            !string.IsNullOrWhiteSpace(tenantId) &&
            !tenantId.Contains('<') &&
            !tenantId.Contains('>');

        _credential = hasExplicitTenant
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId })
            : new DefaultAzureCredential();

        _allowedHosts = (Environment.GetEnvironmentVariable("SHAREPOINT_ALLOWED_HOSTS") ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<SharePointEvidenceResolutionResult> ResolveAsync(EvidenceIntakeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SharePoint is null)
        {
            throw new InvalidOperationException("SharePoint details are required for sharepoint evidence sources.");
        }

        var sharePoint = request.SharePoint;
        GraphDriveItem rootItem;

        if (!string.IsNullOrWhiteSpace(sharePoint.WebUrl))
        {
            EnsureHostAllowed(sharePoint.WebUrl);
            rootItem = await GetDriveItemFromWebUrlAsync(sharePoint.WebUrl, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(sharePoint.SiteId) &&
                 !string.IsNullOrWhiteSpace(sharePoint.DriveId) &&
                 !string.IsNullOrWhiteSpace(sharePoint.ItemId))
        {
            rootItem = await GetDriveItemByIdAsync(sharePoint.SiteId, sharePoint.DriveId, sharePoint.ItemId, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("A SharePoint webUrl or siteId/driveId/itemId combination is required.");
        }

        var files = new List<SharePointResolvedFile>();

        if (rootItem.IsFile)
        {
            files.Add(ToResolvedFile(rootItem));
        }
        else if (rootItem.IsFolder && sharePoint.IncludeChildren)
        {
            var rootDriveId = !string.IsNullOrWhiteSpace(rootItem.DriveId)
                ? rootItem.DriveId
                : sharePoint.DriveId;

            if (string.IsNullOrWhiteSpace(rootDriveId))
            {
                throw new InvalidOperationException("The SharePoint drive identifier could not be resolved for the supplied folder.");
            }

            await CollectFolderFilesAsync(rootDriveId, rootItem.Id, files, request.Options, cancellationToken);
        }

        files = ApplyOptions(files, request.Options);

        _logger.LogInformation(
            "Resolved {FileCount} SharePoint evidence files from {RootWebUrl}.",
            files.Count,
            rootItem.WebUrl);

        return new SharePointEvidenceResolutionResult(
            rootItem.Id,
            rootItem.Name,
            rootItem.WebUrl,
            sharePoint.IncludeChildren,
            files.Count,
            files,
            DateTimeOffset.UtcNow);
    }

    private async Task<GraphDriveItem> GetDriveItemFromWebUrlAsync(string webUrl, CancellationToken cancellationToken)
    {
        var shareId = BuildShareId(webUrl);
        return await SendGraphRequestAsync<GraphDriveItem>($"shares/{shareId}/driveItem?$select=id,name,webUrl,size,file,folder,parentReference,lastModifiedDateTime", cancellationToken)
               ?? throw new InvalidOperationException("SharePoint item could not be resolved from the supplied URL.");
    }

    private async Task<GraphDriveItem> GetDriveItemByIdAsync(string siteId, string driveId, string itemId, CancellationToken cancellationToken)
    {
        var encodedSite = Uri.EscapeDataString(siteId);
        var encodedDrive = Uri.EscapeDataString(driveId);
        var encodedItem = Uri.EscapeDataString(itemId);

        return await SendGraphRequestAsync<GraphDriveItem>($"sites/{encodedSite}/drives/{encodedDrive}/items/{encodedItem}?$select=id,name,webUrl,size,file,folder,parentReference,lastModifiedDateTime", cancellationToken)
               ?? throw new InvalidOperationException("SharePoint item could not be resolved from the supplied identifiers.");
    }

    private async Task CollectFolderFilesAsync(string driveId, string itemId, List<SharePointResolvedFile> files, EvidenceIntakeOptions? options, CancellationToken cancellationToken)
    {
        var nextUrl = $"drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}/children?$select=id,name,webUrl,size,file,folder,parentReference,lastModifiedDateTime";

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            var page = await SendGraphRequestAsync<GraphDriveItemCollection>(nextUrl, cancellationToken)
                       ?? throw new InvalidOperationException("SharePoint folder contents could not be enumerated.");

            foreach (var item in page.Value)
            {
                if (item.IsFile)
                {
                    files.Add(ToResolvedFile(item));
                }
                else if (item.IsFolder)
                {
                    await CollectFolderFilesAsync(item.DriveId, item.Id, files, options, cancellationToken);
                }

                if (options?.MaxFileCount is int maxFileCount && files.Count > maxFileCount)
                {
                    throw new InvalidOperationException($"SharePoint selection exceeds the configured max file count of {maxFileCount}.");
                }
            }

            nextUrl = page.NextLink;
        }
    }

    private List<SharePointResolvedFile> ApplyOptions(List<SharePointResolvedFile> files, EvidenceIntakeOptions? options)
    {
        IEnumerable<SharePointResolvedFile> filtered = files;

        if (options?.AllowedExtensions is { Length: > 0 })
        {
            var allowed = options.AllowedExtensions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : $".{x.ToLowerInvariant()}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            filtered = filtered.Where(file => allowed.Contains(file.Extension));
        }

        var materialized = filtered.ToList();

        if (options?.MaxFileCount is int maxCount && materialized.Count > maxCount)
        {
            throw new InvalidOperationException($"SharePoint selection exceeds the configured max file count of {maxCount}.");
        }

        if (options?.MaxFileSizeMb is int maxFileSizeMb)
        {
            var maxBytes = maxFileSizeMb * 1024L * 1024L;
            var oversized = materialized.FirstOrDefault(file => file.SizeBytes is > 0 && file.SizeBytes > maxBytes);
            if (oversized is not null)
            {
                throw new InvalidOperationException($"SharePoint file '{oversized.Name}' exceeds the configured max size of {maxFileSizeMb} MB.");
            }
        }

        return materialized;
    }

    private void EnsureHostAllowed(string webUrl)
    {
        if (_allowedHosts.Length == 0)
        {
            return;
        }

        var uri = new Uri(webUrl, UriKind.Absolute);
        var allowed = _allowedHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
        {
            throw new InvalidOperationException($"SharePoint host '{uri.Host}' is not in the configured allowlist.");
        }
    }

    private async Task<T?> SendGraphRequestAsync<T>(string relativeOrAbsoluteUrl, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(SharePointEvidenceResolver));
        client.BaseAddress ??= new Uri("https://graph.microsoft.com/v1.0/");

        var token = await _credential.GetTokenAsync(new TokenRequestContext(GraphScopes), cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeOrAbsoluteUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Graph request failed with status {StatusCode}. Body: {ResponseBody}", (int)response.StatusCode, content);
            throw new InvalidOperationException($"SharePoint Graph request failed with status {(int)response.StatusCode}.");
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static SharePointResolvedFile ToResolvedFile(GraphDriveItem item)
    {
        var extension = Path.GetExtension(item.Name ?? string.Empty);
        return new SharePointResolvedFile(
            item.Id,
            item.Name ?? string.Empty,
            item.WebUrl ?? string.Empty,
            extension.ToLowerInvariant(),
            item.Size,
            item.LastModifiedDateTime);
    }

    private static string BuildShareId(string webUrl)
    {
        var bytes = Encoding.UTF8.GetBytes(webUrl);
        return "u!" + Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('/', '_')
            .Replace('+', '-');
    }

    private sealed class GraphDriveItemCollection
    {
        [JsonPropertyName("value")]
        public List<GraphDriveItem> Value { get; set; } = new();

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private sealed class GraphDriveItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("webUrl")]
        public string WebUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public DateTimeOffset? LastModifiedDateTime { get; set; }

        [JsonPropertyName("file")]
        public object? File { get; set; }

        [JsonPropertyName("folder")]
        public object? Folder { get; set; }

        [JsonPropertyName("parentReference")]
        public GraphParentReference? ParentReference { get; set; }

        public bool IsFile => File is not null;
        public bool IsFolder => Folder is not null;
        public string DriveId => ParentReference?.DriveId ?? string.Empty;
    }

    private sealed class GraphParentReference
    {
        [JsonPropertyName("driveId")]
        public string DriveId { get; set; } = string.Empty;
    }
}