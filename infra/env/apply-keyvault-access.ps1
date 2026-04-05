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
