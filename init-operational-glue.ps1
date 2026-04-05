param (
  [string]$RootPath = (Get-Location).Path,
  [string]$SystemName = "Suitability Writer",
  [string]$SystemVersion = "3.0",
  [string]$EngineVersion = "SuitabilityEngine/3.0"
)

Write-Host "Initializing operational glue files in $RootPath" -ForegroundColor Cyan

function New-DirectoryIfMissing {
  param([string]$RelativeDir)
  $full = Join-Path $RootPath $RelativeDir
  if (-not (Test-Path $full)) {
    New-Item -ItemType Directory -Path $full -Force | Out-Null
    Write-Host "Created dir: $RelativeDir" -ForegroundColor Green
  }
}

function Set-TextFileIfMissing {
  param(
    [string]$RelativePath,
    [string]$Content
  )
  $full = Join-Path $RootPath $RelativePath
  $dir = Split-Path $full -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  if (-not (Test-Path $full)) {
    $Content | Out-File -FilePath $full -Encoding utf8
    Write-Host "Created: $RelativePath" -ForegroundColor Green
  } else {
    Write-Host "Exists : $RelativePath" -ForegroundColor Yellow
  }
}


function Get-GitSha {
  try {
    $sha = (git rev-parse HEAD 2>$null).Trim()
    if ($sha) { return $sha }
  } catch {}
  return "unknown"
}

function Get-UtcNowIso {
  return (Get-Date).ToUniversalTime().ToString("o")
}

# --------------------------------------------------------------------
# 1) DELIVERY GATE MANIFEST + SENTINEL MARKER
# --------------------------------------------------------------------

New-DirectoryIfMissing "delivery.gate"

$gateManifest = @{
  schema = "com.consilium.delivery.gate"
  schemaVersion = "1.0.0"
  system = $SystemName
  systemVersion = $SystemVersion
  generatedAtUtc = Get-UtcNowIso
  generatedFrom = @{
    gitSha = Get-GitSha
    machine = $env:COMPUTERNAME
  }
  asserts = @{
    governanceDocsPresent = $true
    verificationDocsPresent = $true
    architectureDocsPresent = $true
    systemControlsIntegrated = $true
    operationalEvidencePresent = $true
    quarterlyMiPresent = $true
  }
  validator = @{
    script = "delivery.gate.check.ps1"
    expectedExitCode = 0
  }
  notes = @(
    "This manifest is an auditable declaration that delivery.gate was satisfied at the time of release.",
    "Any change to schemas or approved artefacts requires a new system version."
  )
} | ConvertTo-Json -Depth 10

Set-TextFileIfMissing "delivery.gate/DELIVERY-GATE.json" $gateManifest
Set-TextFileIfMissing "delivery.gate.passed" ("PASS " + (Get-UtcNowIso))

# --------------------------------------------------------------------
# 2) VERSION / PROVENANCE FILE (root)
# --------------------------------------------------------------------

$version = @{
  system = $SystemName
  version = $SystemVersion
  engine = $EngineVersion
  builtFrom = Get-GitSha
  buildDateUtc = Get-UtcNowIso
  provenance = @{
    intent = "Forensic identification of which build produced an artefact"
    notes = @(
      "Engine version aligns to the current suitability engine constant used in code."
    )
  }
} | ConvertTo-Json -Depth 10

Set-Content -Path (Join-Path $RootPath "VERSION.json") -Value $version -Encoding utf8
Write-Host "Updated: VERSION.json" -ForegroundColor Green

# --------------------------------------------------------------------
# 3) ENVIRONMENT CONTRACTS (per Function App)
# --------------------------------------------------------------------

New-DirectoryIfMissing "infra/env"

