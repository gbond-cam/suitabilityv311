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

$previewOnly = $SubscriptionId -match "^\s*<.+>\s*$"
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
