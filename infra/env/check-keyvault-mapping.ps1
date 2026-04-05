param (
  [string]$RootPath = (Get-Location).Path
)

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

$mappingPath = Join-Path $RootPath "infra/env/keyvault.mapping.json"
if (-not (Test-Path $mappingPath)) {
  throw "Key Vault mapping file was not found at $mappingPath"
}

$mapping = Read-JsonLenient -Path $mappingPath
if ($mapping.schema -ne "com.consilium.keyvault.mapping") {
  throw "Unexpected schema in ${mappingPath}: $($mapping.schema)"
}

$contractFiles = Get-ChildItem (Join-Path $RootPath "infra/env/*.required.json") | Sort-Object Name
$contractApps = @{}
$hasFailures = $false

foreach ($contractFile in $contractFiles) {
  $contract = Read-JsonLenient -Path $contractFile.FullName
  $appName = [string]$contract.app
  if ([string]::IsNullOrWhiteSpace($appName)) {
    Write-Host "[FAIL] $($contractFile.Name) - missing app name" -ForegroundColor Red
    $hasFailures = $true
    continue
  }

  $contractApps[$appName] = $true

  $required = @($contract.requiredAppSettings | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  $optional = @($contract.optionalAppSettings | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  $declaredSecrets = @($contract.secretSettings | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  $knownSettings = @($required + $optional + $declaredSecrets | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  $appRules = @($mapping.rules | Where-Object { $_.app -eq $appName })

  if ($declaredSecrets.Count -gt 0 -and $appRules.Count -eq 0) {
    Write-Host "[FAIL] $appName - secret settings declared but no Key Vault mapping rule exists" -ForegroundColor Red
    $hasFailures = $true
    continue
  }

  $appFailed = $false
  foreach ($rule in $appRules) {
    if ([string]::IsNullOrWhiteSpace([string]$rule.vault)) {
      Write-Host "[FAIL] $appName - mapping rule is missing a vault name" -ForegroundColor Red
      $hasFailures = $true
      $appFailed = $true
      continue
    }

    foreach ($secret in @($rule.secrets)) {
      $envVar = [string]$secret.envVar
      $secretName = [string]$secret.secretName

      if ([string]::IsNullOrWhiteSpace($envVar) -or [string]::IsNullOrWhiteSpace($secretName)) {
        Write-Host "[FAIL] $appName - mapping entry must include envVar and secretName" -ForegroundColor Red
        $hasFailures = $true
        $appFailed = $true
        continue
      }

      if ($envVar -notin $knownSettings) {
        Write-Host "[FAIL] $appName - mapped env var '$envVar' is not declared in the env contract" -ForegroundColor Red
        $hasFailures = $true
        $appFailed = $true
      }
    }
  }

  if (-not $appFailed) {
    if ($appRules.Count -gt 0) {
      $count = (@($appRules | ForEach-Object { @($_.secrets).Count } | Measure-Object -Sum).Sum)
      Write-Host "[OK] $appName - Key Vault mapping validated ($count secret entries)" -ForegroundColor Green
    }
    else {
      Write-Host "[OK] $appName - no Key Vault mappings required" -ForegroundColor Green
    }
  }
}

foreach ($rule in @($mapping.rules)) {
  $appName = [string]$rule.app
  if (-not [string]::IsNullOrWhiteSpace($appName) -and -not $contractApps.ContainsKey($appName)) {
    Write-Host "[FAIL] $appName - mapping rule exists but no env contract file was found" -ForegroundColor Red
    $hasFailures = $true
  }
}

if ($hasFailures) {
  exit 1
}

Write-Host "`n[OK] Key Vault mappings are consistent with the environment contracts." -ForegroundColor Cyan