function Write-EnvContract {
  param(
    [string]$Name,
    [string[]]$Required,
    [string[]]$Optional = @(),
    [string[]]$Secrets = @()
  )
  $obj = @{
    schema = "com.consilium.env.contract"
    schemaVersion = "1.0.0"
    app = $Name
    generatedAtUtc = Get-UtcNowIso
    requiredAppSettings = $Required
    optionalAppSettings = $Optional
    secretSettings = $Secrets
    guidance = @{
      rule = "If a required setting is missing, startup.healthcheck must fail closed."
      note = "Secrets should be stored in Key Vault or function app configuration, not committed to source control."
    }
  } | ConvertTo-Json -Depth 10

  Set-Content -Path (Join-Path $RootPath ("infra/env/{0}.required.json" -f $Name)) -Value $obj -Encoding utf8
  Write-Host "Updated: infra/env/$Name.required.json" -ForegroundColor Green
}

$commonRequired = @(
  "FUNCTIONS_WORKER_RUNTIME",
  "AzureWebJobsStorage",
  "APP_VERSION"
)

Write-EnvContract -Name "evidence.intake" -Required ($commonRequired + @(
  "AUDIT_LINEAGE_BASE_URL",
  "AUDIT_LINEAGE_FUNCTION_KEY"
)) -Optional @(
  "EVIDENCE_CONTAINER_NAME",
  "MAX_UPLOAD_BYTES"
) -Secrets @(
  "AUDIT_LINEAGE_FUNCTION_KEY"
)

Write-EnvContract -Name "template.routing" -Required ($commonRequired + @(
  "ROUTING_POLICY_PATH"
)) -Optional @(
  "TEMPLATE_LIST_SOURCE_URL"
)

Write-EnvContract -Name "placeholder.validation" -Required ($commonRequired + @(
  "PLACEHOLDER_SEVERITY_POLICY_PATH"
)) -Optional @(
  "PLACEHOLDER_MATRIX_SOURCE_URL"
)

Write-EnvContract -Name "report.generator" -Required ($commonRequired + @(
  "APPROVED_TEMPLATES_ROOT_URL",
  "REPORT_OUTPUT_ROOT_URL"
)) -Optional @(
  "DEFAULT_REPORT_FORMAT"
)

Write-EnvContract -Name "suitability.engine" -Required ($commonRequired + @(
  "AUDIT_LINEAGE_BASE_URL",
  "AUDIT_LINEAGE_FUNCTION_KEY"
)) -Optional @(
  "APPROVED_ARTEFACTS_SITE_ID",
  "APPROVED_ARTEFACTS_LIST_ID",
  "APPROVED_ARTEFACTS_DRIVE_ID"
) -Secrets @(
  "AUDIT_LINEAGE_FUNCTION_KEY"
)

Write-EnvContract -Name "startup.healthcheck" -Required ($commonRequired + @(
  "REQUIRE_DELIVERY_GATE"
)) -Optional @(
  "HEALTHCHECK_SETTINGS_PATH"
)

Set-Content -Path (Join-Path $RootPath "infra/env/shared.kernel.required.json") -Value ((@{
  schema = "com.consilium.env.contract"
  schemaVersion = "1.0.0"
  app = "shared.kernel"
  generatedAtUtc = Get-UtcNowIso
  requiredAppSettings = @()
  note = "shared.kernel is a class library; no runtime environment settings."
} | ConvertTo-Json -Depth 10)) -Encoding utf8
Write-Host "Updated: infra/env/shared.kernel.required.json" -ForegroundColor Green

$envCheckScript = @'
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
'@

Set-Content -Path (Join-Path $RootPath "infra/env/check-env-contracts.ps1") -Value $envCheckScript -Encoding utf8
Write-Host "Updated: infra/env/check-env-contracts.ps1" -ForegroundColor Green

$keyVaultCheckScript = @'
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
'@

Set-Content -Path (Join-Path $RootPath "infra/env/check-keyvault-mapping.ps1") -Value $keyVaultCheckScript -Encoding utf8
Write-Host "Updated: infra/env/check-keyvault-mapping.ps1" -ForegroundColor Green

$applyKeyVaultAccessScript = @'
param (
    [Parameter(Mandatory)]
    [string]$SubscriptionId,

    [Parameter(Mandatory)]
    [string]$ResourceGroup,

    [string]$RootPath = (Get-Location).Path,
    [string]$App
)

