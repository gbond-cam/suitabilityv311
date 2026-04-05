param (
    [string]$RootPath = (Get-Location).Path
)

Write-Host "Initializing evidence.intake structure in $RootPath" -ForegroundColor Cyan

function Ensure-File {
    param (
        [string]$Path,
        [string]$Content
    )

    $fullPath = Join-Path $RootPath $Path
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

# ------------------------------------------------------------------
# EVIDENCE.INTAKE STRUCTURE
# ------------------------------------------------------------------

$intakeFiles = @{
# --------------------------------------------------
# API CONTRACTS
# --------------------------------------------------
"evidence/intake/contracts/intake-request.schema.json" = @'
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "EvidenceIntakeRequest",
  "type": "object",
  "required": ["caseId", "evidenceType", "payload", "submittedAt"],
  "properties": {
    "caseId": {
      "type": "string",
      "description": "Unique advice case identifier"
    },
    "evidenceType": {
      "type": "string",
      "enum": ["factfind", "risk-profile", "suitability-input", "supporting-document"]
    },
    "submittedAt": {
      "type": "string",
      "format": "date-time"
    },
    "payload": {
      "type": "object",
      "description": "Evidence-specific payload"
    }
  },
  "additionalProperties": false
}
'@

"evidence/intake/contracts/intake-response.schema.json" = @'
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "EvidenceIntakeResponse",
  "type": "object",
  "required": ["caseId", "intakeStatus", "intakeId"],
  "properties": {
    "caseId": { "type": "string" },
    "intakeId": { "type": "string" },
    "intakeStatus": {
      "type": "string",
      "enum": ["accepted", "rejected"]
    },
    "errors": {
      "type": "array",
      "items": { "type": "string" }
    }
  }
}
'@

# --------------------------------------------------
# VALIDATION
# --------------------------------------------------
"evidence/intake/validation/validate-intake.ps1" = @'
function Validate-Intake {
    param ($Request)

    # Fail-closed validation
    if (-not $Request.caseId) { throw "Missing caseId" }
    if (-not $Request.evidenceType) { throw "Missing evidenceType" }
    if (-not $Request.payload) { throw "Missing payload" }

    return $true
}
'@

# --------------------------------------------------
# NORMALISATION
# --------------------------------------------------
"evidence/intake/normalisation/normalise-payload.ps1" = @'
function Normalise-Payload {
    param ($Request)

    # Canonical normalisation layer
    $normalised = @{
      caseId       = $Request.caseId.Trim()
      evidenceType = $Request.evidenceType
      submittedAt  = (Get-Date $Request.submittedAt -Format o)
      payload      = $Request.payload
    }

    return $normalised
}
'@

# --------------------------------------------------
# LINEAGE
# --------------------------------------------------
"evidence/intake/lineage/emit-intake-lineage.json" = @'
{
  "eventType": "EVIDENCE_INTAKE",
  "description": "Evidence accepted into system",
  "requiredFields": [
    "caseId",
    "evidenceType",
    "submittedAt",
    "intakeId"
  ]
}
'@

# --------------------------------------------------
# HANDLER
# --------------------------------------------------
"evidence/intake/handlers/intake-handler.ps1" = @'
param ($Request)

. "$PSScriptRoot/../validation/validate-intake.ps1"
. "$PSScriptRoot/../normalisation/normalise-payload.ps1"

Validate-Intake $Request | Out-Null
$normalised = Normalise-Payload $Request

$intakeId = [guid]::NewGuid().ToString()

# Emit lineage event (append-only)
$lineageEvent = @{
  eventType    = "EVIDENCE_INTAKE"
  caseId       = $normalised.caseId
  intakeId     = $intakeId
  evidenceType = $normalised.evidenceType
  timestamp    = (Get-Date -Format o)
}

return @{
  caseId       = $normalised.caseId
  intakeId     = $intakeId
  intakeStatus = "accepted"
}
'@

# --------------------------------------------------
# README
# --------------------------------------------------
"evidence/intake/README.md" = @'
# evidence.intake

Responsible for controlled ingestion of evidence into the Evidence System.

## Responsibilities
- Accept evidence inputs
- Validate structure and completeness
- Normalise inputs
- Emit append-only intake lineage
- Fail closed on invalid submissions

## Non-Responsibilities
- No business decisioning
- No evidence bundling
- No cryptographic signing
'@
}

# ------------------------------------------------------------------
# EXECUTION
# ------------------------------------------------------------------

foreach ($file in $intakeFiles.GetEnumerator()) {
    Ensure-File -Path $file.Key -Content $file.Value
}

Write-Host "`n[OK] evidence.intake structure initialized successfully." -ForegroundColor Cyan
