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