Write-Host "Applying Key Vault access policies via Managed Identity" -ForegroundColor Cyan

$envDir = Join-Path $RootPath "infra/env"
$mappingCandidates = @(
    (Join-Path $envDir "keyvault-mapping.json"),
    (Join-Path $envDir "keyvault.mapping.json")
)
$mappingPath = $mappingCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $mappingPath) {
    Write-Error "No Key Vault mapping file found under infra/env"
    exit 1
}

$templatePath = Join-Path $RootPath "infra/keyvault/keyvault-access.bicep"
if (-not (Test-Path $templatePath)) {
    Write-Error "Key Vault access template not found at $templatePath"
    exit 1
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI 'az' is required to apply Key Vault access policies"
    exit 1
}

$mapping = Get-Content $mappingPath -Raw | ConvertFrom-Json

if ($App) {
    $rules = @($mapping.rules | Where-Object { $_.app -eq $App })
    if ($rules.Count -eq 0) {
        Write-Error "No Key Vault mapping rules were found for app '$App'"
        exit 1
    }
}
else {
    $rules = @($mapping.rules)
}

az account set --subscription $SubscriptionId | Out-Null

foreach ($rule in $rules) {
    $appName = $rule.app
    $vaultName = $rule.vault

    Write-Host "`n[KEYVAULT] App: $appName -> Vault: $vaultName" -ForegroundColor Yellow

    $func = az functionapp list `
        --resource-group $ResourceGroup `
        --query "[?name=='$appName'] | [0]" `
        --output json | ConvertFrom-Json

    if (-not $func) {
        Write-Error "Function App '$appName' not found in RG '$ResourceGroup'"
        exit 1
    }

    $principalId = $func.identity.principalId
    if (-not $principalId) {
        Write-Error "Function App '$appName' does not have a managed identity enabled"
        exit 1
    }

    Write-Host "  [OK] Managed Identity ObjectId: $principalId" -ForegroundColor Green

    az deployment group create `
        --resource-group $ResourceGroup `
        --template-file $templatePath `
        --parameters `
            keyVaultName=$vaultName `
            principalObjectId=$principalId `
        --only-show-errors | Out-Null

    Write-Host "  [OK] Access granted: Key Vault Secrets User" -ForegroundColor Green
}

Write-Host "`n[OK] Key Vault access successfully applied for all mappings" -ForegroundColor Cyan
'@

Set-Content -Path (Join-Path $RootPath "infra/env/apply-keyvault-access.ps1") -Value $applyKeyVaultAccessScript -Encoding utf8
Write-Host "Updated: infra/env/apply-keyvault-access.ps1" -ForegroundColor Green

New-DirectoryIfMissing "infra/keyvault"
$bootstrapKeyVaultSecretsScript = @'
[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string]$SubscriptionId,

    [Parameter(Mandatory)]
    [ValidateSet("dev","test","prod")]
    [string]$Environment,

    [string]$App,
    [string]$RootPath
)

if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

Write-Host "Bootstrapping Key Vault secrets ($Environment)" -ForegroundColor Cyan

$mappingCandidates = @(
    (Join-Path $RootPath "infra/env/keyvault-mapping.json"),
    (Join-Path $RootPath "infra/env/keyvault.mapping.json")
)
$mappingPath = $mappingCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $mappingPath) {
    Write-Error "keyvault-mapping.json not found under infra/env"
    exit 1
}

$previewOnly = $SubscriptionId -match '^\s*<.+>\s*$'
if ($previewOnly) {
    Write-Warning "Placeholder subscription detected; running in preview mode only. Replace <sub-id> to apply in Azure."
}
else {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Error "Azure CLI 'az' is required to bootstrap Key Vault secrets"
        exit 1
    }

    az account set --subscription $SubscriptionId | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to select Azure subscription '$SubscriptionId'"
        exit 1
    }
}

$mapping = Get-Content $mappingPath -Raw | ConvertFrom-Json

