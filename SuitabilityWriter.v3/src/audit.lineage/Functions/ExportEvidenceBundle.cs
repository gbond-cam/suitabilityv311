using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public sealed class ExportEvidenceBundle
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly BlobServiceClient _blobServiceClient;
    private readonly ArtefactVerifier _verifier;
    private readonly ArtefactBinaryDownloader _downloader;
    private readonly BundleEncryptor _bundleEncryptor;
    private readonly RevocationChecker? _revocationChecker;
    private readonly ZipSigner? _zipSigner;
    private readonly Rfc3161TimestampClient? _tsa;
    private readonly ILogger<ExportEvidenceBundle> _logger;

    public ExportEvidenceBundle(
        BlobServiceClient blobServiceClient,
        ArtefactVerifier verifier,
        ArtefactBinaryDownloader downloader,
        BundleEncryptor bundleEncryptor,
        IServiceProvider serviceProvider,
        ILogger<ExportEvidenceBundle> logger)
    {
        _blobServiceClient = blobServiceClient;
        _verifier = verifier;
        _downloader = downloader;
        _bundleEncryptor = bundleEncryptor;
        _revocationChecker = serviceProvider.GetService<RevocationChecker>();
        _zipSigner = serviceProvider.GetService<ZipSigner>();
        _tsa = serviceProvider.GetService<Rfc3161TimestampClient>();
        _logger = logger;
    }

    [Function("ExportEvidenceBundle")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "lineage/evidence-bundle")]
        HttpRequestData req)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var caseId = query["caseId"];

            if (string.IsNullOrWhiteSpace(caseId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new
                {
                    status = "BAD_REQUEST",
                    message = "Query parameter 'caseId' is required.",
                    correlationId
                });
                bad.StatusCode = HttpStatusCode.BadRequest;
                return bad;
            }

            var rawText = await LoadRawJsonlAsync(caseId);
            if (rawText is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new
                {
                    status = "NOT_FOUND",
                    message = $"No lineage found for caseId '{caseId}'.",
                    correlationId
                });
                notFound.StatusCode = HttpStatusCode.NotFound;
                return notFound;
            }

            var records = DeserializeRecords(rawText);
            var reconstruction = BuildReconstruction(caseId!, records);

            var ordered = records
                .OrderBy(r => r.TimestampUtc)
                .Select(r => new ReconstructionEventDto(
                    EventId: r.EventId,
                    TimestampUtc: r.TimestampUtc,
                    Stage: r.Stage,
                    Action: r.Action,
                    PerformedBy: r.PerformedBy,
                    Artefact: string.Equals(r.ArtefactName, "N/A", StringComparison.OrdinalIgnoreCase)
                        ? ArtefactDto.NotApplicable
                        : new ArtefactDto(r.ArtefactName, r.ArtefactVersion, r.ArtefactHash),
                    Metadata: ToJsonElement(r.Metadata)
                ))
                .ToList();

            var includeBinaries =
                bool.TryParse(Environment.GetEnvironmentVariable("INCLUDE_ARTEFACT_BINARIES"), out var ib) && ib;

            var maxBytes =
                int.TryParse(Environment.GetEnvironmentVariable("MAX_ARTEFACT_BYTES"), out var mb)
                    ? mb
                    : 20_000_000;

            var failOnError =
                bool.TryParse(Environment.GetEnvironmentVariable("FAIL_ON_ARTEFACT_DOWNLOAD_ERROR"), out var fe) && fe;

            var artefacts = ordered
                .Where(r => !string.Equals(r.Artefact.Name, "N/A", StringComparison.OrdinalIgnoreCase))
                .Select(r => new
                {
                    ArtefactName = r.Artefact.Name,
                    ArtefactVersion = r.Artefact.Version,
                    ArtefactHash = r.Artefact.Hash,
                    SourceUrl =
                        r.Metadata.ValueKind == JsonValueKind.Object &&
                        r.Metadata.TryGetProperty("sourceUrl", out var u)
                            ? u.GetString()
                            : null
                })
                .DistinctBy(a => $"{a.ArtefactName}|{a.ArtefactVersion}|{a.ArtefactHash}")
                .Select(a => (a.ArtefactName, a.ArtefactVersion, a.ArtefactHash, a.SourceUrl))
                .ToList();

            var verification = await _verifier.VerifyAsync(artefacts, CancellationToken.None);

            var failOnMismatch =
                bool.TryParse(
                    Environment.GetEnvironmentVariable("FAIL_ON_HASH_MISMATCH"),
                    out var b) && b;

            if (failOnMismatch && verification.Any(v => v.Status == "Mismatch"))
            {
                throw new InvalidOperationException(
                    "One or more artefacts failed hash verification");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Body = new MemoryStream();

            using (var zip = new ZipArchive(response.Body, ZipArchiveMode.Create, leaveOpen: true))
            {
                var verificationGuide = BuildVerificationGuide(caseId!);
                var publicKeyPem = BuildPublicKeyPem();

                AddText(zip, "raw-lineage.jsonl", rawText);
                AddText(zip, "reconstruction.json", JsonSerializer.Serialize(reconstruction, JsonOpts));
                AddText(zip, "verification.json", JsonSerializer.Serialize(verification, JsonOpts));
                AddText(zip, "VERIFY.md", verificationGuide);
                AddText(zip, "verification-guide.md", verificationGuide);
                AddText(zip, "public-key.pem", publicKeyPem);

                var embedded = new List<object>();

                if (includeBinaries)
                {
                    foreach (var a in artefacts)
                    {
                        if (string.IsNullOrWhiteSpace(a.SourceUrl))
                        {
                            embedded.Add(new { a.ArtefactName, a.ArtefactVersion, status = "Skipped", reason = "No sourceUrl" });
                            continue;
                        }

                        try
                        {
                            var fallbackName = ArtefactBinaryDownloader.SanitizeFileName(a.ArtefactName);

                            var (bytes, fileName) =
                                await _downloader.DownloadAsync(a.SourceUrl!, fallbackName, CancellationToken.None);

                            if (bytes.Length > maxBytes)
                            {
                                embedded.Add(new { a.ArtefactName, a.ArtefactVersion, status = "Skipped", reason = "File too large" });
                                continue;
                            }

                            var computed = ArtefactBinaryDownloader.Sha256Hex(bytes);

                            if (!computed.Equals(a.ArtefactHash, StringComparison.OrdinalIgnoreCase))
                            {
                                embedded.Add(new
                                {
                                    a.ArtefactName,
                                    a.ArtefactVersion,
                                    status = "Mismatch",
                                    expected = a.ArtefactHash,
                                    actual = computed
                                });

                                if (failOnError) throw new InvalidOperationException("Hash mismatch");
                                continue;
                            }

                            var safeName = ArtefactBinaryDownloader.SanitizeFileName(a.ArtefactName);
                            var safeVersion = ArtefactBinaryDownloader.SanitizeFileName(a.ArtefactVersion);
                            var zipPath = $"artefacts/{safeName}/{safeVersion}/{fileName}";

                            var entry = zip.CreateEntry(zipPath, CompressionLevel.Optimal);
                            await using var entryStream = entry.Open();
                            await entryStream.WriteAsync(bytes);

                            embedded.Add(new { a.ArtefactName, a.ArtefactVersion, status = "Embedded", zipPath });
                        }
                        catch (Exception ex)
                        {
                            embedded.Add(new { a.ArtefactName, a.ArtefactVersion, status = "Error", reason = ex.Message });
                            if (failOnError) throw;
                        }
                    }
                }

                var manifest = new
                {
                    caseId,
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    correlationId,
                    artefacts = artefacts.Select(a => new
                    {
                        name = a.ArtefactName,
                        version = a.ArtefactVersion,
                        hash = a.ArtefactHash,
                        sourceUrlPresent = !string.IsNullOrWhiteSpace(a.SourceUrl)
                    }),
                    embedding = embedded,
                    verificationGuidePath = "VERIFY.md",
                    legacyVerificationGuidePath = "verification-guide.md",
                    publicKeyPath = "public-key.pem",
                    signature = new
                    {
                        enabled = _zipSigner is not null,
                        detachedHashPath = _zipSigner is not null ? "signature/bundle.sha256" : null,
                        detachedSignaturePath = _zipSigner is not null ? "signature/bundle.sig" : null,
                        timestampPath = _zipSigner is not null && _tsa is not null ? "signature/timestamp.tsr" : null,
                        keyInventoryPath = _zipSigner is not null ? "signature/signing-keys.json" : null,
                        revocationListPath = _zipSigner is not null ? "signature/revocation-list.json" : null
                    }
                };

                AddText(zip, "manifest.json", JsonSerializer.Serialize(manifest, JsonOpts));
            }

            // Read signed ZIP bytes
            response.Body.Position = 0;
            var zipBytes = ((MemoryStream)response.Body).ToArray();

            if (_zipSigner is not null)
            {
                var hash = _zipSigner.ComputeSha256(zipBytes);
                var signature = _zipSigner.SignHash(hash);
                byte[]? timestamp = null;

                if (_tsa is not null)
                {
                    timestamp = await _tsa.TimestampAsync(hash, CancellationToken.None);
                }

                var signingKeyId = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_NAME")
                    ?? "zip-signing-key-v1";

                if (_revocationChecker is null)
                {
                    throw new InvalidOperationException("REVOCATION_LIST_URL missing for signed evidence bundle export");
                }

                var revocationJson = await _revocationChecker.GetValidatedRevocationListAsync(
                    signingKeyId,
                    CancellationToken.None);

                using var signedStream = new MemoryStream();
                await signedStream.WriteAsync(zipBytes, CancellationToken.None);
                signedStream.Position = 0;

                using (var zip = new ZipArchive(signedStream, ZipArchiveMode.Update, leaveOpen: true))
                {
                    AddText(zip, "signature/bundle.sha256", ZipSigner.ToHex(hash));
                    AddText(zip, "signature/bundle.sig", Convert.ToBase64String(signature));
                    AddText(zip, "signature/signing-keys.json", JsonSerializer.Serialize(BuildSigningKeyInventory(), JsonOpts));
                    AddText(zip, "signature/revocation-list.json", revocationJson);

                    if (timestamp is not null)
                    {
                        AddBinary(zip, "signature/timestamp.tsr", timestamp);
                    }
                }

                zipBytes = signedStream.ToArray();
            }

            // Load auditor public key
            using var rsa = BuildRecipientPublicKey();

            var recipientId = Environment.GetEnvironmentVariable("EVIDENCE_BUNDLE_RECIPIENT_ID")
                ?? "Auditor-UK-FCA-2026";
            var recipientKeyId = Environment.GetEnvironmentVariable("EVIDENCE_BUNDLE_RECIPIENT_KEY_ID")
                ?? recipientId;

            // Encrypt
            var encrypted = _bundleEncryptor.Encrypt(
                zipBytes,
                rsa,
                recipientId,
                recipientKeyId);

            // Return encrypted envelope
            var encryptedResponse = req.CreateResponse(HttpStatusCode.OK);
            encryptedResponse.Headers.Add("Content-Disposition", $"attachment; filename=\"case-{SanitizeFileName(caseId!)}-evidence.enc\"");
            encryptedResponse.Headers.Add("X-Correlation-Id", correlationId);
            await encryptedResponse.WriteAsJsonAsync(encrypted);

            return encryptedResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Evidence bundle export failed. CorrelationId={CorrelationId}",
                correlationId);

            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new
            {
                status = "ERROR",
                message = "Evidence bundle export failed.",
                correlationId
            });
            err.StatusCode = HttpStatusCode.InternalServerError;
            return err;
        }
    }

    private async Task<string?> LoadRawJsonlAsync(string caseId)
    {
        var containerName = Environment.GetEnvironmentVariable("LINEAGE_CONTAINER_NAME") ?? "audit-lineage";
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blob = container.GetBlobClient($"{caseId}.jsonl");

        if (!await blob.ExistsAsync())
        {
            return null;
        }

        var download = await blob.DownloadStreamingAsync();
        using var stream = download.Value.Content;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static List<LineageRecord> DeserializeRecords(string rawText)
    {
        return rawText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<LineageRecord>(line, JsonOpts))
            .Where(record => record is not null)
            .Cast<LineageRecord>()
            .ToList();
    }

    private static CaseReconstructionDto BuildReconstruction(string caseId, IReadOnlyList<LineageRecord> records)
    {
        var ordered = records
            .OrderBy(r => r.TimestampUtc)
            .Select(r => new ReconstructionEventDto(
                EventId: r.EventId,
                TimestampUtc: r.TimestampUtc,
                Stage: r.Stage,
                Action: r.Action,
                PerformedBy: r.PerformedBy,
                Artefact: string.Equals(r.ArtefactName, "N/A", StringComparison.OrdinalIgnoreCase)
                    ? ArtefactDto.NotApplicable
                    : new ArtefactDto(r.ArtefactName, r.ArtefactVersion, r.ArtefactHash),
                Metadata: ToJsonElement(r.Metadata)))
            .ToList();

        var blockedEvent = ordered.FirstOrDefault(e => string.Equals(e.Action, LineageActions.Blocked, StringComparison.OrdinalIgnoreCase));
        var overrideEvent = ordered.FirstOrDefault(e => string.Equals(e.Action, LineageActions.EmergencyOverrideApplied, StringComparison.OrdinalIgnoreCase));
        var adviserApprovedEvent = ordered.FirstOrDefault(e => string.Equals(e.Action, "AdviserApproved", StringComparison.OrdinalIgnoreCase));
        var deliveredEvent = ordered.FirstOrDefault(e => string.Equals(e.Action, "Delivered", StringComparison.OrdinalIgnoreCase));

        var governance = new GovernanceDto(
            IsBlocked: blockedEvent is not null,
            BlockReasonCode: blockedEvent is null ? null : GetMetadataString(blockedEvent.Metadata, "reasonCode", "blockReasonCode", "reason"),
            EmergencyOverrideApplied: overrideEvent is not null,
            IncidentId: overrideEvent is null ? null : GetMetadataString(overrideEvent.Metadata, "incidentId"),
            AdviserApproved: adviserApprovedEvent is not null,
            AdviserId: adviserApprovedEvent?.PerformedBy,
            DeliveredAtUtc: deliveredEvent?.TimestampUtc ?? adviserApprovedEvent?.TimestampUtc);

        var metrics = new MetricsDto(
            TotalEvents: ordered.Count,
            TimeToDelivery: governance.DeliveredAtUtc.HasValue && ordered.Count > 0
                ? ToIso8601Duration(governance.DeliveredAtUtc.Value - ordered.First().TimestampUtc)
                : null,
            TimeToBlock: blockedEvent is not null && ordered.Count > 0
                ? ToIso8601Duration(blockedEvent.TimestampUtc - ordered.First().TimestampUtc)
                : null);

        return new CaseReconstructionDto(caseId, ordered, governance, metrics);
    }

    private static JsonElement ToJsonElement(object? metadata)
    {
        if (metadata is null)
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        return metadata is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(metadata);
    }

    private static string? GetMetadataString(JsonElement metadata, params string[] propertyNames)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (metadata.TryGetProperty(propertyName, out var property) &&
                property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
            }
        }

        return null;
    }

    private static string ToIso8601Duration(TimeSpan duration) => XmlConvert.ToString(duration);

    private static RSA BuildRecipientPublicKey()
    {
        string? pem = null;

        var pemPath = Environment.GetEnvironmentVariable("AUDITOR_PUBLIC_KEY_PEM_FILE");
        if (!string.IsNullOrWhiteSpace(pemPath) && File.Exists(pemPath))
        {
            pem = File.ReadAllText(pemPath);
        }

        if (string.IsNullOrWhiteSpace(pem))
        {
            pem = Environment.GetEnvironmentVariable("AUDITOR_PUBLIC_KEY_PEM");
            if (LooksLikePlaceholderPem(pem))
            {
                pem = null;
            }
        }

        pem ??= Environment.GetEnvironmentVariable("EVIDENCE_BUNDLE_RECIPIENT_PUBLIC_KEY_PEM");

        if (LooksLikePlaceholderPem(pem) || string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException("AUDITOR_PUBLIC_KEY_PEM missing or invalid");
        }

        return RecipientKeyLoader.LoadFromPem(pem.Replace("\\n", "\n"));
    }

    private static bool LooksLikePlaceholderPem(string? pem) =>
        string.IsNullOrWhiteSpace(pem) ||
        pem.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) ||
        pem.Contains("MIIBIjANBgkq...", StringComparison.OrdinalIgnoreCase);

    private static string BuildPublicKeyPem()
    {
        var keyVaultUrl = Environment.GetEnvironmentVariable("KEYVAULT_URL");
        var keyName = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_NAME");
        var keyVersion = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_VERSION");

        if (!string.IsNullOrWhiteSpace(keyVaultUrl) &&
            !string.IsNullOrWhiteSpace(keyName) &&
            !string.IsNullOrWhiteSpace(keyVersion))
        {
            try
            {
                return PublicKeyExporter.ExportRsaPublicKeyPem(keyVaultUrl, keyName, keyVersion);
            }
            catch
            {
                // Fall back to configured/static PEM below for offline or local runs.
            }
        }

        var pemPath = Environment.GetEnvironmentVariable("ZIP_SIGNING_PUBLIC_KEY_PEM_FILE");
        if (string.IsNullOrWhiteSpace(pemPath))
        {
            var defaultPublicKeyPath = Path.Combine(Environment.CurrentDirectory, "keys", "test-auditor-public.pem");
            if (File.Exists(defaultPublicKeyPath))
            {
                pemPath = defaultPublicKeyPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(pemPath) && File.Exists(pemPath))
        {
            return File.ReadAllText(pemPath).Replace("\\n", "\n").Trim();
        }

        var pem = Environment.GetEnvironmentVariable("ZIP_SIGNING_PUBLIC_KEY_PEM");

        if (!string.IsNullOrWhiteSpace(pem))
        {
            return pem.Replace("\\n", "\n").Trim();
        }

        return """
-----BEGIN PUBLIC KEY-----
REPLACE_WITH_ACTUAL_SIGNING_PUBLIC_KEY
-----END PUBLIC KEY-----
""";
    }

    private static string BuildVerificationGuide(string caseId) => $$"""
# Evidence Bundle Verification Guide

This evidence bundle is cryptographically signed, timestamped, and delivered as an encrypted JSON envelope.
Decrypt the `.enc` file to recover the signed ZIP, then follow the steps below to independently verify its integrity and authenticity.
The envelope structure is defined by schema version `1.0.0` at `https://schemas.consilium.co.uk/evidence/encrypted-bundle-envelope/1.0.0.json`.
The schema catalog is published at `https://schemas.consilium.co.uk/evidence/schema-catalog.json`.

---

## 1. Decrypt the encrypted evidence envelope

Use the auditor private RSA key to unwrap the AES key and recover the signed ZIP bytes:

```csharp
// 1. Decrypt AES key with private RSA key
var aesKey = rsaPrivateKey.Decrypt(
    Convert.FromBase64String(encrypted.EncryptedKey),
    RSAEncryptionPadding.OaepSHA256);

// 2. Decrypt ZIP bytes
var ciphertext = Convert.FromBase64String(encrypted.Ciphertext);
var iv = Convert.FromBase64String(encrypted.Iv);
var tag = Convert.FromBase64String(encrypted.AuthTag);
var zipBytes = new byte[ciphertext.Length];

using var aes = new AesGcm(aesKey);
aes.Decrypt(iv, ciphertext, tag, zipBytes);

// 3. Save ZIP and verify signature
File.WriteAllBytes("case.zip", zipBytes);
```

## 2. Verify ZIP Integrity

Compute the SHA-256 hash of the ZIP file:

### Windows (PowerShell)
```powershell
Get-FileHash case-{{SanitizeFileName(caseId)}}-evidence.enc -Algorithm SHA256
```

### Linux/macOS
```bash
sha256sum case-{{SanitizeFileName(caseId)}}-evidence.enc
```

Record the resulting digest in your audit notes and preserve it with the case evidence.

## 3. Review detached verification artefacts

Open the `signature/` folder in the ZIP and inspect:
- `signature/bundle.sha256`
- `signature/bundle.sig`
- `signature/timestamp.tsr` if present
- `signature/signing-keys.json`
- `signature/revocation-list.json`

## 4. Verify the detached signature

Use OpenSSL with the signer public key.
If you need to export the public key from Azure Key Vault first:

```bash
az keyvault key download \
  --vault-name <KEYVAULT_NAME> \
  --name <ZIP_SIGNING_KEY_NAME> \
  --version <ZIP_SIGNING_KEY_VERSION> \
  --encoding PEM \
  --file public-key.pem
```

### Windows PowerShell
```powershell
openssl dgst -sha256 `
  -verify public-key.pem `
  -signature signature/bundle.sig `
  case-{{SanitizeFileName(caseId)}}-evidence.enc
```

### Example PowerShell verification script
```powershell
param (
  [Parameter(Mandatory=$true)]
  [string]$ZipPath
)

Write-Host "Verifying $ZipPath"

$hash = Get-FileHash $ZipPath -Algorithm SHA256
$expected = Get-Content "signature/bundle.sha256"

if ($hash.Hash -ne $expected.Trim()) {
  throw "Hash mismatch"
}

openssl dgst -sha256 `
  -verify public-key.pem `
  -signature signature/bundle.sig `
  $ZipPath

Write-Host "Verification complete ✅"
```

## 5. Confirm signing key status

Check `signature/revocation-list.json` and confirm the signing key used for the bundle is listed as `"Active"`.

## 6. Validate the timestamp

If `signature/timestamp.tsr` is present, validate it with your RFC3161-compatible verification tooling or compliance process.

### OpenSSL
```bash
openssl ts -verify \
  -in signature/timestamp.tsr \
  -data signature/bundle.sha256 \
  -CAfile tsa-root.pem
```
""";

    private static object BuildSigningKeyInventory()
    {
        var issuer = Environment.GetEnvironmentVariable("SIGNING_ISSUER") ?? "Consilium Asset Management";
        var activeKeyId = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_NAME") ?? "zip-signing-key-v2";
        var activeThumbprint = Environment.GetEnvironmentVariable("ZIP_SIGNING_KEY_THUMBPRINT") ?? "B47A11...";
        var revokedKeyId = Environment.GetEnvironmentVariable("ZIP_SIGNING_PREVIOUS_KEY_ID") ?? "zip-signing-key-v1";
        var revokedThumbprint = Environment.GetEnvironmentVariable("ZIP_SIGNING_PREVIOUS_KEY_THUMBPRINT") ?? "A9F3C2...";
        var revokedAtUtc = Environment.GetEnvironmentVariable("ZIP_SIGNING_PREVIOUS_KEY_REVOKED_AT_UTC") ?? "2026-03-18T09:12:00Z";
        var revokeReason = Environment.GetEnvironmentVariable("ZIP_SIGNING_PREVIOUS_KEY_REVOKE_REASON") ?? "Key rotation";

        return new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            issuer,
            keys = new object[]
            {
                new
                {
                    keyId = revokedKeyId,
                    thumbprint = revokedThumbprint,
                    status = "Revoked",
                    revokedAtUtc,
                    reason = revokeReason
                },
                new
                {
                    keyId = activeKeyId,
                    thumbprint = activeThumbprint,
                    status = "Active"
                }
            }
        };
    }

    private static void AddText(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void AddBinary(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
        using var s = entry.Open();
        s.Write(bytes);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return value;
    }
}