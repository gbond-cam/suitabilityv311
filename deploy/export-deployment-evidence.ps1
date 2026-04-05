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