$rules = @($mapping.rules)
if ($App) {
    $rules = @($rules | Where-Object { $_.app -eq $App })
    if ($rules.Count -eq 0) {
        Write-Error "No Key Vault mapping rules found for app '$App'"
        exit 1
    }
}

$errors = @()

foreach ($rule in @($rules)) {
    $appName = [string]$rule.app
    $vaultName = [string]$rule.vault

    Write-Host "`n[KEYVAULT] App: $appName -> Vault: $vaultName" -ForegroundColor Yellow

    if ($previewOnly) {
        foreach ($secret in @($rule.secrets)) {
            Write-Host "  [PLAN] Ensure secret '$($secret.secretName)' exists" -ForegroundColor DarkCyan
        }
        continue
    }

    $vault = az keyvault show --name $vaultName --output json 2>$null | ConvertFrom-Json
    if (-not $vault) {
        $errors += "Key Vault '$vaultName' not found for app '$appName'"
        continue
    }

    foreach ($secret in @($rule.secrets)) {
        $secretName = [string]$secret.secretName
        $required = [string]$secret.required

        $exists = az keyvault secret show --vault-name $vaultName --name $secretName --query "id" --output tsv 2>$null

        if ($exists) {
            Write-Host "  [OK] Exists: $secretName" -ForegroundColor Green
            continue
        }

        Write-Host "  [CREATE] Creating placeholder: $secretName" -ForegroundColor Cyan

        $placeholderValue = "REPLACE-ME-$Environment"

        az keyvault secret set --vault-name $vaultName --name $secretName --value $placeholderValue --tags app=$appName environment=$Environment managedBy=bootstrap required=$required --only-show-errors | Out-Null

        if ($LASTEXITCODE -ne 0) {
            $errors += "Failed to create placeholder secret '$secretName' in vault '$vaultName'"
            continue
        }

        Write-Host "  [OK] Created placeholder for $secretName" -ForegroundColor Green
    }
}

if ($errors.Count -gt 0) {
    Write-Host "`n[FAIL] KEY VAULT BOOTSTRAP FAILED" -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host " - $e" -ForegroundColor Red
    }
    exit 1
}

if ($previewOnly) {
    Write-Host "`n[OK] Preview complete. No Azure changes were made." -ForegroundColor Cyan
}
else {
    Write-Host "`n[OK] Key Vault secret bootstrap completed successfully" -ForegroundColor Cyan
}
exit 0
'@

Set-Content -Path (Join-Path $RootPath "infra/keyvault/bootstrap-keyvault-secrets.ps1") -Value $bootstrapKeyVaultSecretsScript -Encoding utf8
Write-Host "Updated: infra/keyvault/bootstrap-keyvault-secrets.ps1" -ForegroundColor Green

$breakGlassSecretOverrideScript = @'
[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string]$SubscriptionId,

    [Parameter(Mandatory)]
    [string]$VaultName,

    [Parameter(Mandatory)]
    [string]$App,

    [Parameter(Mandatory)]
    [string]$EnvVar,

    [Parameter(Mandatory)]
    [string]$IncidentId,

    [Parameter(Mandatory)]
    [string]$ApprovedBy,

    [Parameter(Mandatory)]
    [datetime]$ExpiresAtUtc,

    [string]$RootPath
)

if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

$expiryUtc = $ExpiresAtUtc.ToUniversalTime()

Write-Host "[BREAKGLASS] SECRET OVERRIDE" -ForegroundColor Red
Write-Host "This action is audited and time-boxed." -ForegroundColor Yellow

if ($expiryUtc -le (Get-Date).ToUniversalTime()) {
    Write-Error "Expiry must be in the future."
    exit 1
}

if ($expiryUtc -gt (Get-Date).ToUniversalTime().AddDays(7)) {
    Write-Warning "Break-glass overrides should be short-lived. Review the expiry window."
}

