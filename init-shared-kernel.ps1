param (
    [string]$RootPath = (Get-Location).Path
)

$SolutionRoot = if (Test-Path (Join-Path $RootPath "SuitabilityWriter.v3")) {
    Join-Path $RootPath "SuitabilityWriter.v3"
}
else {
    $RootPath
}

Write-Host "Initializing src/shared.kernel in $SolutionRoot" -ForegroundColor Cyan

function New-FileIfMissing {
    param (
        [string]$Path,
        [string]$Content
    )

    $fullPath = Join-Path $SolutionRoot $Path
    $dir = Split-Path $fullPath -Parent

    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    if (-not (Test-Path $fullPath)) {
        $Content | Out-File -FilePath $fullPath -Encoding utf8
        Write-Host "Created: $Path" -ForegroundColor Green
    }
    else {
        Write-Host "Exists : $Path" -ForegroundColor Yellow
    }
}

# ------------------------------------------------------------
# Shared Kernel layout (class library)
# ------------------------------------------------------------

$files = @{

"src/shared.kernel/shared.kernel.csproj" = @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <AssemblyName>shared.kernel</AssemblyName>
    <RootNamespace>Shared.Kernel</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

</Project>
'@

"src/shared.kernel/README.md" = @'
# shared.kernel

Common, reusable primitives shared across Suitability Writer services.

## Purpose
- Shared models (contracts/DTOs) used across services
- Shared interfaces (clock, identifiers)
- Shared crypto/hash utilities (no secrets)
- Shared constants (schema IDs, event/action names)

## Non-goals
- No Azure Functions host files (no Program.cs, host.json)
- No business workflows (those belong in service apps)
- No storage implementations (those belong in each app Infrastructure)
'@

"src/shared.kernel/GlobalUsings.cs" = @'
global using System.Text.Json;
global using System.Text.Json.Serialization;
'@

# ----------------------------
# Serialization defaults
# ----------------------------
"src/shared.kernel/Serialization/JsonDefaults.cs" = @'
namespace Shared.Kernel.Serialization;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions CamelCaseIndented = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
'@

# ----------------------------
# Common models
# ----------------------------
"src/shared.kernel/Models/ArtefactReference.cs" = @'
namespace Shared.Kernel.Models;

/// <summary>
/// Identifies an approved artefact used in processing (template/schema/matrix).
/// </summary>
public sealed record ArtefactReference(
    string Name,
    string Version,
    string Status,
    string? SourceUrl
);
'@

"src/shared.kernel/Models/LineageRecord.cs" = @'
namespace Shared.Kernel.Models;

/// <summary>
/// Immutable append-only audit lineage record. Metadata is stored raw for audit fidelity.
/// </summary>
public sealed record LineageRecord(
    string EventId,
    string CaseId,
    string Stage,
    string Action,
    string ArtefactName,
    string ArtefactVersion,
    string ArtefactHash,
    string PerformedBy,
    DateTimeOffset TimestampUtc,
    JsonElement Metadata
);
'@

"src/shared.kernel/Models/EncryptedBundleEnvelope.cs" = @'
namespace Shared.Kernel.Models;

/// <summary>
/// Versioned encryption envelope for transmitting an evidence bundle securely.
/// </summary>
public sealed record EncryptedBundleEnvelope
{
    public string Schema { get; init; } = "com.consilium.evidence.envelope";
    public string SchemaVersion { get; init; } = "1.0.0";

    public string EncryptionAlgorithm { get; init; } = "AES-256-GCM";
    public string KeyEncryptionAlgorithm { get; init; } = "RSA-OAEP-SHA256";

    public string RecipientId { get; init; } = default!;
    public string RecipientKeyId { get; init; } = default!;

    public string EncryptedKey { get; init; } = default!;
    public string Iv { get; init; } = default!;
    public string Ciphertext { get; init; } = default!;
    public string AuthTag { get; init; } = default!;

    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string BundleHash { get; init; } = default!;
    public string Signature { get; init; } = default!;
    public string? TimestampToken { get; init; }
}
'@
}

foreach ($file in $files.GetEnumerator()) {
    New-FileIfMissing -Path $file.Key -Content $file.Value
}

Write-Host "`n[OK] src/shared.kernel initialized successfully." -ForegroundColor Cyan
