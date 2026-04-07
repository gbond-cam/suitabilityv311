using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Azure.Storage.Blobs;

public abstract class GoldenPathTestBase
{
    private const string DefaultSigningKeyName = "zip-signing-key-v1";
    private static int _pendingRevocationRequestCount;
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string LocalRevocationListPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "audit.lineage", "keys", "local-revocation-list.json"));
    private static readonly string SeedRunId = Guid.NewGuid().ToString("N");
    private static readonly SemaphoreSlim SeedSync = new(1, 1);
    private static readonly HashSet<string> SeededCases = new(StringComparer.OrdinalIgnoreCase);

    protected static Task RunFullPipeline(string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        return EnsureScenarioSeededAsync(caseId);
    }

    protected static Task RunPipelineWithEmergencyOverride(string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        return EnsureScenarioSeededAsync(caseId);
    }

    protected static Task RunBlockedPipeline(string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        return EnsureScenarioSeededAsync(caseId);
    }

    protected static async Task<string> GetLineageJson(string caseId)
    {
        await EnsureScenarioSeededAsync(caseId);

        using var http = TestHttpClientFactory.Create();
        using var resp = await http.GetAsync($"/api/lineage/reconstruct?caseId={Uri.EscapeDataString(caseId)}");

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Lineage retrieval failed with {(int)resp.StatusCode}: {body}");
        }

        return await resp.Content.ReadAsStringAsync();
    }

    protected static async Task<string> GetComplianceAuditReportJson(string caseId)
    {
        await EnsureScenarioSeededAsync(caseId);

        using var http = TestHttpClientFactory.Create();
        using var resp = await http.GetAsync($"/api/lineage/compliance-report?caseId={Uri.EscapeDataString(caseId)}");

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Compliance audit report retrieval failed with {(int)resp.StatusCode}: {body}");
        }

        return await resp.Content.ReadAsStringAsync();
    }

    protected static async Task<HttpResponseMessage> GetEvidenceBundleResponse(string caseId)
    {
        await EnsureScenarioSeededAsync(caseId);
        PrepareRevocationStateForBundleRequest();
        return await EvidenceBundleClient.GetEvidenceBundleResponse(caseId);
    }

    protected static async Task<string> GetEncryptedEnvelopeJson(string caseId)
    {
        await EnsureScenarioSeededAsync(caseId);
        PrepareRevocationStateForBundleRequest();
        return await EvidenceBundleClient.GetEncryptedEnvelopeJson(caseId);
    }

    protected static async Task<EncryptedEnvelope> GetEncryptedEnvelope(string caseId)
    {
        var json = await GetEncryptedEnvelopeJson(caseId);
        return JsonSerializer.Deserialize<EncryptedEnvelope>(json, EnvelopeJsonOptions)
            ?? throw new InvalidOperationException("Encrypted evidence bundle response could not be deserialized.");
    }

    protected static async Task<byte[]> GetEvidenceBundleZip(string caseId)
    {
        await EnsureScenarioSeededAsync(caseId);
        PrepareRevocationStateForBundleRequest();
        return await EvidenceBundleClient.GetEvidenceBundleZip(caseId);
    }

    protected static async Task RevokeSigningKeyInKeyVault(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        Environment.SetEnvironmentVariable("ZIP_SIGNING_KEY_NAME", keyId);
        Interlocked.Increment(ref _pendingRevocationRequestCount);

        var publishUrl = Environment.GetEnvironmentVariable("REVOCATION_PUBLISH_URL");
        if (!string.IsNullOrWhiteSpace(publishUrl))
        {
            using var http = TestHttpClientFactory.Create();
            using var response = await http.PostAsync(publishUrl, content: null);
            response.EnsureSuccessStatusCode();
        }
    }

    protected static Task RevokeSigningKey(string keyId) => RevokeSigningKeyInKeyVault(keyId);

    private static async Task EnsureScenarioSeededAsync(string caseId)
    {
        if (SeededCases.Contains(caseId))
        {
            return;
        }

        await SeedSync.WaitAsync();
        try
        {
            if (SeededCases.Contains(caseId))
            {
                return;
            }

            WriteLocalRevocationList(DefaultSigningKeyName, isRevoked: false);
            await ResetLocalCaseStateAsync(caseId);

            var records = caseId switch
            {
                "GOLDEN-BLOCKED-001" => BuildBlockedCaseRecords(caseId),
                "GOLDEN-EMERGENCY-001" => BuildEmergencyOverrideRecords(caseId),
                _ => BuildFullLifecycleRecords(caseId)
            };

            using var http = TestHttpClientFactory.Create();
            foreach (var record in records)
            {
                var json = JsonSerializer.Serialize(record);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync("/api/RecordLineageEvent", content);
                response.EnsureSuccessStatusCode();
            }

            SeededCases.Add(caseId);
        }
        finally
        {
            SeedSync.Release();
        }
    }

    private static async Task ResetLocalCaseStateAsync(string caseId)
    {
        var connectionString = Environment.GetEnvironmentVariable("AuditLineageStorage")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? "UseDevelopmentStorage=true";

        var blobServiceClient = new BlobServiceClient(connectionString);
        var container = blobServiceClient.GetBlobContainerClient("audit-lineage");
        await container.CreateIfNotExistsAsync();
        await container.DeleteBlobIfExistsAsync($"{caseId}.jsonl");
    }

    private static void PrepareRevocationStateForBundleRequest()
    {
        var requestedKeyId = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_NAME");
        var keyId = string.IsNullOrWhiteSpace(requestedKeyId) ? DefaultSigningKeyName : requestedKeyId;
        var revoke = Interlocked.Exchange(ref _pendingRevocationRequestCount, 0) > 0;
        WriteLocalRevocationList(keyId, revoke);
    }

    private static void WriteLocalRevocationList(string keyId, bool isRevoked)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LocalRevocationListPath)!);

        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            source = "local-goldenpath-tests",
            keys = new[]
            {
                new
                {
                    keyId,
                    status = isRevoked ? "Revoked" : "Active"
                }
            }
        };

        File.WriteAllText(LocalRevocationListPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
    }

    private static IReadOnlyList<LineageRecord> BuildFullLifecycleRecords(string caseId)
    {
        return new[]
        {
            CreateRecord(
                eventId: $"{caseId}-validation-001",
                caseId: caseId,
                stage: LineageStages.Validation,
                action: "SchemaValidated",
                artefactName: "ClientEvidence",
                artefactVersion: "v3.1",
                artefactHash: "HASH-CLIENT-EVIDENCE-V3_1",
                performedBy: "validation.engine",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T10:00:00Z"),
                metadata: new { channel = "golden-path", note = "schema validated" }),
            CreateRecord(
                eventId: $"{caseId}-routing-001",
                caseId: caseId,
                stage: LineageStages.Routing,
                action: "TemplateSelected",
                artefactName: "ISA_Template",
                artefactVersion: "v2.4",
                artefactHash: "HASH-ISA-TEMPLATE-V2_4",
                performedBy: "template.router",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T10:02:00Z"),
                metadata: new { route = "isa" }),
            CreateRecord(
                eventId: $"{caseId}-generation-001",
                caseId: caseId,
                stage: LineageStages.Generation,
                action: "ReportGenerated",
                artefactName: "PlaceholderSeverity",
                artefactVersion: "v2",
                artefactHash: "HASH-PLACEHOLDER-SEVERITY-V2",
                performedBy: "report.generator",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T10:04:00Z"),
                metadata: new { severity = "medium" }),
            CreateRecord(
                eventId: $"{caseId}-delivery-approval-001",
                caseId: caseId,
                stage: LineageStages.Delivery,
                action: "AdviserApproved",
                artefactName: "SuitabilityReport",
                artefactVersion: "v3.11",
                artefactHash: "HASH-SUITABILITY-REPORT-V3_11",
                performedBy: "adviser.uk",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T10:06:00Z"),
                metadata: new { declaration = "Reviewed and suitable" }),
            CreateRecord(
                eventId: $"{caseId}-delivery-001",
                caseId: caseId,
                stage: LineageStages.Delivery,
                action: "Delivered",
                artefactName: "SuitabilityReport",
                artefactVersion: "v3.11",
                artefactHash: "HASH-SUITABILITY-REPORT-V3_11",
                performedBy: "delivery.bot",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T10:07:00Z"),
                metadata: new { channel = "secure-portal" })
        };
    }

    private static IReadOnlyList<LineageRecord> BuildBlockedCaseRecords(string caseId)
    {
        return new[]
        {
            CreateRecord(
                eventId: $"{caseId}-validation-001",
                caseId: caseId,
                stage: LineageStages.Validation,
                action: "SchemaValidated",
                artefactName: "ClientEvidence",
                artefactVersion: "v3.1",
                artefactHash: "HASH-BLOCKED-EVIDENCE",
                performedBy: "validation.engine",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T11:00:00Z"),
                metadata: new { note = "validation started" }),
            CreateRecord(
                eventId: $"{caseId}-blocked-001",
                caseId: caseId,
                stage: LineageStages.Validation,
                action: LineageActions.Blocked,
                artefactName: "N/A",
                artefactVersion: "N/A",
                artefactHash: "N/A",
                performedBy: "delivery.gate",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T11:01:00Z"),
                metadata: new { reasonCode = "MISSING_CLIENT_CONSENT" }),
            CreateRecord(
                eventId: $"{caseId}-audit-001",
                caseId: caseId,
                stage: LineageStages.Audit,
                action: LineageActions.Blocked,
                artefactName: "N/A",
                artefactVersion: "N/A",
                artefactHash: "N/A",
                performedBy: "audit.logger",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T11:02:00Z"),
                metadata: new { reasonCode = "MISSING_CLIENT_CONSENT", auditNote = "case halted" })
        };
    }

    private static IReadOnlyList<LineageRecord> BuildEmergencyOverrideRecords(string caseId)
    {
        const string incidentId = "INC-EMERGENCY-001";

        return new[]
        {
            CreateRecord(
                eventId: $"{caseId}-override-001",
                caseId: caseId,
                stage: LineageStages.Governance,
                action: LineageActions.EmergencyOverrideApplied,
                artefactName: "N/A",
                artefactVersion: "N/A",
                artefactHash: "N/A",
                performedBy: "supervisor.uk",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T09:00:00Z"),
                metadata: new { incidentId, approvedBy = "supervisor.uk", reason = "client vulnerable but urgent remediation required" }),
            CreateRecord(
                eventId: $"{caseId}-incident-created-001",
                caseId: caseId,
                stage: LineageStages.Governance,
                action: LineageActions.IncidentCreated,
                artefactName: "N/A",
                artefactVersion: "N/A",
                artefactHash: "N/A",
                performedBy: "ops.queue",
                timestampUtc: DateTimeOffset.Parse("2026-04-03T09:10:00Z"),
                metadata: new { incidentId, queue = "governance" }),
            CreateRecord(
                eventId: $"{caseId}-sla-warning-001",
                caseId: caseId,
                stage: LineageStages.Governance,
                action: LineageActions.SlaWarningIssued,
                artefactName: "N/A",
                artefactVersion: "N/A",
                artefactHash: "N/A",
                performedBy: "sla.monitor",
                timestampUtc: DateTimeOffset.Parse("2026-04-07T09:10:00Z"),
                metadata: new { incidentId, level = "Warning" }),
            CreateRecord(
                eventId: $"{caseId}-sla-critical-001",
                caseId: caseId,
                stage: LineageStages.Governance,
                action: LineageActions.SlaWarningIssued,
                artefactName: "N/A",
                artefactVersion: "N/A",
                artefactHash: "N/A",
                performedBy: "sla.monitor",
                timestampUtc: DateTimeOffset.Parse("2026-04-09T09:10:00Z"),
                metadata: new { incidentId, level = "Critical" }),
            CreateRecord(
                eventId: $"{caseId}-incident-closed-001",
                caseId: caseId,
                stage: LineageStages.Governance,
                action: LineageActions.IncidentClosed,
                artefactName: "N/A",
                artefactVersion: "N/A",
                artefactHash: "N/A",
                performedBy: "ops.queue",
                timestampUtc: DateTimeOffset.Parse("2026-04-09T12:00:00Z"),
                metadata: new { incidentId, outcome = "approved" }),
            CreateRecord(
                eventId: $"{caseId}-approval-001",
                caseId: caseId,
                stage: LineageStages.Delivery,
                action: "AdviserApproved",
                artefactName: "SuitabilityReport",
                artefactVersion: "v3.11",
                artefactHash: "HASH-EMERGENCY-REPORT-V3_11",
                performedBy: "adviser.uk",
                timestampUtc: DateTimeOffset.Parse("2026-04-09T13:00:00Z"),
                metadata: new { incidentId, declaration = "override accepted" }),
            CreateRecord(
                eventId: $"{caseId}-delivered-001",
                caseId: caseId,
                stage: LineageStages.Delivery,
                action: "Delivered",
                artefactName: "SuitabilityReport",
                artefactVersion: "v3.11",
                artefactHash: "HASH-EMERGENCY-REPORT-V3_11",
                performedBy: "delivery.bot",
                timestampUtc: DateTimeOffset.Parse("2026-04-09T14:00:00Z"),
                metadata: new { incidentId, channel = "secure-portal" })
        };
    }

    private static LineageRecord CreateRecord(
        string eventId,
        string caseId,
        string stage,
        string action,
        string artefactName,
        string artefactVersion,
        string artefactHash,
        string performedBy,
        DateTimeOffset timestampUtc,
        object metadata)
    {
        return new LineageRecord(
            EventId: $"{eventId}-{SeedRunId}",
            CaseId: caseId,
            Stage: stage,
            Action: action,
            ArtefactName: artefactName,
            ArtefactVersion: artefactVersion,
            ArtefactHash: artefactHash,
            PerformedBy: performedBy,
            TimestampUtc: timestampUtc,
            Metadata: metadata);
    }
}

public static class TestHttpClientFactory
{
    public static HttpClient Create()
    {
        var baseUrl = Environment.GetEnvironmentVariable("GOLDEN_PATH_BASE_URL") ?? "http://localhost:7071";
        return new HttpClient
        {
            BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : $"{baseUrl}/")
        };
    }
}