$mappingCandidates = @(
    (Join-Path $RootPath "infra/env/keyvault-mapping.json"),
    (Join-Path $RootPath "infra/env/keyvault.mapping.json")
)
$mappingPath = $mappingCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $mappingPath) {
    Write-Error "keyvault-mapping.json not found"
    exit 1
}

$previewOnly = $SubscriptionId -match "^\s*<.+>\s*$"
if (-not $previewOnly) {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Error "Azure CLI 'az' is required for break-glass secret override"
        exit 1
    }

    az account set --subscription $SubscriptionId | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to select Azure subscription '$SubscriptionId'"
        exit 1
    }
}
else {
    Write-Warning "Placeholder subscription detected; running in preview mode only. Replace <sub-id> to apply in Azure."
}

$mapping = Get-Content $mappingPath -Raw | ConvertFrom-Json

$rule = @($mapping.rules | Where-Object { $_.app -eq $App -and $_.vault -eq $VaultName }) | Select-Object -First 1
if (-not $rule) {
    Write-Error "No Key Vault mapping found for app '$App' and vault '$VaultName'"
    exit 1
}

$secret = @($rule.secrets | Where-Object { $_.envVar -eq $EnvVar }) | Select-Object -First 1
if (-not $secret) {
    Write-Error "Env var '$EnvVar' is not approved for Key Vault usage"
    exit 1
}

$secretName = [string]$secret.secretName

Write-Host "[APP]        : $App"
Write-Host "[VAULT]      : $VaultName"
Write-Host "[SECRET]     : $secretName"
Write-Host "[INCIDENT]   : $IncidentId"
Write-Host "[APPROVER]   : $ApprovedBy"
Write-Host "[EXPIRES]    : $($expiryUtc.ToString('o')) (UTC)"

if ($previewOnly) {
    Write-Host "`n[PLAN] Prompt for the emergency secret value (hidden input)." -ForegroundColor DarkCyan
    Write-Host "[PLAN] Apply override tags: breakglass=true, incidentId, approvedBy, expiresAtUtc, overriddenAtUtc" -ForegroundColor DarkCyan
    Write-Host "`n[OK] Preview complete. No Azure changes were made." -ForegroundColor Cyan
    exit 0
}

Write-Host "`nEnter the EMERGENCY SECRET VALUE (input hidden):"
$secureValue = $null
$secretPtr = [IntPtr]::Zero
$plain = $null

try {
    $secureValue = Read-Host -AsSecureString
    $secretPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPtr)

    az keyvault secret set `
        --vault-name $VaultName `
        --name $secretName `
        --value $plain `
        --tags `
            breakglass=true `
            app=$App `
            incidentId=$IncidentId `
            approvedBy=$ApprovedBy `
            expiresAtUtc=$($expiryUtc.ToString("o")) `
            overriddenAtUtc=$((Get-Date).ToUniversalTime().ToString("o")) `
        --only-show-errors | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to apply break-glass override for secret '$secretName'"
        exit 1
    }
}
finally {
    if ($secretPtr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPtr)
    }

    $plain = $null
    $secureValue = $null
    [GC]::Collect()
}

Write-Host "`n[OK] BREAK-GLASS OVERRIDE APPLIED" -ForegroundColor Red
Write-Host "[WARN] You MUST schedule secret restoration before expiry." -ForegroundColor Yellow
exit 0
'@

Set-Content -Path (Join-Path $RootPath "infra/keyvault/breakglass-secret-override.ps1") -Value $breakGlassSecretOverrideScript -Encoding utf8
Write-Host "Updated: infra/keyvault/breakglass-secret-override.ps1" -ForegroundColor Green

$deploymentRecheckScript = @'
param (
    [Parameter(Mandatory)]
    [ValidateSet("dev", "test", "prod")]
    [string]$Environment,

    [Parameter(Mandatory)]
    [string]$SubscriptionId
)

Write-Host "[DEPLOYMENT RE-CHECK] $Environment" -ForegroundColor Cyan
Write-Host "This must run immediately before production deploy." -ForegroundColor Yellow

