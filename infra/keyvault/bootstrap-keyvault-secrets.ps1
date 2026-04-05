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

$previewOnly = $SubscriptionId -match "^\s*<.+>\s*$"
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
