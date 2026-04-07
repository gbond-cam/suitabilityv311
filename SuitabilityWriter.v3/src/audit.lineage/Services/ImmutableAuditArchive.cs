using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

public sealed class ImmutableAuditArchive
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BlobServiceClient _blobServiceClient;

    public ImmutableAuditArchive(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    public Task<ImmutableArchiveResult> StoreLineageRecordAsync(LineageRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        var blobName = $"{SanitizePathSegment(record.CaseId)}/logs/{record.TimestampUtc:yyyy/MM/dd}/{SanitizePathSegment(record.EventId)}.json";
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["caseId"] = record.CaseId,
            ["stage"] = record.Stage,
            ["action"] = record.Action,
            ["performedBy"] = string.IsNullOrWhiteSpace(record.PerformedBy) ? "system" : record.PerformedBy,
            ["recordType"] = "lineage-event"
        };

        var containerName = Environment.GetEnvironmentVariable("IMMUTABLE_AUDIT_LOG_CONTAINER_NAME")
            ?? "audit-lineage-immutable";

        return StoreJsonAsync(containerName, blobName, record, metadata, ct);
    }

    public Task<ImmutableArchiveResult> StoreComplianceReportAsync(string caseId, object report, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentNullException.ThrowIfNull(report);

        var generatedAtUtc = DateTimeOffset.UtcNow;
        var blobName = $"{SanitizePathSegment(caseId)}/reports/{generatedAtUtc:yyyy/MM/dd}/compliance-audit-{generatedAtUtc:yyyyMMddTHHmmssfff}.json";
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["caseId"] = caseId,
            ["recordType"] = "compliance-audit-report"
        };

        var containerName = Environment.GetEnvironmentVariable("COMPLIANCE_AUDIT_REPORT_CONTAINER_NAME")
            ?? "compliance-audit-reports";

        return StoreJsonAsync(containerName, blobName, report, metadata, ct);
    }

    private async Task<ImmutableArchiveResult> StoreJsonAsync<T>(string containerName, string blobName, T payload, IDictionary<string, string> metadata, CancellationToken ct)
    {
        var sanitizedContainerName = SanitizeContainerName(containerName);
        var container = _blobServiceClient.GetBlobContainerClient(sanitizedContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var retentionUntilUtc = DateTimeOffset.UtcNow.AddDays(GetRetentionDays());

        var metadataWithRetention = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["sha256"] = sha256,
            ["retentionUntilUtc"] = retentionUntilUtc.ToString("O")
        };

        var blobClient = container.GetBlobClient(blobName);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = "application/json; charset=utf-8"
            },
            Metadata = metadataWithRetention,
            Conditions = new BlobRequestConditions
            {
                IfNoneMatch = ETag.All
            }
        };

        var created = false;

        try
        {
            await using var stream = new MemoryStream(bytes, writable: false);
            await blobClient.UploadAsync(stream, uploadOptions, ct);
            created = true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            created = false;
        }

        return new ImmutableArchiveResult(sanitizedContainerName, blobName, sha256, retentionUntilUtc, created);
    }

    private static int GetRetentionDays()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("IMMUTABLE_AUDIT_RETENTION_DAYS"), out var retentionDays) && retentionDays > 0)
        {
            return retentionDays;
        }

        return 2555;
    }

    private static string SanitizeContainerName(string value)
    {
        var sanitized = string.Concat(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
        if (sanitized.Length < 3)
        {
            sanitized = "audit-archive";
        }

        return sanitized.Length > 63 ? sanitized[..63].Trim('-') : sanitized;
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
    }
}

public sealed record ImmutableArchiveResult(
    string ContainerName,
    string BlobName,
    string Sha256,
    DateTimeOffset RetentionUntilUtc,
    bool Created);