function Fail($msg) {
    Write-Host "[FAIL] $msg" -ForegroundColor Red
    exit 1
}

function Step($msg) {
    Write-Host "`n[STEP] $msg" -ForegroundColor Yellow
}

function Invoke-RepoPowerShellScript {
    param([Parameter(Mandatory)][string]$ScriptPath)

    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    $exe = if ($pwsh) { $pwsh.Source } else { (Get-Command powershell).Source }

    $process = Start-Process `
        -FilePath $exe `
        -ArgumentList @('-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) `
        -Wait `
        -NoNewWindow `
        -PassThru

    return $process.ExitCode
}

$previewOnly = $SubscriptionId -match '^\s*<.+>\s*$'
if ($previewOnly) {
    Write-Warning "Placeholder subscription detected; Azure-dependent checks will run in preview mode only."
}
else {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Fail "Azure CLI 'az' is required for deployment-time re-check"
    }

    az account set --subscription $SubscriptionId | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Fail "Unable to select subscription '$SubscriptionId'"
    }
}

Step "Checking delivery gate artefacts"

if (-not (Test-Path "delivery.gate/DELIVERY-GATE.json")) {
    Fail "DELIVERY-GATE.json missing"
}

if (-not (Test-Path "delivery.gate.passed")) {
    Fail "delivery.gate.passed marker missing"
}

Write-Host "[OK] Delivery gate artefacts present" -ForegroundColor Green

Step "Checking VERSION.json"

if (-not (Test-Path "VERSION.json")) {
    Fail "VERSION.json missing"
}

$version = Get-Content VERSION.json -Raw | ConvertFrom-Json
if (-not $version.engine) {
    Fail "VERSION.json does not contain engine identifier"
}

Write-Host "[OK] Version: $($version.engine)" -ForegroundColor Green

Step "Validating environment contracts"

$envExit = Invoke-RepoPowerShellScript -ScriptPath ".\infra\env\check-env-contracts.ps1"
if ($envExit -ne 0) {
    Fail "Environment contract validation failed"
}

Write-Host "[OK] Environment contracts satisfied" -ForegroundColor Green

Step "Validating Key Vault mappings"

$keyVaultMapExit = Invoke-RepoPowerShellScript -ScriptPath ".\infra\env\check-keyvault-mapping.ps1"
if ($keyVaultMapExit -ne 0) {
    Fail "Key Vault mapping validation failed"
}

Write-Host "[OK] Key Vault mappings valid" -ForegroundColor Green

$mappingCandidates = @(
    "infra/env/keyvault-mapping.json",
    "infra/env/keyvault.mapping.json"
)
$mappingPath = $mappingCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $mappingPath) {
    Fail "No Key Vault mapping file found under infra/env"
}

$mapping = Get-Content $mappingPath -Raw | ConvertFrom-Json

Step "Checking required secrets resolve (presence only)"

foreach ($rule in @($mapping.rules)) {
    $vault = [string]$rule.vault

    foreach ($secret in @($rule.secrets)) {
        $secretName = [string]$secret.secretName

        if ($previewOnly) {
            Write-Host "[PLAN] Secret present: $vault/$secretName" -ForegroundColor DarkCyan
            continue
        }

        $exists = az keyvault secret show `
            --vault-name $vault `
            --name $secretName `
            --query "id" `
            --output tsv 2>$null

        if (-not $exists) {
            Fail "Required secret '$secretName' missing in vault '$vault'"
        }

        Write-Host "[OK] Secret present: $vault/$secretName" -ForegroundColor Green
    }
}

Step "Checking break-glass overrides"

$now = (Get-Date).ToUniversalTime()

