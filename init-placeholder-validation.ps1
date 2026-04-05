param (
    [string]$RootPath = (Get-Location).Path
)

Write-Host "Initializing placeholder.validation structure in $RootPath" -ForegroundColor Cyan

function New-FileIfMissing {
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
# PLACEHOLDER.VALIDATION STRUCTURE
# ------------------------------------------------------------------

$validationFiles = @{
# --------------------------------------------------
# CONTRACTS
# --------------------------------------------------
"placeholder/validation/contracts/placeholder-validation-result.schema.json" = @'
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "PlaceholderValidationResult",
  "type": "object",
  "required": ["issues"],
  "properties": {
    "issues": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["placeholder", "severity", "message"],
        "properties": {
          "placeholder": { "type": "string" },
          "severity": {
            "type": "string",
            "enum": ["Block", "High", "Medium", "Low", "Info"]
          },
          "message": { "type": "string" }
        }
      }
    }
  },
  "additionalProperties": false
}
'@

# --------------------------------------------------
# SEVERITY POLICY
# --------------------------------------------------
"placeholder/validation/policies/placeholder-severity-policy.json" = @'
{
  "rules": [
    { "pattern": "{{Client Name}}", "severity": "Block" },
    { "pattern": "{{amount}}", "severity": "Block" },
    { "pattern": "{{ATR}}", "severity": "Block" },
    { "pattern": "{{risk}}", "severity": "High" },
    { "pattern": "{{secondary}}", "severity": "Medium" }
  ]
}
'@

# --------------------------------------------------
# VALIDATOR
# --------------------------------------------------
"placeholder/validation/validators/validate-placeholders.ps1" = @'
param (
    [string]$DocumentText,
    [object]$SeverityPolicy
)

$regex = "\{\{\s*[^}]+\s*\}\}"
$matches = [regex]::Matches($DocumentText, $regex)

$issues = @()

foreach ($m in $matches) {
    $severity = "Info"

    foreach ($rule in $SeverityPolicy.rules) {
        if ($m.Value -like "*$($rule.pattern)*") {
            $severity = $rule.severity
        }
    }

    $issues += @{
        placeholder = $m.Value
        severity    = $severity
        message     = "Unresolved placeholder detected"
    }
}

return @{
    issues = $issues
}
'@

# --------------------------------------------------
# HANDLER
# --------------------------------------------------
"placeholder/validation/handlers/placeholder-validation-handler.ps1" = @'
param (
    [string]$DocumentText
)

$policy = Get-Content "$PSScriptRoot/../policies/placeholder-severity-policy.json" | ConvertFrom-Json
$result = & "$PSScriptRoot/../validators/validate-placeholders.ps1" `
            -DocumentText $DocumentText `
            -SeverityPolicy $policy

$hasBlockers = $result.issues | Where-Object { $_.severity -eq "Block" }

if ($hasBlockers) {
    throw "Placeholder validation failed with BLOCK severity issues"
}

return $result
'@

# --------------------------------------------------
# LINEAGE
# --------------------------------------------------
"placeholder/validation/lineage/emit-placeholder-validation-lineage.json" = @'
{
  "eventType": "PLACEHOLDER_VALIDATION",
  "description": "Template placeholder validation executed",
  "requiredFields": [
    "caseId",
    "templateId",
    "validationResult"
  ]
}
'@

# --------------------------------------------------
# README
# --------------------------------------------------
"placeholder/validation/README.md" = @'
# placeholder.validation

Responsible for validating unresolved placeholders in advice templates.

## Responsibilities
- Detect unresolved {{placeholders}}
- Apply severity-based policy
- Fail closed on Block severity
- Emit append-only validation lineage

## Non-Responsibilities
- No document generation
- No cryptographic signing
- No business suitability logic
'@
}

# ------------------------------------------------------------------
# EXECUTION
# ------------------------------------------------------------------

foreach ($file in $validationFiles.GetEnumerator()) {
    New-FileIfMissing -Path $file.Key -Content $file.Value
}

Write-Host "`n[OK] placeholder.validation structure initialized successfully." -ForegroundColor Cyan

