param (
  [string]$RootPath = (Get-Location).Path
)

function Get-LocalSettingsPath {
  param([string]$AppName)

  $candidates = switch ($AppName) {
    "evidence.intake" { @("SuitabilityWriter.v3/src/evidence.intake/local.settings.json") }
    "placeholder.validation" { @("SuitabilityWriter.v3/src/placeholder.validation/local.settings.json") }
    "report.generator" { @("SuitabilityWriter.v3/src/report.generator/local.settings.json") }
    "startup.healthcheck" { @("SuitabilityWriter.v3/src/startup.healthcheck/local.settings.json") }
    "template.routing" { @("src/Template.Routing.FunctionApp/local.settings.json", "SuitabilityWriter.v3/src/template.routing/local.settings.json") }
    "suitability.engine" { @("src/Suitability.Engine.FunctionApp/local.settings.json", "SuitabilityWriter.v3/src/suitability.engine/local.settings.json") }
    default { @() }
  }

  foreach ($candidate in $candidates) {
    $fullPath = Join-Path $RootPath $candidate
    if (Test-Path $fullPath) { return $fullPath }
  }

  return $null
}

function Read-JsonLenient {
  param([string]$Path)

  $raw = Get-Content $Path -Raw
  try {
    return $raw | ConvertFrom-Json
  }
  catch {
    $normalized = $raw -replace '""', '"'
    return $normalized | ConvertFrom-Json
  }
}

$contractFiles = Get-ChildItem (Join-Path $RootPath "infra/env/*.required.json") | Sort-Object Name
$hasFailures = $false

foreach ($contractFile in $contractFiles) {
  $contract = Read-JsonLenient -Path $contractFile.FullName
  $appName = [string]$contract.app
  $required = @($contract.requiredAppSettings)

  if ($required.Count -eq 0) {
    Write-Host "[OK] $appName - no runtime app settings required" -ForegroundColor Green
    continue
  }

  $settingsPath = Get-LocalSettingsPath -AppName $appName
  if (-not $settingsPath) {
    Write-Host "[FAIL] $appName - no local.settings.json found" -ForegroundColor Red
    $hasFailures = $true
    continue
  }

  $localSettings = Read-JsonLenient -Path $settingsPath
  if (-not $localSettings.Values) {
    Write-Host "[FAIL] $appName - local.settings.json is missing a Values object" -ForegroundColor Red
    $hasFailures = $true
    continue
  }

  $available = @($localSettings.Values.PSObject.Properties.Name)
  $missing = @($required | Where-Object { $_ -notin $available })

  if ($missing.Count -gt 0) {
    Write-Host "[FAIL] $appName - missing required settings: $($missing -join ', ')" -ForegroundColor Red
    $hasFailures = $true
  }
  else {
    Write-Host "[OK] $appName - all required settings present" -ForegroundColor Green
  }
}

if ($hasFailures) {
  exit 1
}

Write-Host "`n[OK] All environment contracts are satisfied." -ForegroundColor Cyan