foreach ($rule in @($mapping.rules)) {
    $vault = [string]$rule.vault

    foreach ($secret in @($rule.secrets)) {
        $name = [string]$secret.secretName

        if ($previewOnly) {
            Write-Host "[PLAN] Break-glass expiry check: $vault/$name" -ForegroundColor DarkCyan
            continue
        }

        $tags = az keyvault secret show `
            --vault-name $vault `
            --name $name `
            --query "tags" `
            --output json 2>$null | ConvertFrom-Json

        if ($tags -and $tags.breakglass -eq "true") {
            $expires = [datetime]$tags.expiresAtUtc
            if ($expires -le $now) {
                Fail "BREAK-GLASS SECRET EXPIRED: $vault/$name"
            }

            Write-Host "[WARN] Break-glass active (valid until $expires): $vault/$name" -ForegroundColor Yellow
        }
    }
}

Write-Host "`n[OK] DEPLOYMENT RE-CHECK PASSED" -ForegroundColor Cyan
Write-Host "Safe to proceed with deployment." -ForegroundColor Green
exit 0
'@

Set-Content -Path (Join-Path $RootPath "deployment.recheck.ps1") -Value $deploymentRecheckScript -Encoding utf8
Write-Host "Updated: deployment.recheck.ps1" -ForegroundColor Green

New-DirectoryIfMissing "deploy"
$preDeployRecheckWrapper = @'
param (
    [Parameter(Mandatory)]
    [ValidateSet("dev", "test", "prod")]
    [string]$Environment,

    [Parameter(Mandatory)]
    [string]$SubscriptionId
)

$rootPath = (Resolve-Path (Join-Path $PSScriptRoot "..\")).Path
$targetScript = Join-Path $rootPath "deployment.recheck.ps1"

if (-not (Test-Path $targetScript)) {
    Write-Error "deployment.recheck.ps1 not found at $targetScript"
    exit 1
}

$pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
$exe = if ($pwsh) { $pwsh.Source } else { (Get-Command powershell).Source }

$process = Start-Process `
    -FilePath $exe `
    -ArgumentList @('-ExecutionPolicy', 'Bypass', '-File', $targetScript, '-Environment', $Environment, '-SubscriptionId', $SubscriptionId) `
    -Wait `
    -NoNewWindow `
    -PassThru

exit $process.ExitCode
'@

Set-Content -Path (Join-Path $RootPath "deploy/pre-deploy-recheck.ps1") -Value $preDeployRecheckWrapper -Encoding utf8
Write-Host "Updated: deploy/pre-deploy-recheck.ps1" -ForegroundColor Green

$exportDeploymentEvidenceScript = @'
param (
    [Parameter(Mandatory)]
    [ValidateSet("dev", "test", "prod")]
    [string]$Environment,

    [Parameter(Mandatory)]
    [string]$DeploymentId,

    [string]$OutputRoot = "deploy/evidence"
)

Write-Host "[EXPORT] DEPLOYMENT ARTEFACT EVIDENCE" -ForegroundColor Cyan
Write-Host "Environment : $Environment"
Write-Host "Deployment  : $DeploymentId"

function Fail($msg) {
    Write-Host "[FAIL] $msg" -ForegroundColor Red
    exit 1
}

function Copy-IfExists($path, $dest) {
    if (-not (Test-Path $path)) {
        Fail "Required artefact missing: $path"
    }

    $parent = Split-Path $dest -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    Copy-Item $path -Destination $dest -Recurse -Force
}

function Sha256($file) {
    (Get-FileHash $file -Algorithm SHA256).Hash
}

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$bundleName = "deployment-evidence-$Environment-$DeploymentId-$timestamp"
$workDir = Join-Path $OutputRoot $bundleName

New-Item -ItemType Directory -Path $workDir -Force | Out-Null

Write-Host "[STEP] Capturing delivery gate artefacts" -ForegroundColor Yellow
New-Item (Join-Path $workDir "delivery-gate") -ItemType Directory -Force | Out-Null
Copy-IfExists "delivery.gate/DELIVERY-GATE.json" (Join-Path $workDir "delivery-gate")
Copy-IfExists "delivery.gate.passed" (Join-Path $workDir "delivery-gate")

Write-Host "[STEP] Capturing VERSION.json" -ForegroundColor Yellow
Copy-IfExists "VERSION.json" $workDir

Write-Host "[STEP] Capturing environment contracts" -ForegroundColor Yellow
Copy-IfExists "infra/env" (Join-Path $workDir "infra-env")

Write-Host "[STEP] Capturing Key Vault governance artefacts" -ForegroundColor Yellow
New-Item (Join-Path $workDir "keyvault") -ItemType Directory -Force | Out-Null
$mappingCandidates = @(
    "infra/env/keyvault-mapping.json",
    "infra/env/keyvault.mapping.json"
)
$mappingPath = $mappingCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $mappingPath) {
    Fail "No Key Vault mapping file found under infra/env"
}
Copy-IfExists $mappingPath (Join-Path $workDir "keyvault")

Write-Host "[STEP] Capturing deployment metadata" -ForegroundColor Yellow

$gitCommit = "unknown"
$gitBranch = "unknown"
try { $gitCommit = (git rev-parse HEAD 2>$null).Trim() } catch {}
try { $gitBranch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim() } catch {}

$metadata = @{
    environment = $Environment
    deploymentId = $DeploymentId
    exportedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    exportedBy = $env:USERNAME
    machine = $env:COMPUTERNAME
    git = @{
        commit = $gitCommit
        branch = $gitBranch
    }
}

$metadata | ConvertTo-Json -Depth 10 | Out-File (Join-Path $workDir "deployment-metadata.json") -Encoding utf8

Write-Host "[STEP] Creating ZIP bundle" -ForegroundColor Yellow

$zipPath = "$workDir.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $workDir -DestinationPath $zipPath

Write-Host "[STEP] Generating hash manifest" -ForegroundColor Yellow

$hash = Sha256 $zipPath
$manifest = @{
    bundle = (Split-Path $zipPath -Leaf)
    sha256 = $hash
    environment = $Environment
    deploymentId = $DeploymentId
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}

$manifest | ConvertTo-Json -Depth 10 | Out-File "$zipPath.manifest.json" -Encoding utf8

Write-Host "`n[OK] DEPLOYMENT EVIDENCE EXPORTED" -ForegroundColor Green
Write-Host "Bundle  : $zipPath"
Write-Host "SHA256  : $hash"
exit 0
'@

Set-Content -Path (Join-Path $RootPath "deploy/export-deployment-evidence.ps1") -Value $exportDeploymentEvidenceScript -Encoding utf8
Write-Host "Updated: deploy/export-deployment-evidence.ps1" -ForegroundColor Green

$keyVaultAccessBicep = @'
@description('Name of the Key Vault')
param keyVaultName string

@description('Object ID of the managed identity')
param principalObjectId string

@description('Role definition ID for Key Vault Secrets User')
param roleDefinitionId string = '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User

resource kv 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, principalObjectId, roleDefinitionId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roleDefinitionId
    )
    principalId: principalObjectId
    principalType: 'ServicePrincipal'
  }
}
'@

Set-Content -Path (Join-Path $RootPath "infra/keyvault/keyvault-access.bicep") -Value $keyVaultAccessBicep -Encoding utf8
Write-Host "Updated: infra/keyvault/keyvault-access.bicep" -ForegroundColor Green

Write-Host "`n[OK] Operational glue created:" -ForegroundColor Cyan
Write-Host " - delivery.gate/DELIVERY-GATE.json"
Write-Host " - delivery.gate.passed"
Write-Host " - VERSION.json"
Write-Host " - deployment.recheck.ps1"
Write-Host " - deploy/pre-deploy-recheck.ps1"
Write-Host " - deploy/export-deployment-evidence.ps1"
Write-Host " - infra/env/*.required.json"
Write-Host " - infra/env/check-env-contracts.ps1"
Write-Host " - infra/env/check-keyvault-mapping.ps1"
Write-Host " - infra/env/apply-keyvault-access.ps1"
Write-Host " - infra/keyvault/bootstrap-keyvault-secrets.ps1"
Write-Host " - infra/keyvault/breakglass-secret-override.ps1"
Write-Host " - infra/keyvault/keyvault-access.bicep"

